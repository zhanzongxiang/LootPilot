using System.Text.Json;
using TarkovPriceOverlay.Models;

namespace TarkovPriceOverlay.Services;

public sealed class JsonItemCache
{
    private readonly Func<string> _pathProvider;
    private readonly Func<string?> _fallbackPathProvider;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public JsonItemCache(string path) : this(() => path, () => null) { }

    public JsonItemCache(Func<string> pathProvider, Func<string?> fallbackPathProvider)
    {
        _pathProvider = pathProvider;
        _fallbackPathProvider = fallbackPathProvider;
    }

    public async Task<CacheEnvelope?> LoadAsync(CancellationToken ct = default)
    {
        var path = ResolvePath();
        var fallbackPath = _fallbackPathProvider();
        if (!File.Exists(path) && !string.IsNullOrWhiteSpace(fallbackPath))
            path = Resolve(fallbackPath);
        if (!File.Exists(path)) return null;
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<CacheEnvelope>(stream, JsonOptions, ct);
    }

    public async Task SaveAsync(CacheEnvelope envelope, CancellationToken ct = default)
    {
        var path = ResolvePath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        await using (var stream = File.Create(temp))
            await JsonSerializer.SerializeAsync(stream, envelope, JsonOptions, ct);
        File.Move(temp, path, true);
    }

    private string ResolvePath() => Resolve(_pathProvider());
    private static string Resolve(string path) => Environment.ExpandEnvironmentVariables(path)
        .Replace('/', Path.DirectorySeparatorChar);
}
