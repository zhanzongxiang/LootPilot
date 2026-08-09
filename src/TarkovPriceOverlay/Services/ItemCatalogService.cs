using TarkovPriceOverlay.Models;

namespace TarkovPriceOverlay.Services;

public sealed class ItemCatalogService
{
    private readonly JsonItemCache _cache;
    private readonly IReadOnlyList<IPriceDataSource> _sources;
    private List<ItemPrice> _items = [];
    public DateTimeOffset? UpdatedAt { get; private set; }
    public string? LastRefreshError { get; private set; }
    public string CurrentSource { get; private set; } = "无";
    public int Count => _items.Count;

    public ItemCatalogService(JsonItemCache cache, IReadOnlyList<IPriceDataSource> sources)
        => (_cache, _sources) = (cache, sources);

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
            _items = [];
            UpdatedAt = null;
            CurrentSource = "无缓存";
            return false;
        }
        _items = cached.Items;
        UpdatedAt = cached.UpdatedAt;
        CurrentSource = cached.Source;
        return true;
    }

    public async Task<bool> RefreshAsync(CancellationToken ct = default)
    {
        var errors = new List<string>();
        foreach (var source in _sources)
        {
            try
            {
                var fresh = MergeUsageFlags(await source.FetchItemsAsync(ct));
                var envelope = new CacheEnvelope(DateTimeOffset.UtcNow, fresh, source.Name);
                await _cache.SaveAsync(envelope, ct);
                _items = fresh;
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

    private List<ItemPrice> MergeUsageFlags(List<ItemPrice> fresh)
    {
        if (_items.Count == 0) return fresh;
        var oldById = _items.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);
        return fresh.Select(item =>
        {
            if (!oldById.TryGetValue(item.Id, out var old)) return item;
            return item with
            {
                UsedInTasks = item.UsedInTasks || old.UsedInTasks,
                UsedInHideout = item.UsedInHideout || old.UsedInHideout
            };
        }).ToList();
    }

    public (ItemPrice? Item, double Score) FindBest(string text)
    {
        var normalized = Normalize(text);
        if (string.IsNullOrWhiteSpace(normalized) || _items.Count == 0) return (null, 0);
        var candidates = _items.SelectMany(item => new[]
        {
            (Item: item, Name: Normalize(item.Name)),
            (Item: item, Name: Normalize(item.ShortName))
        }).Where(x => x.Name.Length > 0).ToList();

        // Prefer a complete OCR/name match before substring matching. Without
        // this, "VOG-17" can incorrectly select the shorter "G17" alias.
        var exact = candidates
            .Where(x => normalized == x.Name)
            .OrderByDescending(x => x.Name.Length)
            .FirstOrDefault();
        if (exact.Item is not null) return (exact.Item, 1);

        exact = candidates
            .Where(x => (x.Name.Length >= 4 || x.Name.Length >= 2 && ContainsCjk(x.Name)) &&
                        normalized.Contains(x.Name))
            .OrderByDescending(x => x.Name.Length)
            .FirstOrDefault();
        if (exact.Item is not null) return (exact.Item, 1);

        // Two/three-character OCR fragments are too ambiguous for fuzzy
        // matching, but exact short names such as TT and PP still work above.
        if (normalized.Length <= 3) return (null, 0);
        var ranked = candidates
            .Select(x => (x.Item, Score: Similarity(normalized, x.Name)))
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
