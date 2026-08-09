using System.Drawing;

namespace TarkovPriceOverlay.Services;

public interface IOcrService
{
    Task<string> RecognizeAsync(Bitmap image, CancellationToken ct = default);
}

public interface IOcrLayoutService
{
    Task<IReadOnlyList<TarkovPriceOverlay.Models.OcrTextBlock>> RecognizeBlocksAsync(
        Bitmap image, CancellationToken ct = default);
}

public interface IConfigurableOcrLayoutService : IOcrLayoutService
{
    Task<IReadOnlyList<TarkovPriceOverlay.Models.OcrTextBlock>> RecognizeBlocksAsync(
        Bitmap image, bool useAllLanguageOrders, CancellationToken ct = default);
}
