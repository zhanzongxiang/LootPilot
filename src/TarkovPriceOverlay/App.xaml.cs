using System.Windows;
using TarkovPriceOverlay.Configuration;
using TarkovPriceOverlay.Services;

namespace TarkovPriceOverlay;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var settings = AppSettings.Load();
        AppThemeManager.Apply(settings.Theme);
        var cache = new JsonItemCache(
            () => settings.ExpandedModeCachePath,
            () => settings.GameMode.Equals("PvP", StringComparison.OrdinalIgnoreCase)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "TarkovPriceOverlay", "items.json")
                : null);
        IPriceDataSource[] priceSources =
        [
            new EftarkovClient(settings),
            new TarkovDevClient(settings)
        ];
        var catalog = new ItemCatalogService(cache, priceSources);
        var capture = new ScreenCaptureService(settings);
        var tesseract = new TesseractOcrService(settings);
        IOcrService ocr = settings.UseNeuralChineseOcr
            ? new HybridOcrService(new PaddleOcrService(), tesseract)
            : tesseract;
        var coordinator = new ScanCoordinator(capture, ocr, catalog, settings);
        var inventoryCoordinator = new InventoryScanCoordinator(
            capture,
            new InventoryGridDetector(settings),
            new InventoryOccupancyDetector(),
            (IOcrLayoutService)ocr,
            catalog,
            settings);
        new MainWindow(settings, catalog, coordinator, inventoryCoordinator,
            new ScanHistoryService()).Show();
    }
}
