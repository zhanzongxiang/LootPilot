using TarkovPriceOverlay.Models;

namespace TarkovPriceOverlay.Services;

public sealed class ItemCatalogService
{
    private static readonly TimeSpan DefaultMetadataTimeout = TimeSpan.FromSeconds(8);
    private readonly JsonItemCache _cache;
    private readonly IReadOnlyList<IPriceDataSource> _sources;
    private readonly TimeSpan _metadataTimeout;
    private List<ItemPrice> _items = [];
    private Dictionary<string, IReadOnlyList<ItemPrice>> _fullNames = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, IReadOnlyList<ItemPrice>> _shortNames = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<(ItemPrice Item, string Alias)> _uniqueAliases = [];
    public DateTimeOffset? UpdatedAt { get; private set; }
    public string? LastRefreshError { get; private set; }
    public string CurrentSource { get; private set; } = "无";
    public int Count => _items.Count;

    public ItemCatalogService(JsonItemCache cache, IReadOnlyList<IPriceDataSource> sources)
        : this(cache, sources, DefaultMetadataTimeout) { }

    internal ItemCatalogService(JsonItemCache cache, IReadOnlyList<IPriceDataSource> sources,
        TimeSpan metadataTimeout)
    {
        if (metadataTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(metadataTimeout));
        (_cache, _sources, _metadataTimeout) = (cache, sources, metadataTimeout);
    }

    public async Task<bool> InitializeAsync(CancellationToken ct = default)
    {
        await ReloadCacheAsync(ct);
        return await RefreshAsync(ct);
    }

    public async Task<bool> ReloadCacheAsync(CancellationToken ct = default)
    {
        var cached = await _cache.LoadAsync(ct);
        if (cached is null)
        {
            SetItems([]);
            UpdatedAt = null;
            CurrentSource = "无缓存";
            return false;
        }
        SetItems(cached.Items);
        UpdatedAt = cached.UpdatedAt;
        CurrentSource = cached.Source;
        return true;
    }

    public async Task<bool> RefreshAsync(CancellationToken ct = default)
    {
        var errors = new List<string>();
        for (var sourceIndex = 0; sourceIndex < _sources.Count; sourceIndex++)
        {
            var source = _sources[sourceIndex];
            try
            {
                var fresh = NormalizeItems(await source.FetchItemsAsync(ct));
                if (fresh.Count < source.MinimumExpectedItemCount)
                    throw new InvalidDataException(
                        $"{source.Name} 返回的有效物品仅 {fresh.Count} 件，低于安全阈值 " +
                        $"{source.MinimumExpectedItemCount}，已保留原缓存。");

                // eftarkov carries the current prices and names, while the
                // secondary source can fill in task/hideout usage flags.
                IReadOnlyList<ItemPrice>? metadata = null;
                if (sourceIndex == 0 && _sources.Count > 1)
                    metadata = await TryFetchMetadataAsync(sourceIndex + 1, ct);
                fresh = MergeUsageFlags(fresh, metadata);
                var envelope = new CacheEnvelope(DateTimeOffset.UtcNow, fresh, source.Name);
                await _cache.SaveAsync(envelope, ct);
                SetItems(fresh);
                UpdatedAt = envelope.UpdatedAt;
                CurrentSource = source.Name;
                LastRefreshError = null;
                return true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                errors.Add($"{source.Name}: {ex.Message}");
            }
        }
        LastRefreshError = string.Join(Environment.NewLine, errors);
        return false;
    }

    private List<ItemPrice> MergeUsageFlags(
        List<ItemPrice> fresh, IReadOnlyList<ItemPrice>? metadata)
    {
        var oldById = _items
            .GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
        var metadataById = metadata?
            .GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
        return fresh.Select(item =>
        {
            oldById.TryGetValue(item.Id, out var old);
            ItemPrice? metadataItem = null;
            metadataById?.TryGetValue(item.Id, out metadataItem);
            return item with
            {
                UsedInTasks = item.UsedInTasks || old?.UsedInTasks == true || metadataItem?.UsedInTasks == true,
                UsedInHideout = item.UsedInHideout || old?.UsedInHideout == true || metadataItem?.UsedInHideout == true
            };
        }).ToList();
    }

