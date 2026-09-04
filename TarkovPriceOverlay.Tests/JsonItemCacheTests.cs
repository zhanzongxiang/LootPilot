using TarkovPriceOverlay.Services;

namespace TarkovPriceOverlay.Tests;

public sealed class JsonItemCacheTests
{
    [Fact]
    public async Task CorruptPrimaryCacheFallsBackToValidSecondaryCache()
    {
        using var temp = new TemporaryDirectory();
        var primary = System.IO.Path.Combine(temp.Path, "primary.json");
        var fallback = System.IO.Path.Combine(temp.Path, "fallback.json");
        await File.WriteAllTextAsync(primary, "{ not valid json");
        await File.WriteAllTextAsync(fallback, "{\"UpdatedAt\":\"2026-01-01T00:00:00Z\",\"Items\":[],\"Source\":\"test\"}");

        var cache = new JsonItemCache(() => primary, () => fallback);
        var envelope = await cache.LoadAsync();

        Assert.NotNull(envelope);
        Assert.Equal("test", envelope.Source);
    }

    [Fact]
    public async Task PrimaryCacheWithoutItemsFallsBackToValidSecondaryCache()
    {
        using var temp = new TemporaryDirectory();
        var primary = System.IO.Path.Combine(temp.Path, "primary.json");
        var fallback = System.IO.Path.Combine(temp.Path, "fallback.json");
        await File.WriteAllTextAsync(primary, "{\"UpdatedAt\":\"2026-01-01T00:00:00Z\",\"Source\":\"broken\"}");
        await File.WriteAllTextAsync(fallback, "{\"UpdatedAt\":\"2026-01-01T00:00:00Z\",\"Items\":[],\"Source\":\"fallback\"}");

        var envelope = await new JsonItemCache(() => primary, () => fallback).LoadAsync();

        Assert.NotNull(envelope);
        Assert.Equal("fallback", envelope.Source);
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
