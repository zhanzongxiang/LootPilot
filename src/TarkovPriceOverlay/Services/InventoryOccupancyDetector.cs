using System.Buffers;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace TarkovPriceOverlay.Services;

public sealed class InventoryOccupancyDetector : IInventoryOccupancyDetector
{
    private const double OccupiedThreshold = 6.0;

    public IReadOnlyList<OccupiedGridRegion> Detect(Bitmap frame, InventoryGridRegion grid)
    {
        using var pixels = new PixelBuffer(frame);
        return Detect(pixels, grid);
    }

    public IReadOnlyDictionary<InventoryGridRegion, IReadOnlyList<OccupiedGridRegion>> DetectMany(
        Bitmap frame, IReadOnlyList<InventoryGridRegion> grids)
    {
        using var pixels = new PixelBuffer(frame);
        return grids.ToDictionary(grid => grid,
            grid => (IReadOnlyList<OccupiedGridRegion>)Detect(pixels, grid));
    }

    private static IReadOnlyList<OccupiedGridRegion> Detect(
        PixelBuffer pixels, InventoryGridRegion grid)
    {
        var results = new List<OccupiedGridRegion>();
        for (var row = 0; row < grid.VisibleRows; row++)
        for (var column = 0; column < grid.VisibleColumns; column++)
        {
            var bounds = CellBounds(grid, column, row);
            var score = VisualComplexity(pixels, bounds);
            if (score < OccupiedThreshold) continue;
            results.Add(new(
                grid,
                new Rectangle(column, row, 1, 1),
                bounds,
                Math.Clamp((score - OccupiedThreshold) / 20d + 0.55, 0.55, 0.98)));
        }
        return results;
    }

    private static Rectangle CellBounds(InventoryGridRegion grid, int column, int row)
    {
        var left = (int)Math.Round(grid.Bounds.Left + column * grid.CellWidth);
        var top = (int)Math.Round(grid.Bounds.Top + row * grid.CellHeight);
        var right = (int)Math.Round(grid.Bounds.Left + (column + 1) * grid.CellWidth);
        var bottom = (int)Math.Round(grid.Bounds.Top + (row + 1) * grid.CellHeight);
        return Rectangle.FromLTRB(left, top, right, bottom);
    }

    private static double VisualComplexity(PixelBuffer frame, Rectangle bounds)
    {
        var left = Math.Clamp(bounds.Left + 5, 0, frame.Width - 2);
        var top = Math.Clamp(bounds.Top + 5, 0, frame.Height - 2);
        var right = Math.Clamp(bounds.Right - 5, left + 1, frame.Width - 1);
        var bottom = Math.Clamp(bounds.Bottom - 5, top + 1, frame.Height - 1);
        double luminanceTotal = 0;
        double luminanceSquaredTotal = 0;
        double saturationTotal = 0;
        double edgeTotal = 0;
        var count = 0;
        for (var y = top; y < bottom; y += 2)
        for (var x = left; x < right; x += 2)
        {
            var color = frame[x, y];
            var next = frame[Math.Min(x + 1, right), Math.Min(y + 1, bottom)];
            var luma = Luma(color.R, color.G, color.B);
            luminanceTotal += luma;
            luminanceSquaredTotal += luma * luma;
            saturationTotal += Math.Max(color.R, Math.Max(color.G, color.B)) -
                               Math.Min(color.R, Math.Min(color.G, color.B));
            edgeTotal += Math.Abs(luma - Luma(next.R, next.G, next.B));
            count++;
        }
        if (count == 0) return 0;
        var mean = luminanceTotal / count;
        var variance = Math.Max(0, luminanceSquaredTotal / count - mean * mean);
        var deviation = Math.Sqrt(variance);
        return deviation * 0.35 + saturationTotal / count * 0.20 + edgeTotal / count;
    }

    private static double Luma(byte red, byte green, byte blue) =>
        red * 0.299 + green * 0.587 + blue * 0.114;

    private sealed class PixelBuffer : IDisposable
    {
        private readonly Bitmap _bitmap;
        private readonly BitmapData _data;
        private readonly byte[] _bytes;
        private readonly bool _ownsBitmap;
        public int Width => _bitmap.Width;
        public int Height => _bitmap.Height;

        public PixelBuffer(Bitmap source)
        {
            _ownsBitmap = source.PixelFormat != PixelFormat.Format32bppArgb;
            _bitmap = _ownsBitmap
                ? source.Clone(new Rectangle(0, 0, source.Width, source.Height),
                    PixelFormat.Format32bppArgb)
                : source;
            _data = _bitmap.LockBits(new Rectangle(0, 0, Width, Height),
                ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var byteCount = Math.Abs(_data.Stride) * Height;
            _bytes = ArrayPool<byte>.Shared.Rent(byteCount);
            Marshal.Copy(_data.Scan0, _bytes, 0, byteCount);
        }

        public (byte R, byte G, byte B) this[int x, int y]
        {
            get
            {
                var offset = y * _data.Stride + x * 4;
                return (_bytes[offset + 2], _bytes[offset + 1], _bytes[offset]);
            }
        }

        public void Dispose()
        {
            _bitmap.UnlockBits(_data);
            ArrayPool<byte>.Shared.Return(_bytes);
            if (_ownsBitmap) _bitmap.Dispose();
        }
    }
}