    private async Task<IReadOnlyList<ItemPrice>?> TryFetchMetadataAsync(
        int startIndex, CancellationToken ct)
    {
        for (var index = startIndex; index < _sources.Count; index++)
        {
            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(_metadataTimeout);
                var metadata = NormalizeItems(
                    await _sources[index].FetchItemsAsync(timeoutCts.Token));
                if (metadata.Count >= _sources[index].MinimumExpectedItemCount) return metadata;
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                // Metadata is supplemental. A price refresh remains valid when
                // the enrichment endpoint is unavailable.
            }
        }
        return null;
    }

    private static List<ItemPrice> NormalizeItems(IEnumerable<ItemPrice> items) => items
        .Where(item => !string.IsNullOrWhiteSpace(item.Id) && !string.IsNullOrWhiteSpace(item.Name))
        .GroupBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
        .Select(group => group.First())
        .ToList();

    private void SetItems(IEnumerable<ItemPrice> items)
    {
        _items = NormalizeItems(items);
        _fullNames = BuildAliasIndex(_items.Select(item => (Normalize(item.Name), item)));
        _shortNames = BuildAliasIndex(_items.Select(item => (Normalize(item.ShortName), item)));
        var aliases = _items
            .SelectMany(item => new[] { (Alias: Normalize(item.Name), Item: item),
                                        (Alias: Normalize(item.ShortName), Item: item) })
            .Where(entry => entry.Alias.Length > 0)
            .GroupBy(entry => entry.Alias, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<ItemPrice>)group
                    .GroupBy(entry => entry.Item.Id, StringComparer.OrdinalIgnoreCase)
                    .Select(entry => entry.First().Item)
                    .ToList(),
                StringComparer.OrdinalIgnoreCase);
        _uniqueAliases = aliases
            .Where(entry => entry.Value.Count == 1)
            .Select(entry => (entry.Value[0], entry.Key))
            .ToList();
    }

    private static Dictionary<string, IReadOnlyList<ItemPrice>> BuildAliasIndex(
        IEnumerable<(string Alias, ItemPrice Item)> aliases) => aliases
        .Where(entry => entry.Alias.Length > 0)
        .GroupBy(entry => entry.Alias, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(
            group => group.Key,
            group => (IReadOnlyList<ItemPrice>)group
                .GroupBy(entry => entry.Item.Id, StringComparer.OrdinalIgnoreCase)
                .Select(entry => entry.First().Item)
                .ToList(),
            StringComparer.OrdinalIgnoreCase);

    public (ItemPrice? Item, double Score) FindBest(string text)
    {
        var normalized = Normalize(text);
        if (string.IsNullOrWhiteSpace(normalized) || _items.Count == 0) return (null, 0);

        // A full name is authoritative even if another item happens to use it
        // as a short name. Shared short names such as PM remain ambiguous.
        if (_fullNames.TryGetValue(normalized, out var fullNameItems))
            return fullNameItems.Count == 1 ? (fullNameItems[0], 1) : (null, 0);
        if (_shortNames.TryGetValue(normalized, out var shortNameItems))
            return shortNameItems.Count == 1 ? (shortNameItems[0], 1) : (null, 0);

        var contained = _uniqueAliases
            .Where(x => (x.Alias.Length >= 4 || x.Alias.Length >= 2 && ContainsCjk(x.Alias)) &&
                        normalized.Contains(x.Alias, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.Alias.Length)
            .FirstOrDefault();
        if (contained.Item is not null) return (contained.Item, 1);

        // Two/three-character OCR fragments are too ambiguous for fuzzy
        // matching, but exact short names such as TT and PP still work above.
        if (normalized.Length <= 3) return (null, 0);
        var ranked = _uniqueAliases
            .Select(x => (x.Item, Score: Similarity(normalized, x.Alias)))
            .GroupBy(x => x.Item.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(x => x.Score).First())
            .OrderByDescending(x => x.Score)
            .Take(2)
            .ToList();
        if (ranked.Count == 0 || ranked[0].Score < 0.70) return (null, 0);
        if (ranked.Count > 1 && ranked[0].Score < 0.90 && ranked[0].Score - ranked[1].Score < 0.07)
            return (null, 0);
        return ranked[0];
    }

    private static string Normalize(string value) =>
        new(value.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    private static bool ContainsCjk(string value) =>
        value.Any(character => character is >= '\u3400' and <= '\u9FFF');

    private static double Similarity(string a, string b)
    {
        if (a.Length == 0 || b.Length == 0) return 0;
        var d = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (var j = 0; j <= b.Length; j++) d[0, j] = j;
        for (var i = 1; i <= a.Length; i++)
        for (var j = 1; j <= b.Length; j++)
            d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
        return 1d - (double)d[a.Length, b.Length] / Math.Max(a.Length, b.Length);
    }
}
