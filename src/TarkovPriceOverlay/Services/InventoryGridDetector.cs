using System.Drawing;
using TarkovPriceOverlay.Configuration;

namespace TarkovPriceOverlay.Services;

public sealed record InventoryGridRegion(
    Rectangle Bounds,
    int Columns,
    int Rows,
    float CellWidth,
    float CellHeight,
    string Kind,
    double Confidence)
{
    public float CellSize => (CellWidth + CellHeight) / 2f;
    public int VisibleColumns => Math.Min(Columns, Math.Max(0, (int)Math.Round(Bounds.Width / CellWidth)));
    public int VisibleRows => Math.Min(Rows, Math.Max(0, (int)Math.Round(Bounds.Height / CellHeight)));
}

/// <summary>
/// Detects Tarkov's inventory lattice from visible pixels only. The first
/// implementation targets the 16:9, 1920x1080 UI scale represented by the
/// calibration screenshots, while all coordinates scale with the frame size.
/// </summary>
public sealed class InventoryGridDetector : IInventoryGridDetector
{
    private const float ReferenceWidth = 1920f;
    private const float ReferenceHeight = 1080f;
    private const float ReferenceCell = 63.5f;
    private readonly AppSettings _settings;
    private readonly DynamicInventoryGridDetector _dynamicGridDetector;

    public InventoryGridDetector(AppSettings? settings = null)
    {
        _settings = settings ?? new AppSettings();
        _dynamicGridDetector = new DynamicInventoryGridDetector(_settings);
    }

    public IReadOnlyList<InventoryGridRegion> Detect(Bitmap frame)
    {
        var results = new List<InventoryGridRegion>();
        var right = DetectRightPanel(frame);
        if (right is not null) results.Add(right);
        var transform = InventoryCoordinateTransform.Create(frame.Width, frame.Height, _settings);
        var sx = transform.ScaleX;
        var sy = transform.ScaleY;
        var dynamicRegions = _settings.EnableLocalGridCalibration
            ? _dynamicGridDetector.Detect(frame) : [];
        var offsetX = right?.Bounds.Left - (int)Math.Round(1263f * sx) ??
                      (int)Math.Round(transform.OffsetX);
        var offsetY = right?.Bounds.Top - (int)Math.Round(78f * sy) ??
                      (int)Math.Round(transform.OffsetY);
        results.Add(CreateFixedRegion(frame, 655, 165, 2, 2, 65f, 64f,
            "已装备胸挂", 0.84, offsetX, offsetY));
        var dynamicRig = dynamicRegions.FirstOrDefault(region => region.Kind == "弹挂");
        var dynamicPockets = dynamicRegions.FirstOrDefault(region => region.Kind == "口袋");
        // If pockets have scrolled into the upper half, the rig itself is
        // already above the viewport; do not revive its old fixed rectangle.
        var rig = dynamicRig ?? (dynamicPockets?.Bounds.Y < (int)Math.Round(350f * sy)
            ? null
            : DetectTacticalRig(frame, offsetX, offsetY));
        if (rig is not null) results.Add(rig);
        var pockets = dynamicPockets ?? DetectPockets(frame, offsetX, offsetY);
        if (pockets is not null) results.Add(pockets);
        results.Add(CreateFixedRegion(frame, 655, 582, 2, 2, 65f, 64f,
            "已装备背包", 0.80, offsetX, offsetY));
        // Prefer a lattice discovered from the visible backpack itself. This
        // follows scrolling and different bag dimensions while the remaining
        // equipment regions keep their proven fixed-layout fallback.
        var dynamicBackpack = dynamicRegions
            .Where(region => region.Kind == "背包" && region.Bounds.X < (int)Math.Round(1200f * sx) &&
                             region.Bounds.Y >= (int)Math.Round(350f * sy) &&
                             region.Rows >= 3 && region.Columns is >= 3 and <= 5)
            .OrderByDescending(region => region.Bounds.Width * region.Bounds.Height)
            .ThenBy(region => region.Bounds.Y)
            .FirstOrDefault();
        var backpack = dynamicBackpack is null
            ? DetectBackpack(frame, offsetX, offsetY)
            : dynamicBackpack with { Kind = "背包", Confidence = Math.Max(0.86, dynamicBackpack.Confidence) };
        if (backpack is not null) results.Add(backpack);
        var secure = dynamicRegions
                         .Where(region => region.Kind == "安全箱" &&
                                          region.Bounds.Y >= (backpack?.Bounds.Bottom ?? 0) -
                                          (int)Math.Round(10f * sy))
                         .OrderBy(region => region.Bounds.Y)
                         .FirstOrDefault() ??
                     DetectSecureContainer(frame, offsetX, offsetY);
        if (secure is not null && (backpack is null ||
            IntersectionRatio(secure.Bounds, backpack.Bounds) < 0.25))
            results.Add(secure);
        return results;
    }

