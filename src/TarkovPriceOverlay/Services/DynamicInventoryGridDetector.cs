using System.Buffers;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using TarkovPriceOverlay.Configuration;

namespace TarkovPriceOverlay.Services;

/// <summary>Offline prototype for discovering visible carried-container grids.</summary>
public sealed class DynamicInventoryGridDetector
{
    private readonly AppSettings _settings;

    public DynamicInventoryGridDetector(AppSettings? settings = null) =>
        _settings = settings ?? new AppSettings();

    public IReadOnlyList<InventoryGridRegion> Detect(Bitmap frame)
    {
        using var pixels = new LumaPixels(frame);
        var transform = InventoryCoordinateTransform.Create(frame.Width, frame.Height, _settings);
        var sx = transform.ScaleX;
        var sy = transform.ScaleY;
        var ox = transform.OffsetX;
        var oy = transform.OffsetY;
        var cellWidth = 63.5f * sx;
        var cellHeight = 63.5f * sy;
        var leftMin = (int)Math.Round(ox + 780 * sx);
        var leftMax = (int)Math.Round(ox + 800 * sx);
        var topMin = (int)Math.Round(oy + 105 * sy);
        var topMax = Math.Min(frame.Height - 3, (int)Math.Round(oy + 950 * sy));
        var seedLeft = (int)Math.Round(ox + 786 * sx);
        var iconLeft = (int)Math.Round(ox + 654 * sx);
        var iconRight = (int)Math.Round(ox + 785 * sx);
        var iconHeight = (int)Math.Round(127 * sy);

        // Backpack and secure-container content starts beside a 2x2 equipped
        // item slot. Detect that real slot instead of assuming its Y position.
        var yPeaks = LocalPeaks(Math.Max(topMin, (int)(oy + 300 * sy)), topMax,
                y => HorizontalScore(pixels, y, iconLeft, iconRight), 8.0, 3)
            .Where(y => y + iconHeight < pixels.Height &&
                        BestAround(y - (int)Math.Round(18 * sy), 3,
                            header => HorizontalScore(pixels, header, iconLeft, iconRight)) >= 5.0 &&
                        BestAround(y + iconHeight, 4,
                            bottom => HorizontalScore(pixels, bottom, iconLeft, iconRight)) >= 5.0 &&
                        BestAround(iconLeft, 3,
                            left => VerticalScore(pixels, left, y, y + iconHeight)) >= 4.0 &&
                        BestAround(iconRight, 3,
                            right => VerticalScore(pixels, right, y, y + iconHeight)) >= 4.0)
            .ToList();
        var results = new List<InventoryGridRegion>();
        var rig = DetectTacticalRig(pixels, sx, sy, ox, oy);
        if (rig is not null)
            results.Add(rig);
        var pockets = DetectPockets(pixels, rig?.Bounds.Y, sx, sy, ox, oy);
        if (pockets is not null) results.Add(pockets);

        var candidates = new List<InventoryGridRegion>();
        for (var seedIndex = 0; seedIndex < yPeaks.Count; seedIndex++)
        {
            var y = yPeaks[seedIndex];
            var x = Enumerable.Range(leftMin, Math.Max(1, leftMax - leftMin + 1))
                .OrderByDescending(candidate => VerticalScore(pixels, candidate, y,
                    Math.Min(frame.Height - 1, (int)(y + cellHeight * 4))))
                .First();
            var viewportBottom = Math.Min(frame.Height, (int)Math.Round(oy + 951 * sy));
            var maxVisibleRows = Math.Max(2,
                (int)Math.Ceiling((viewportBottom - y) / cellHeight));
            var nextEquippedSlot = yPeaks.Skip(seedIndex + 1)
                .FirstOrDefault(next => next - y > iconHeight + (int)Math.Round(20 * sy));
            if (nextEquippedSlot > 0)
                maxVisibleRows = Math.Min(maxVisibleRows, Math.Max(2,
                    (int)Math.Floor((nextEquippedSlot - y - 8 * sy) / cellHeight)));
            var candidate = BuildLattice(pixels, x, y, cellWidth, cellHeight,
                Math.Min(8, maxVisibleRows));
            if (candidate is not null) candidates.Add(candidate);
        }

        // Prefer the first boundary in a periodic run. A later internal row can
        // otherwise look like a stronger top edge when it contains bright text.
        var accepted = new List<InventoryGridRegion>();
        foreach (var candidate in candidates.OrderBy(region => region.Bounds.Y)
                     .ThenByDescending(region => region.Confidence))
        {
            if (accepted.Any(existing => Overlap(candidate.Bounds, existing.Bounds) > 0.42)) continue;
            accepted.Add(candidate);
        }
        var backpack = accepted
            .Where(region => region.Rows >= 3)
            .OrderByDescending(region => region.Bounds.Width * region.Bounds.Height)
            .ThenBy(region => region.Bounds.Y)
            .FirstOrDefault();
        foreach (var region in accepted)
        {
            var kind = ReferenceEquals(region, backpack) ? "背包" : "安全箱";
            results.Add(region with { Kind = kind });
        }
        return results;
    }

