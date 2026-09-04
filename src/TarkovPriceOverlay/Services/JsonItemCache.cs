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
        var paths = new[] { ResolvePath(), _fallbackPathProvider() }
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Resolve(path!))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            if (!File.Exists(path)) continue;
            try
            {
                await using var stream = File.OpenRead(path);
                var envelope = await JsonSerializer.DeserializeAsync<CacheEnvelope>(stream, JsonOptions, ct);
                if (envelope?.Items is not null) return envelope;
            }
            catch (JsonException) { }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return null;
    }

    public async Task SaveAsync(CacheEnvelope envelope, CancellationToken ct = default)
    {
        var path = ResolvePath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = File.Create(temp))
                await JsonSerializer.SerializeAsync(stream, envelope, JsonOptions, ct);
            File.Move(temp, path, true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    private string ResolvePath() => Resolve(_pathProvider());
    private static string Resolve(string path) => Environment.ExpandEnvironmentVariables(path)
        .Replace('/', Path.DirectorySeparatorChar);
}