    private static double IntersectionRatio(Rectangle target, Rectangle other)
    {
        var intersection = Rectangle.Intersect(target, other);
        if (intersection.IsEmpty || target.Width <= 0 || target.Height <= 0) return 0;
        return intersection.Width * intersection.Height /
               (double)(target.Width * target.Height);
    }

    private InventoryGridRegion CreateFixedRegion(
        Bitmap frame,
        float referenceX,
        float referenceY,
        int columns,
        int rows,
        float referenceCellWidth,
        float referenceCellHeight,
        string kind,
        double confidence,
        int offsetX = 0,
        int offsetY = 0)
    {
        var transform = InventoryCoordinateTransform.Create(frame.Width, frame.Height, _settings);
        var sx = transform.ScaleX;
        var sy = transform.ScaleY;
        var x = (int)Math.Round(referenceX * sx) + offsetX;
        var y = (int)Math.Round(referenceY * sy) + offsetY;
        var cellWidth = referenceCellWidth * sx;
        var cellHeight = referenceCellHeight * sy;
        var right = Math.Min(frame.Width, (int)Math.Round(x + columns * cellWidth));
        var bottom = Math.Min(frame.Height, (int)Math.Round(y + rows * cellHeight));
        return new(Rectangle.FromLTRB(x, y, right, bottom), columns, rows,
            cellWidth, cellHeight, kind, confidence);
    }

    /// <summary>
    /// One broad carried-items surface. Tarkov moves pocket/backpack/secure
    /// layouts between equipment states, so treating each as a fixed small
    /// rectangle caused edge columns to disappear. The broad surface starts at
    /// the left edge of pockets and ends at the right edge of the rig/backpack.
    /// </summary>
    private InventoryGridRegion? DetectCarriedArea(Bitmap frame, int verticalOffset)
    {
        var transform = InventoryCoordinateTransform.Create(frame.Width, frame.Height, _settings);
        var sx = transform.ScaleX;
        var sy = transform.ScaleY;
        var left = transform.X(648f);
        var top = transform.Y(145f) + verticalOffset;
        var right = Math.Min(frame.Width, transform.X(1158f));
        var bottom = Math.Min(frame.Height, transform.Y(952f) + verticalOffset);
        if (right - left < 300 || bottom - top < 400) return null;
        const int columns = 8;
        const int rows = 13;
        return new(
            Rectangle.FromLTRB(left, top, right, bottom),
            columns, rows,
            (right - left) / (float)columns,
            (bottom - top) / (float)rows,
            "随身区域", 0.86);
    }

    private InventoryGridRegion? DetectRightPanel(Bitmap frame)
    {
        var transform = InventoryCoordinateTransform.Create(frame.Width, frame.Height, _settings);
        var sx = transform.ScaleX;
        var sy = transform.ScaleY;
        var cell = ReferenceCell * sx;
        var expectedX = transform.OffsetX + 1263f * sx;
        var expectedY = transform.OffsetY + 78f * sy;
        var x = _settings.EnableLocalGridCalibration
            ? FindVerticalAnchor(frame, expectedX, expectedY, cell)
            : (int)Math.Round(expectedX);
        var y = _settings.EnableLocalGridCalibration
            ? FindHorizontalAnchor(frame, expectedX, expectedY, cell, searchRadius: 36)
            : (int)Math.Round(expectedY);
        if (x < 0 || y < 0) return null;

        var maxColumns = Math.Min(10, (int)((frame.Width - x) / cell));
        // Stash height varies with layout and can continue below the old
        // carried-inventory cutoff. Follow the visible screen to the bottom.
        var maxRows = Math.Min(16, Math.Max(3, (int)((frame.Height - y) / cell)));
        var columns = FindBoundaryExtent(frame, x, y, cell, maxColumns, horizontal: true, minimum: 3);
        var rows = FindBoundaryExtent(frame, x, y, cell, maxRows, horizontal: false, minimum: 3);
        if (columns < 3 || rows < 3) return null;

        var kind = columns >= 9 && rows >= 10 ? "仓库" : "容器";
        var bounds = Rectangle.FromLTRB(
            x, y,
            Math.Min(frame.Width, (int)Math.Round(x + columns * cell)),
            Math.Min(frame.Height, (int)Math.Round(y + rows * cell)));
        return new(bounds, columns, rows, cell, cell, kind, 0.92);
    }