    private static InventoryGridRegion? DetectTacticalRig(
        LumaPixels pixels, float sx, float sy, float ox, float oy)
    {
        var iconLeft = (int)Math.Round(ox + 654 * sx);
        var iconRight = (int)Math.Round(ox + 785 * sx);
        var iconHeight = (int)Math.Round(127 * sy);
        var yCandidates = LocalPeaks((int)Math.Round(oy + 110 * sy), (int)Math.Round(oy + 210 * sy),
                y => HorizontalScore(pixels, y, iconLeft, iconRight), 8.0, 3)
            .Where(y => y + iconHeight < pixels.Height &&
                        BestAround(y - (int)Math.Round(18 * sy), 3,
                            header => HorizontalScore(pixels, header, iconLeft, iconRight)) >= 5.0 &&
                        BestAround(y + iconHeight, 4,
                            bottom => HorizontalScore(pixels, bottom, iconLeft, iconRight)) >= 5.0)
            .ToList();
        foreach (var y in yCandidates)
        {
            // Tactical rigs are intentionally irregular and may have missing
            // cells. Anchor the complete 5x4 work surface to the detected
            // equipped-rig card instead of requiring a continuous rectangle.
            var x = (int)Math.Round(ox + 793 * sx);
            var cellWidth = 68f * sx;
            var cellHeight = 63.5f * sy;
            return new InventoryGridRegion(
                Rectangle.FromLTRB(x, y,
                    (int)Math.Round(x + 5 * cellWidth),
                    (int)Math.Round(y + 4 * cellHeight)),
                5, 4, cellWidth, cellHeight, "弹挂", 0.88);
        }
        return null;
    }

    private static InventoryGridRegion? DetectPockets(
        LumaPixels pixels, int? rigTop, float sx, float sy, float ox, float oy)
    {
        var cellWidth = 68.5f * sx;
        var cellHeight = 64f * sy;
        var expectedX = (int)Math.Round(ox + 655 * sx);
        var anchoredPocketTop = rigTop.GetValueOrDefault() + (int)Math.Round(306 * sy);
        var start = rigTop is null ? (int)Math.Round(oy + 120 * sy) : anchoredPocketTop;
        var end = rigTop is null ? (int)Math.Round(oy + 700 * sy) : anchoredPocketTop;
        var best = (Score: 0d, X: expectedX, Y: 0);
        for (var y = start; y <= end; y++)
        for (var x = Math.Max(2, expectedX - 4); x <= Math.Min(pixels.Width - 3, expectedX + 4); x++)
        {
            var right = (int)Math.Round(x + 4 * cellWidth);
            var bottom = (int)Math.Round(y + cellHeight);
            if (right >= pixels.Width || bottom >= pixels.Height) continue;
            var header = BestAround(y - (int)Math.Round(18 * sy), 3,
                position => HorizontalScore(pixels, position, x, (int)Math.Round(x + 2 * cellWidth)));
            if (header < 4) continue;
            var horizontal = RobustHorizontalScore(pixels, y, x, cellWidth, 4) +
                             RobustHorizontalScore(pixels, bottom, x, cellWidth, 4);
            var vertical = Enumerable.Range(0, 5)
                .Average(index => BestAround((int)Math.Round(x + index * cellWidth), 2,
                    position => VerticalCoverage(pixels, position, y, bottom)));
            var score = horizontal + vertical * 20;
            if (score > best.Score) best = (score, x, y);
        }
        if (best.Score < 7 && rigTop is null) return null;
        var detectedX = rigTop is not null ? expectedX : best.X;
        var detectedY = rigTop is not null ? anchoredPocketTop : best.Y;
        var detectedRight = (int)Math.Round(detectedX + 4 * cellWidth);
        var detectedBottom = (int)Math.Round(detectedY + cellHeight);
        return new InventoryGridRegion(
            Rectangle.FromLTRB(detectedX, detectedY, detectedRight, detectedBottom),
            4, 1, cellWidth, cellHeight, "口袋", 0.86);
    }

