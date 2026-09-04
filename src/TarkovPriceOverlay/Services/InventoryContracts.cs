using System.Drawing;

namespace TarkovPriceOverlay.Services;

public interface IInventoryGridDetector
{
    IReadOnlyList<InventoryGridRegion> Detect(Bitmap frame);
}

public interface IInventoryOccupancyDetector
{
    IReadOnlyList<OccupiedGridRegion> Detect(Bitmap frame, InventoryGridRegion grid);

    IReadOnlyDictionary<InventoryGridRegion, IReadOnlyList<OccupiedGridRegion>> DetectMany(
        Bitmap frame, IReadOnlyList<InventoryGridRegion> grids)
        => grids.ToDictionary(grid => grid, grid => Detect(frame, grid));
}

public sealed record OccupiedGridRegion(
    InventoryGridRegion Grid,
    Rectangle CellBounds,
    Rectangle PixelBounds,
    double Confidence);