    private InventoryGridRegion? DetectTacticalRig(Bitmap frame, int offsetX, int offsetY)
    {
        var transform = InventoryCoordinateTransform.Create(frame.Width, frame.Height, _settings);
        var sx = transform.ScaleX;
        var sy = transform.ScaleY;
        var cellWidth = 68f * sx;
        var cellHeight = 63.5f * sy;
        var x = (int)Math.Round(793f * sx) + offsetX;
        var y = (int)Math.Round(165f * sy) + offsetY;
        var right = (int)Math.Round(x + 5 * cellWidth);
        var bottom = (int)Math.Round(y + 4 * cellHeight);
        if (right >= frame.Width || bottom >= frame.Height) return null;

        var verticalEvidence = Enumerable.Range(0, 6)
            .Average(i => VerticalLineScore(frame, (int)Math.Round(x + i * cellWidth), y, bottom));
        var horizontalEvidence = Enumerable.Range(0, 5)
            .Average(i => HorizontalLineScore(frame, (int)Math.Round(y + i * cellHeight), x, right));
        if (verticalEvidence < 3.5 || horizontalEvidence < 3.5) return null;
        return new(
            Rectangle.FromLTRB(x, y, right, bottom),
            5, 4, cellWidth, cellHeight, "弹挂", 0.78);
    }

    private InventoryGridRegion? DetectPockets(Bitmap frame, int offsetX, int offsetY) => DetectFixedRegion(
        frame, 655, 470, 4, 1, 68.5f, 64f, "口袋", 0.72, offsetX, offsetY);

    private InventoryGridRegion? DetectSecureContainer(Bitmap frame, int offsetX, int offsetY) => DetectFixedRegion(
        frame, 655, 751, 7, 2, 65f, 64f, "安全箱", 0.70, offsetX, offsetY);

    private InventoryGridRegion? DetectFixedRegion(
        Bitmap frame,
        float referenceX,
        float referenceY,
        int columns,
        int rows,
        float referenceCellWidth,
        float referenceCellHeight,
        string kind,
        double confidence,
        int offsetX,
        int offsetY)
    {
        var transform = InventoryCoordinateTransform.Create(frame.Width, frame.Height, _settings);
        var sx = transform.ScaleX;
        var sy = transform.ScaleY;
        var x = (int)Math.Round(referenceX * sx) + offsetX;
        var y = (int)Math.Round(referenceY * sy) + offsetY;
        var cellWidth = referenceCellWidth * sx;
        var cellHeight = referenceCellHeight * sy;
        var right = (int)Math.Round(x + columns * cellWidth);
        var bottom = (int)Math.Round(y + rows * cellHeight);
        if (right >= frame.Width || bottom >= frame.Height) return null;

        var verticalEvidence = Enumerable.Range(0, columns + 1)
            .Average(i => VerticalLineScore(frame, (int)Math.Round(x + i * cellWidth), y, bottom));
        var horizontalEvidence = Enumerable.Range(0, rows + 1)
            .Average(i => HorizontalLineScore(frame, (int)Math.Round(y + i * cellHeight), x, right));
        if (verticalEvidence < 2.5 || horizontalEvidence < 2.5) return null;
        return new(
            Rectangle.FromLTRB(x, y, right, bottom),
            columns, rows, cellWidth, cellHeight, kind, confidence);
    }

    private InventoryGridRegion? DetectBackpack(Bitmap frame, int offsetX, int offsetY)
    {
        var transform = InventoryCoordinateTransform.Create(frame.Width, frame.Height, _settings);
        var sx = transform.ScaleX;
        var sy = transform.ScaleY;
        var cell = ReferenceCell * sx;
        var expectedX = 787f * sx;
        var expectedY = 582f * sy;
        var x = (int)Math.Round(expectedX) + offsetX;
        var y = (int)Math.Round(expectedY) + offsetY;
        var topBoundary = BoundaryBandScore(frame, x, y, cell, 0, vertical: false);
        var middleBoundary = BoundaryBandScore(frame, x, y, cell, 2, vertical: false);
        if (topBoundary < 8 || middleBoundary < 8) return null;

        // Backpack contents begin to the right of the equipped backpack icon.
        // Width varies by model but cannot extend beyond the tactical-rig edge.
        var maxColumns = Math.Max(3, (int)Math.Floor(((1133f * sx + offsetX) - x) / cell));
        var columns = FindBoundaryExtent(frame, x, y, cell, maxColumns, horizontal: true, minimum: 3);
        if (columns is < 3 or > 5) return null;

        // The supplied Takedown is 3x8. Only six rows are unobscured in this
        // screenshot, so Bounds describes visible pixels while Rows preserves
        // the logical container size.
        var logicalRows = columns == 3
            ? 8
            : FindBoundaryExtent(frame, x, y, cell, 6, horizontal: false, minimum: 3);
        var visibleBottom = Math.Min((int)Math.Round(950f * sy) + offsetY,
            (int)Math.Round(y + logicalRows * cell));
        var bounds = Rectangle.FromLTRB(
            x, y,
            Math.Min(frame.Width, (int)Math.Round(x + columns * cell)),
            Math.Min(frame.Height, visibleBottom));
        return new(bounds, columns, logicalRows, cell, cell, "背包", 0.82);
    }