    private static InventoryGridRegion? BuildLattice(
        LumaPixels pixels, int x, int y, float cellWidth, float cellHeight, int maxRows)
    {
        var vertical = Enumerable.Range(0, 6)
            .Select(index => BestAround((int)Math.Round(x + index * cellWidth), 3,
                position => VerticalScore(pixels, position, y,
                    Math.Min(pixels.Height - 1, (int)(y + cellHeight * 4)))))
            .ToArray();
        var columns = ContinuousExtent(vertical, 4.0, minimum: 3, maximum: 5, allowedWeak: 0);
        var verticalCoverage = Enumerable.Range(0, 6)
            .Select(index => BestAround((int)Math.Round(x + index * cellWidth), 3,
                position => VerticalCoverage(pixels, position, y,
                    Math.Min(pixels.Height - 1, (int)Math.Round(y + cellHeight * 2)))))
            .ToArray();
        var coverageColumns = ContinuousExtent(verticalCoverage, 0.24,
            minimum: 3, maximum: 5, allowedWeak: 0);
        if (coverageColumns >= 3) columns = Math.Min(columns, coverageColumns);
        if (columns < 3 || vertical[0] < 3.2) return null;
        var horizontal = Enumerable.Range(0, 9)
            .Select(index => BestAround((int)Math.Round(y + index * cellHeight), 3,
                position => RobustHorizontalScore(pixels, position, x, cellWidth, columns)))
            .ToArray();
        var rows = ContinuousExtent(horizontal, 2.8, minimum: 2, maximum: maxRows, allowedWeak: 0);
        if (rows < 2 || horizontal[0] < 3.8) return null;

        // A horizontal line elsewhere in the equipment page can share the
        // same 64 px rhythm. Require vertical dividers to continue through
        // every accepted row so adjacent containers cannot be joined.
        var structuralRows = 0;
        for (var row = 0; row < rows; row++)
        {
            var segmentTop = (int)Math.Round(y + row * cellHeight);
            var segmentBottom = (int)Math.Round(y + (row + 1) * cellHeight);
            var dividerScores = Enumerable.Range(0, columns + 1)
                .Select(index => BestAround((int)Math.Round(x + index * cellWidth), 3,
                    position => VerticalCoverage(pixels, position, segmentTop, segmentBottom)))
                .ToArray();
            if (dividerScores.Average() < 0.30) break;
            structuralRows++;
        }
        rows = structuralRows;
        if (rows < 2) return null;

        var right = Math.Min(pixels.Width, (int)Math.Round(x + columns * cellWidth));
        var bottom = Math.Min(pixels.Height, (int)Math.Round(y + rows * cellHeight));
        var vAverage = vertical.Take(columns + 1).Average();
        var hAverage = horizontal.Take(rows + 1).Average();
        var confidence = Math.Clamp(0.48 + Math.Min(vAverage, hAverage) / 45d, 0d, 0.92);
        return new InventoryGridRegion(Rectangle.FromLTRB(x, y, right, bottom),
            columns, rows, cellWidth, cellHeight, "动态容器网格", confidence);
    }

    private static int ContinuousExtent(
        double[] scores, double threshold, int minimum, int maximum, int allowedWeak)
    {
        var lastStrongBoundary = 0;
        var weakRun = 0;
        for (var boundary = 1; boundary <= maximum; boundary++)
        {
            if (scores[boundary] >= threshold)
            {
                lastStrongBoundary = boundary;
                weakRun = 0;
            }
            else if (++weakRun > allowedWeak)
            {
                break;
            }
        }
        return lastStrongBoundary >= minimum ? lastStrongBoundary : 0;
    }

