using TarkovPriceOverlay.Configuration;
using TarkovPriceOverlay.Models;

namespace TarkovPriceOverlay.Services;

public sealed class ScanCoordinator
{
    private const int TightCaptureWidth = 240;
    private const int TightCaptureHeight = 180;
    private const double MaximumCursorDistance = 105d;
    private readonly ScreenCaptureService _capture;
    private readonly IOcrService _ocr;
    private readonly ItemCatalogService _catalog;
    private readonly AppSettings _settings;

    public ScanCoordinator(
        ScreenCaptureService capture,
        IOcrService ocr,
        ItemCatalogService catalog,
        AppSettings settings)
        => (_capture, _ocr, _catalog, _settings) = (capture, ocr, catalog, settings);

    public async Task<RecognitionResult> ScanAsync(
        Action? onCaptured = null, CancellationToken ct = default)
    {
        var regions = _capture.CaptureCandidates();
        onCaptured?.Invoke();
        try
        {
            if (regions.Count == 0) return new RecognitionResult("", null, 0);
            var mode = _settings.RecognitionMode;
            if (mode.Equals("Fast", StringComparison.OrdinalIgnoreCase))
            {
                using var tight = CreateTightRegion(regions[0]);
                var fast = await ScanRegionAsync(tight, primaryOnly: true,
                    useAllLanguageOrders: false, scale: Math.Min(2, _settings.OcrImageScale),
                    parallelVariants: false, ct);
                return SelectBest(fast);
            }

            if (mode.Equals("Parallel", StringComparison.OrdinalIgnoreCase))
            {
                var regionTasks = regions.Select(region => ScanRegionAsync(region, primaryOnly: false,
                    useAllLanguageOrders: true, scale: _settings.OcrImageScale,
                    parallelVariants: true, ct));
                var parallelResults = (await Task.WhenAll(regionTasks)).SelectMany(x => x).ToList();
                return SelectBest(parallelResults);
            }

            using var balancedTight = CreateTightRegion(regions[0]);
            var firstPass = await ScanRegionAsync(balancedTight, primaryOnly: true,
                useAllLanguageOrders: false, scale: _settings.OcrImageScale,
                parallelVariants: false, ct);
            var firstResult = SelectBest(firstPass);
            if (firstResult.Item is not null && firstResult.Confidence >= 0.86)
                return firstResult;

            var fallback = new List<(RecognitionResult Result, double Rank)>(firstPass);
            foreach (var region in regions)
            {
                fallback.AddRange(await ScanRegionAsync(region, primaryOnly: false,
                    useAllLanguageOrders: true, scale: _settings.OcrImageScale,
                    parallelVariants: false, ct));
            }
            return SelectBest(fallback);
        }
        finally
        {
            foreach (var region in regions) region.Dispose();
        }
    }

    private static CapturedRegion CreateTightRegion(CapturedRegion source)
    {
        var width = Math.Min(TightCaptureWidth, source.Image.Width);
        var height = Math.Min(TightCaptureHeight, source.Image.Height);
        var targetX = source.TargetScreenPoint?.X - source.Origin.X ?? source.Image.Width / 2;
        var targetY = source.TargetScreenPoint?.Y - source.Origin.Y ?? source.Image.Height / 2;
        var left = Math.Clamp(targetX - width / 2, 0, source.Image.Width - width);
        var top = Math.Clamp(targetY - height / 2, 0, source.Image.Height - height);
        var image = source.Image.Clone(new Rectangle(left, top, width, height), source.Image.PixelFormat);
        return new CapturedRegion(source.Name, image,
            new Point(source.Origin.X + left, source.Origin.Y + top), source.TargetScreenPoint);
    }

    private async Task<IReadOnlyList<(RecognitionResult Result, double Rank)>> ScanRegionAsync(
        CapturedRegion region, bool primaryOnly, bool useAllLanguageOrders, int scale,
        bool parallelVariants, CancellationToken ct)
    {
        IReadOnlyList<Bitmap> variants = primaryOnly
            ? [OcrImagePreprocessor.CreatePrimaryVariant(region.Image, scale)]
            : OcrImagePreprocessor.CreateVariants(region.Image, scale);
        try
        {
            if (parallelVariants)
                return (await Task.WhenAll(variants.Select(variant =>
                    ScanVariantAsync(region, variant, useAllLanguageOrders, ct))))
                    .SelectMany(x => x).ToList();

            var results = new List<(RecognitionResult Result, double Rank)>();
            foreach (var variant in variants)
                results.AddRange(await ScanVariantAsync(region, variant, useAllLanguageOrders, ct));
            return results;
        }
        finally
        {
            foreach (var variant in variants) variant.Dispose();
        }
    }

