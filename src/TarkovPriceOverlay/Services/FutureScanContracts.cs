using System.Drawing;
using TarkovPriceOverlay.Models;

namespace TarkovPriceOverlay.Services;

// Extension point for V2: detect inventory/container cells, classify every visible
// item, then return one priced rectangle per item without changing the overlay UI.
public interface IInventoryScanner
{
    Task<IReadOnlyList<DetectedItem>> ScanAsync(Bitmap frame, CancellationToken ct = default);
}

public sealed record DetectedItem(Rectangle ScreenBounds, ItemPrice Item, double Confidence);

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

/// <summary>
/// V2 scan order: full-screen capture -> discover every visible container and
/// its dimensions -> detect occupied cells inside each grid -> classify icons.
/// </summary>
public sealed class InventoryScanPipeline
{
    private readonly ScreenCaptureService _capture;
    private readonly IInventoryGridDetector _grids;
    private readonly IInventoryOccupancyDetector _occupancy;

    public InventoryScanPipeline(
        ScreenCaptureService capture,
        IInventoryGridDetector grids,
        IInventoryOccupancyDetector occupancy)
        => (_capture, _grids, _occupancy) = (capture, grids, occupancy);

    public IReadOnlyList<OccupiedGridRegion> DetectLayout()
    {
        using var frame = _capture.CaptureCurrentScreen();
        return _grids.Detect(frame.Image)
            .SelectMany(grid => _occupancy.Detect(frame.Image, grid))
            .ToList();
    }
}