    private static List<int> LocalPeaks(
        int start, int end, Func<int, double> score, double threshold, int radius)
    {
        var values = new double[end - start + 1];
        for (var position = start; position <= end; position++) values[position - start] = score(position);
        var peaks = new List<int>();
        for (var position = start + radius; position <= end - radius; position++)
        {
            var value = values[position - start];
            if (value < threshold) continue;
            var isPeak = true;
            for (var offset = -radius; offset <= radius; offset++)
                if (values[position - start + offset] > value) { isPeak = false; break; }
            if (isPeak && (peaks.Count == 0 || position - peaks[^1] > radius)) peaks.Add(position);
        }
        return peaks;
    }

    private static double BestAround(int center, int radius, Func<int, double> score)
    {
        var best = 0d;
        for (var position = center - radius; position <= center + radius; position++)
            best = Math.Max(best, score(position));
        return best;
    }

    private static double VerticalScore(LumaPixels image, int x, int top, int bottom)
    {
        if (x < 2 || x >= image.Width - 2) return 0;
        double total = 0;
        var count = 0;
        for (var y = Math.Max(0, top); y <= Math.Min(image.Height - 1, bottom); y += 4)
        {
            total += Math.Abs(image[x - 2, y] - image[x + 2, y]);
            count++;
        }
        return count == 0 ? 0 : total / count;
    }

    private static double VerticalCoverage(LumaPixels image, int x, int top, int bottom)
    {
        if (x < 2 || x >= image.Width - 2) return 0;
        var hits = 0;
        var count = 0;
        for (var y = Math.Max(0, top); y <= Math.Min(image.Height - 1, bottom); y += 3)
        {
            if (Math.Abs(image[x - 2, y] - image[x + 2, y]) >= 8) hits++;
            count++;
        }
        return count == 0 ? 0 : hits / (double)count;
    }

    private static double HorizontalScore(LumaPixels image, int y, int left, int right)
    {
        if (y < 2 || y >= image.Height - 2) return 0;
        double total = 0;
        var count = 0;
        for (var x = Math.Max(0, left); x <= Math.Min(image.Width - 1, right); x += 4)
        {
            total += Math.Abs(image[x, y - 2] - image[x, y + 2]);
            count++;
        }
        return count == 0 ? 0 : total / count;
    }

    private static double RobustHorizontalScore(
        LumaPixels image, int y, int left, float cellWidth, int columns)
    {
        var scores = Enumerable.Range(0, columns)
            .Select(index => HorizontalScore(image, y,
                (int)Math.Round(left + index * cellWidth) + 3,
                Math.Min(image.Width - 1, (int)Math.Round(left + (index + 1) * cellWidth) - 3)))
            .OrderBy(score => score)
            .ToArray();
        return scores.Length == 0 ? 0 : scores[scores.Length / 2];
    }

    private static double Overlap(Rectangle first, Rectangle second)
    {
        var intersection = Rectangle.Intersect(first, second);
        if (intersection.IsEmpty) return 0;
        return intersection.Width * intersection.Height /
               (double)Math.Min(first.Width * first.Height, second.Width * second.Height);
    }

    private sealed class LumaPixels : IDisposable
    {
        private readonly Bitmap _bitmap;
        private readonly BitmapData _data;
        private readonly byte[] _bytes;
        private readonly bool _ownsBitmap;
        public int Width => _bitmap.Width;
        public int Height => _bitmap.Height;

        public LumaPixels(Bitmap source)
        {
            _ownsBitmap = source.PixelFormat != PixelFormat.Format32bppArgb;
            _bitmap = _ownsBitmap ? source.Clone(new Rectangle(0, 0, source.Width, source.Height),
                PixelFormat.Format32bppArgb) : source;
            _data = _bitmap.LockBits(new Rectangle(0, 0, Width, Height),
                ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var byteCount = Math.Abs(_data.Stride) * Height;
            _bytes = ArrayPool<byte>.Shared.Rent(byteCount);
            Marshal.Copy(_data.Scan0, _bytes, 0, byteCount);
        }

        public double this[int x, int y]
        {
            get
            {
                var offset = y * _data.Stride + x * 4;
                return _bytes[offset + 2] * 0.299 + _bytes[offset + 1] * 0.587 + _bytes[offset] * 0.114;
            }
        }

        public void Dispose()
        {
            _bitmap.UnlockBits(_data);
            ArrayPool<byte>.Shared.Return(_bytes);
            // Do not dispose the caller-owned bitmap when it already had the desired format.
            if (_ownsBitmap) _bitmap.Dispose();
        }
    }
}
