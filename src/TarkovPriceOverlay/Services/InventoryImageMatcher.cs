using System.Collections.Concurrent;
using System.Drawing;
using System.Net.Http;
using SkiaSharp;
using TarkovPriceOverlay.Models;

namespace TarkovPriceOverlay.Services;

/// <summary>
/// Resolves ambiguous OCR candidates by comparing a cropped inventory icon to
/// the matching API icon. Icons are fetched lazily and kept in a local cache.
/// </summary>
public sealed class InventoryImageMatcher
{
    private const int FeatureSize = 24;
    private const int MaximumCandidates = 8;
    private const double MinimumScore = 0.62;
    private const double MinimumMargin = 0.035;
    private readonly HttpClient _http;
    private readonly string _cacheDirectory;
    private readonly ConcurrentDictionary<string, Lazy<Task<IconFeature?>>> _features = new(
        StringComparer.OrdinalIgnoreCase);

    public InventoryImageMatcher(string? cacheDirectory = null, HttpClient? http = null)
    {
        _cacheDirectory = cacheDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LootPilot", "icons");
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(ProductInfo.UserAgent);
    }

    public async Task<(ItemPrice? Item, double Score)> MatchAsync(
        Bitmap screenshotCrop,
        IReadOnlyList<(ItemPrice Item, double Score)> candidates,
        CancellationToken ct = default)
        => await MatchAsync(_ => (Bitmap)screenshotCrop.Clone(), candidates, ct);

    public async Task<(ItemPrice? Item, double Score)> MatchAsync(
        Func<ItemPrice, Bitmap?> screenshotProvider,
        IReadOnlyList<(ItemPrice Item, double Score)> candidates,
        CancellationToken ct = default)
    {
        if (candidates.Count == 0) return (null, 0);
        var scored = new List<(ItemPrice Item, double Score)>();
        foreach (var candidate in candidates.Take(MaximumCandidates))
        {
            if (string.IsNullOrWhiteSpace(candidate.Item.IconUrl)) continue;
            using var screenshotCrop = screenshotProvider(candidate.Item);
            if (screenshotCrop is null) continue;
            var screenshot = IconFeature.FromBitmap(screenshotCrop);
            using var rotatedCrop = (Bitmap)screenshotCrop.Clone();
            rotatedCrop.RotateFlip(RotateFlipType.Rotate90FlipNone);
            var rotatedScreenshot = IconFeature.FromBitmap(rotatedCrop);
            var feature = await GetFeatureAsync(candidate.Item, ct);
            if (feature is null) continue;
            var imageScore = Math.Max(screenshot.Similarity(feature),
                rotatedScreenshot.Similarity(feature));
            // Text remains a useful tie breaker when two icons are visually close.
            var score = imageScore * 0.82 + candidate.Score * 0.18;
            scored.Add((candidate.Item, score));
        }
        var ranked = scored.OrderByDescending(x => x.Score).Take(2).ToList();
        if (ranked.Count == 0 || ranked[0].Score < MinimumScore ||
            ranked.Count > 1 && ranked[0].Score - ranked[1].Score < MinimumMargin)
            return (null, 0);
        return ranked[0];
    }

    internal static double Compare(Bitmap first, Bitmap second) =>
        IconFeature.FromBitmap(first).Similarity(IconFeature.FromBitmap(second));

    private Task<IconFeature?> GetFeatureAsync(ItemPrice item, CancellationToken ct)
    {
        var lazy = _features.GetOrAdd(item.Id,
            _ => new Lazy<Task<IconFeature?>>(
                () => LoadFeatureAsync(item), LazyThreadSafetyMode.ExecutionAndPublication));
        return lazy.Value.WaitAsync(ct);
    }

    private async Task<IconFeature?> LoadFeatureAsync(ItemPrice item)
    {
        try
        {
            Directory.CreateDirectory(_cacheDirectory);
            var path = Path.Combine(_cacheDirectory, SanitizeFileName(item.Id) + ".img");
            byte[] bytes;
            if (File.Exists(path))
                bytes = await File.ReadAllBytesAsync(path);
            else
            {
                bytes = await _http.GetByteArrayAsync(item.IconUrl!);
                var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    await File.WriteAllBytesAsync(temp, bytes);
                    File.Move(temp, path, true);
                }
                finally
                {
                    if (File.Exists(temp)) File.Delete(temp);
                }
            }
            using var bitmap = SKBitmap.Decode(bytes);
            return bitmap is null ? null : IconFeature.FromSkBitmap(bitmap);
        }
        catch
        {
            return null;
        }
    }

    private static string SanitizeFileName(string value) =>
        string.Concat(value.Select(character => char.IsLetterOrDigit(character) ? character : '_'));

    private sealed class IconFeature
    {
        private readonly float[] _gray;
        private readonly float[] _color;
        private readonly float[] _edge;

        private IconFeature(float[] gray, float[] color, float[] edge) =>
            (_gray, _color, _edge) = (gray, color, edge);

        public static IconFeature FromBitmap(Bitmap bitmap) => FromPixels(
            bitmap.Width, bitmap.Height,
            (x, y) =>
            {
                var color = bitmap.GetPixel(x, y);
                return (color.R, color.G, color.B, color.A);
            });

        public static IconFeature FromSkBitmap(SKBitmap bitmap) => FromPixels(
            bitmap.Width, bitmap.Height,
            (x, y) =>
            {
                var color = bitmap.GetPixel(x, y);
                return (color.Red, color.Green, color.Blue, color.Alpha);
            });

        private static IconFeature FromPixels(
            int width, int height,
            Func<int, int, (byte R, byte G, byte B, byte A)> pixel)
        {
            var gray = new float[FeatureSize * FeatureSize];
            var color = new float[FeatureSize * FeatureSize * 3];
            var edge = new float[FeatureSize * FeatureSize];
            var grayTotal = 0d;
            for (var y = 0; y < FeatureSize; y++)
            for (var x = 0; x < FeatureSize; x++)
            {
                var sourceX = Math.Clamp((int)((x + 0.5) * width / FeatureSize), 0, width - 1);
                var sourceY = Math.Clamp((int)((y + 0.5) * height / FeatureSize), 0, height - 1);
                var value = pixel(sourceX, sourceY);
                var alpha = value.A / 255f;
                var luminance = (value.R * 0.299f + value.G * 0.587f + value.B * 0.114f) * alpha;
                var index = y * FeatureSize + x;
                gray[index] = luminance / 255f;
                grayTotal += gray[index];
                var colorIndex = index * 3;
                color[colorIndex] = value.R * alpha / 255f;
                color[colorIndex + 1] = value.G * alpha / 255f;
                color[colorIndex + 2] = value.B * alpha / 255f;
                if (x > 0)
                    edge[index] = Math.Abs(gray[index] - gray[index - 1]);
            }
            var grayMean = (float)(grayTotal / gray.Length);
            for (var i = 0; i < gray.Length; i++) gray[i] -= grayMean;
            return new IconFeature(gray, color, edge);
        }

        public double Similarity(IconFeature other)
        {
            var gray = MeanAbsoluteDifference(_gray, other._gray);
            var edge = MeanAbsoluteDifference(_edge, other._edge);
            var color = MeanAbsoluteDifference(_color, other._color);
            return Math.Clamp(1d - gray * 0.50 - color * 0.35 - edge * 0.15, 0d, 1d);
        }

        private static double MeanAbsoluteDifference(float[] first, float[] second) =>
            first.Zip(second).Average(pair => Math.Abs(pair.First - pair.Second));
    }
}
