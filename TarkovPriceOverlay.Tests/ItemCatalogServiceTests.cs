using TarkovPriceOverlay.Models;
using TarkovPriceOverlay.Services;

namespace TarkovPriceOverlay.Tests;

public sealed class ItemCatalogServiceTests
{
    [Fact]
    public async Task AmbiguousShortNameDoesNotSelectFirstItem()
    {
        using var temp = new TemporaryDirectory();
        var source = new TestPriceSource(
            new ItemPrice("one", "Makarov PM pistol", "PM", 2, 1, 10_000, 5_000, "Prapor", false, false),
            new ItemPrice("two", "PM magazine", "PM", 1, 1, 8_000, 4_000, "Prapor", false, false));
        var catalog = new ItemCatalogService(new JsonItemCache(System.IO.Path.Combine(temp.Path, "items.json")), [source]);

        Assert.True(await catalog.RefreshAsync());
        var match = catalog.FindBest("PM");

        Assert.Null(match.Item);
        Assert.Equal(0, match.Score);
    }

    [Fact]
    public async Task UniqueFullNameStillMatchesWhenShortNamesCollide()
    {
        using var temp = new TemporaryDirectory();
        var source = new TestPriceSource(
            new ItemPrice("one", "Makarov PM pistol", "PM", 2, 1, 10_000, 5_000, "Prapor", false, false),
            new ItemPrice("two", "PM magazine", "PM", 1, 1, 8_000, 4_000, "Prapor", false, false));
        var catalog = new ItemCatalogService(new JsonItemCache(System.IO.Path.Combine(temp.Path, "items.json")), [source]);

        await catalog.RefreshAsync();
        var match = catalog.FindBest("Makarov PM pistol");

        Assert.Equal("one", match.Item?.Id);
        Assert.Equal(1, match.Score);
    }

    [Fact]
    public async Task ExactFullNameWinsWhenAnotherItemUsesItAsShortName()
    {
        using var temp = new TemporaryDirectory();
        var source = new TestPriceSource(
            new ItemPrice("one", "PM", "PISTOL", 2, 1, 10_000, 5_000, "Prapor", false, false),
            new ItemPrice("two", "PM magazine", "PM", 1, 1, 8_000, 4_000, "Prapor", false, false));
        var catalog = new ItemCatalogService(new JsonItemCache(System.IO.Path.Combine(temp.Path, "items.json")), [source]);

        await catalog.RefreshAsync();
        var match = catalog.FindBest("PM");

        Assert.Equal("one", match.Item?.Id);
        Assert.Equal(1, match.Score);
    }

    [Fact]
    public async Task CandidateSearchKeepsAmbiguousItemsForVisualDisambiguation()
    {
        using var temp = new TemporaryDirectory();
        var source = new TestPriceSource(
            new ItemPrice("one", "Makarov PM pistol", "PM", 2, 1, 10_000, 5_000, "Prapor", false, false)
            {
                IconUrl = "https://example.test/one.webp"
            },
            new ItemPrice("two", "PM magazine", "PM", 1, 1, 8_000, 4_000, "Prapor", false, false)
            {
                IconUrl = "https://example.test/two.webp"
            });
        var catalog = new ItemCatalogService(new JsonItemCache(System.IO.Path.Combine(temp.Path, "items.json")), [source]);

        await catalog.RefreshAsync();
        var candidates = catalog.FindCandidates("PM");

        Assert.Equal(2, candidates.Count);
        Assert.All(candidates, candidate => Assert.Equal(1, candidate.Score));
    }

    [Fact]
    public async Task EmptyRefreshKeepsLastValidCatalog()
    {
        using var temp = new TemporaryDirectory();
        var source = new TestPriceSource(new ItemPrice(
            "one", "Unique item", "UNQ", 1, 1, 10_000, 5_000, "Prapor", false, false));
        var catalog = new ItemCatalogService(new JsonItemCache(System.IO.Path.Combine(temp.Path, "items.json")), [source]);

        Assert.True(await catalog.RefreshAsync());
        source.Items = [];

        Assert.False(await catalog.RefreshAsync());
        Assert.Equal("one", catalog.FindBest("Unique item").Item?.Id);
    }

