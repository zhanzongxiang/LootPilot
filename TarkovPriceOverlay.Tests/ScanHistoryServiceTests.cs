using TarkovPriceOverlay.Models;
using TarkovPriceOverlay.Services;

namespace TarkovPriceOverlay.Tests;

public sealed class ScanHistoryServiceTests
{
    [Fact]
    public async Task CorruptHistoryLoadsEmptyAndCanBeReplaced()
    {
        using var temp = new TemporaryDirectory();
        var path = System.IO.Path.Combine(temp.Path, "history.json");
        await File.WriteAllTextAsync(path, "{ invalid json");
        var history = new ScanHistoryService(path);

        Assert.Empty(await history.LoadAsync());

        var entry = new ScanHistoryEntry(DateTimeOffset.UtcNow, "PvP", 2, 20_000, 10_000);
        await history.AddAsync(entry);
        var loaded = await history.LoadAsync();

        Assert.Single(loaded);
        Assert.Equal(2, loaded[0].ItemCount);
    }

    [Fact]
    public async Task NullHistoryEntriesAreIgnored()
    {
        using var temp = new TemporaryDirectory();
        var path = System.IO.Path.Combine(temp.Path, "history.json");
        await File.WriteAllTextAsync(path,
            "[null,{\"ScannedAt\":\"2026-01-01T00:00:00Z\",\"GameMode\":\"PvP\",\"ItemCount\":1,\"FleaTotal\":2,\"TraderTotal\":3}]");

        var loaded = await new ScanHistoryService(path).LoadAsync();

        Assert.Single(loaded);
        Assert.Equal(1, loaded[0].ItemCount);
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