    private static int FindVerticalAnchor(
        Bitmap image, float expectedX, float expectedY, float cell, int searchRadius = 22)
    {
        var top = Math.Max(0, (int)expectedY);
        var bottom = Math.Min(image.Height - 1, (int)(expectedY + cell * 8));
        var bestX = -1;
        var bestScore = 0d;
        for (var x = Math.Max(2, (int)expectedX - searchRadius);
             x <= Math.Min(image.Width - 3, (int)expectedX + searchRadius); x++)
        {
            double score = 0;
            for (var i = 0; i <= 8; i++)
                score += VerticalLineScore(image, (int)Math.Round(x + i * cell), top, bottom);
            if (score > bestScore) (bestScore, bestX) = (score, x);
        }
        return bestScore > 35 ? bestX : -1;
    }

    private static int FindHorizontalAnchor(
        Bitmap image, float expectedX, float expectedY, float cell, int searchRadius = 16)
    {
        var left = Math.Max(0, (int)expectedX);
        var right = Math.Min(image.Width - 1, (int)(expectedX + cell * 8));
        var bestY = -1;
        var bestScore = 0d;
        for (var y = Math.Max(2, (int)expectedY - searchRadius);
             y <= Math.Min(image.Height - 3, (int)expectedY + searchRadius); y++)
        {
            double score = 0;
            for (var i = 0; i <= 8; i++)
                score += HorizontalLineScore(image, (int)Math.Round(y + i * cell), left, right);
            if (score > bestScore) (bestScore, bestY) = (score, y);
        }
        return bestScore > 35 ? bestY : -1;
    }

    private static int FindBoundaryExtent(
        Bitmap image, int x, int y, float cell, int maximum, bool horizontal, int minimum)
    {
        for (var index = minimum; index < maximum; index++)
        {
            var current = BoundaryBandScore(image, x, y, cell, index, horizontal);
            var next = BoundaryBandScore(image, x, y, cell, index + 1, horizontal);
            if (current >= 8 && next < 2.5 && next < current * 0.25)
                return index;
        }
        return maximum;
    }

    private static double BoundaryBandScore(
        Bitmap image, int x, int y, float cell, int index, bool vertical)
    {
        var boundary = (int)Math.Round((vertical ? x : y) + index * cell);
        double total = 0;
        var count = 0;
        if (vertical)
        {
            var top = Math.Max(0, y);
            var bottom = Math.Min(image.Height - 1, (int)Math.Round(y + 3 * cell));
            for (var py = top; py <= bottom; py += 3)
            for (var offset = -2; offset < 2; offset++)
            {
                var px = Math.Clamp(boundary + offset, 0, image.Width - 2);
                total += Math.Abs(Luma(image.GetPixel(px, py)) - Luma(image.GetPixel(px + 1, py)));
                count++;
            }
        }
        else
        {
            var left = Math.Max(0, x);
            var right = Math.Min(image.Width - 1, (int)Math.Round(x + 3 * cell));
            for (var px = left; px <= right; px += 3)
            for (var offset = -2; offset < 2; offset++)
            {
                var py = Math.Clamp(boundary + offset, 0, image.Height - 2);
                total += Math.Abs(Luma(image.GetPixel(px, py)) - Luma(image.GetPixel(px, py + 1)));
                count++;
            }
        }
        return count == 0 ? 0 : total / count;
    }

    private static double VerticalLineScore(Bitmap image, int x, int top, int bottom)
    {
        if (x < 1 || x >= image.Width - 1) return 0;
        double total = 0;
        var count = 0;
        for (var y = top; y <= bottom; y += 5)
        {
            total += Math.Abs(Luma(image.GetPixel(x - 1, y)) - Luma(image.GetPixel(x + 1, y)));
            count++;
        }
        return count == 0 ? 0 : total / count;
    }

    private static double HorizontalLineScore(Bitmap image, int y, int left, int right)
    {
        if (y < 1 || y >= image.Height - 1) return 0;
        double total = 0;
        var count = 0;
        for (var x = left; x <= right; x += 5)
        {
            total += Math.Abs(Luma(image.GetPixel(x, y - 1)) - Luma(image.GetPixel(x, y + 1)));
            count++;
        }
        return count == 0 ? 0 : total / count;
    }

    private static double Luma(Color color) =>
        color.R * 0.299 + color.G * 0.587 + color.B * 0.114;
}