    [Fact]
    public async Task UndersizedRefreshKeepsLastValidCatalog()
    {
        using var temp = new TemporaryDirectory();
        var source = new TestPriceSource(
            new ItemPrice("one", "First item", "ONE", 1, 1, 10_000, 5_000, "Prapor", false, false),
            new ItemPrice("two", "Second item", "TWO", 1, 1, 9_000, 4_000, "Prapor", false, false))
        {
            MinimumExpectedItemCount = 2
        };
        var catalog = new ItemCatalogService(new JsonItemCache(System.IO.Path.Combine(temp.Path, "items.json")), [source]);

        Assert.True(await catalog.RefreshAsync());
        source.Items.RemoveAt(1);

        Assert.False(await catalog.RefreshAsync());
        Assert.Equal(2, catalog.Count);
        Assert.Equal("two", catalog.FindBest("Second item").Item?.Id);
    }

    [Fact]
    public async Task SecondarySourceEnrichesPrimaryUsageFlags()
    {
        using var temp = new TemporaryDirectory();
        var primary = new TestPriceSource(new ItemPrice(
            "one", "Quest item", "QUEST", 1, 1, 12_000, 5_000, "Prapor", false, false));
        var metadata = new TestPriceSource(new ItemPrice(
            "one", "Quest item", "QUEST", 1, 1, null, null, null, true, true));
        var catalog = new ItemCatalogService(
            new JsonItemCache(System.IO.Path.Combine(temp.Path, "items.json")), [primary, metadata]);

        Assert.True(await catalog.RefreshAsync());
        var item = catalog.FindBest("Quest item").Item;

        Assert.True(item?.UsedInTasks);
        Assert.True(item?.UsedInHideout);
        Assert.Equal(12_000, item?.FleaPrice);
    }

    [Fact]
    public async Task SecondarySourceEnrichesMissingIconUrl()
    {
        using var temp = new TemporaryDirectory();
        var primary = new TestPriceSource(new ItemPrice(
            "one", "Icon item", "ICON", 1, 1, 12_000, 5_000, "Prapor", false, false));
        var metadata = new TestPriceSource(new ItemPrice(
            "one", "Icon item", "ICON", 1, 1, null, null, null, false, false)
        {
            IconUrl = "https://example.test/icon.webp"
        });
        var catalog = new ItemCatalogService(
            new JsonItemCache(System.IO.Path.Combine(temp.Path, "items.json")), [primary, metadata]);

        Assert.True(await catalog.RefreshAsync());
        Assert.Equal("https://example.test/icon.webp", catalog.FindBest("Icon item").Item?.IconUrl);
    }

    [Fact]
    public async Task MetadataTimeoutDoesNotFailPrimaryRefresh()
    {
        using var temp = new TemporaryDirectory();
        var primary = new TestPriceSource(new ItemPrice(
            "one", "Current item", "CURRENT", 1, 1, 12_000, 5_000, "Prapor", false, false));
        var catalog = new ItemCatalogService(
            new JsonItemCache(System.IO.Path.Combine(temp.Path, "items.json")),
            [primary, new NeverCompletingPriceSource()], TimeSpan.FromMilliseconds(20));

        Assert.True(await catalog.RefreshAsync());
        Assert.Equal("one", catalog.FindBest("Current item").Item?.Id);
    }

    private sealed class TestPriceSource(params ItemPrice[] items) : IPriceDataSource
    {
        public List<ItemPrice> Items { get; set; } = [.. items];
        public string Name => "test";
        public int MinimumExpectedItemCount { get; set; } = 1;
        public Task<List<ItemPrice>> FetchItemsAsync(CancellationToken ct = default) =>
            Task.FromResult(Items.ToList());
    }

    private sealed class NeverCompletingPriceSource : IPriceDataSource
    {
        public string Name => "slow metadata";

        public async Task<List<ItemPrice>> FetchItemsAsync(CancellationToken ct = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return [];
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "LootPilotTests", Guid.NewGuid().ToString("N"));

        public TemporaryDirectory() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}