    private async Task<IReadOnlyList<(RecognitionResult Result, double Rank)>> ScanVariantAsync(
        CapturedRegion region, Bitmap variant, bool useAllLanguageOrders, CancellationToken ct)
    {
        var results = new List<(RecognitionResult Result, double Rank)>();
        if (_ocr is IOcrLayoutService layout)
        {
            var blocks = layout is IConfigurableOcrLayoutService configurable
                ? await configurable.RecognizeBlocksAsync(variant, useAllLanguageOrders, ct)
                : await layout.RecognizeBlocksAsync(variant, ct);
            var scaleX = variant.Width / (double)region.Image.Width;
            var scaleY = variant.Height / (double)region.Image.Height;
            var target = region.TargetScreenPoint is Point targetScreenPoint
                ? new PointF(
                    (float)((targetScreenPoint.X - region.Origin.X) * scaleX),
                    (float)((targetScreenPoint.Y - region.Origin.Y) * scaleY))
                : new PointF(variant.Width / 2f, variant.Height / 2f);

            foreach (var candidate in BuildPositionedCandidates(blocks, scaleX))
            {
                var (item, score) = _catalog.FindBest(candidate.Text);
                if (item is null) continue;
                var distance = DistanceTo(candidate.Bounds, target);
                var distanceInSourcePixels = distance / Math.Max(scaleX, 0.01);
                if (region.TargetScreenPoint is not null &&
                    distanceInSourcePixels > MaximumCursorDistance)
                    continue;
                var proximity = 1d / (1d + distance / (140d * scaleX));
                var sourceBonus = region.TargetScreenPoint is not null ? 0.08 : 0;
                var rank = score * 0.58 + proximity * 0.34 + sourceBonus;
                results.Add((new RecognitionResult(
                    candidate.Text, item, score, region.Name), rank));
            }
        }
        else
        {
            var text = await _ocr.RecognizeAsync(variant, ct);
            var (item, score) = _catalog.FindBest(text);
            results.Add((new RecognitionResult(text, item, score, region.Name), score));
        }
        return results;
    }

    private static RecognitionResult SelectBest(
        IReadOnlyList<(RecognitionResult Result, double Rank)> results)
    {
        var best = results.OrderByDescending(x => x.Rank).Select(x => x.Result).FirstOrDefault()
            ?? new RecognitionResult("", null, 0);
        return best with { Item = best.Confidence >= 0.58 ? best.Item : null };
    }

    private static IReadOnlyList<(string Text, Rectangle Bounds)> BuildPositionedCandidates(
        IReadOnlyList<OcrTextBlock> blocks, double scaleX)
    {
        var candidates = new List<(string Text, Rectangle Bounds)>();
        foreach (var anchor in blocks.Where(x => x.Text.Any(char.IsLetterOrDigit)))
        {
            var sameLine = blocks.Where(other =>
            {
                var verticalDistance = Math.Abs(
                    (other.Bounds.Top + other.Bounds.Height / 2d) -
                    (anchor.Bounds.Top + anchor.Bounds.Height / 2d));
                var horizontalGap = other.Bounds.Left > anchor.Bounds.Right
                    ? other.Bounds.Left - anchor.Bounds.Right
                    : anchor.Bounds.Left > other.Bounds.Right
                        ? anchor.Bounds.Left - other.Bounds.Right : 0;
                return verticalDistance <= Math.Max(anchor.Bounds.Height, other.Bounds.Height) * 0.8 &&
                       horizontalGap <= 70 * scaleX;
            }).OrderBy(x => x.Bounds.Left).ToList();

            candidates.Add((anchor.Text, anchor.Bounds));
            if (sameLine.Count > 1)
            {
                var text = string.Join(" ", sameLine.Select(x => x.Text));
                var bounds = Rectangle.Union(sameLine[0].Bounds, sameLine[^1].Bounds);
                candidates.Add((text, bounds));
            }
        }
        return candidates
            .GroupBy(x => (Normalize(x.Text), x.Bounds.X / 12, x.Bounds.Y / 12))
            .Select(x => x.First()).ToList();
    }

    private static double DistanceTo(Rectangle bounds, PointF point)
    {
        var dx = Math.Max(bounds.Left - point.X, Math.Max(0, point.X - bounds.Right));
        var dy = Math.Max(bounds.Top - point.Y, Math.Max(0, point.Y - bounds.Bottom));
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static string Normalize(string value) =>
        new(value.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    public RecognitionResult MatchText(string text)
    {
        var (item, score) = _catalog.FindBest(text);
        return new(text, score >= 0.58 ? item : null, score);
    }
}
