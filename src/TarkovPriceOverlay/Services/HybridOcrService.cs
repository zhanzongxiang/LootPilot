using System.Drawing;
using TarkovPriceOverlay.Models;

namespace TarkovPriceOverlay.Services;

/// <summary>
/// Combines PP-OCRv6's small-Chinese recognition with the established
/// Tesseract channel. If the neural model cannot initialize, Tesseract still
/// provides the complete legacy behavior.
/// </summary>
public sealed class HybridOcrService : IOcrService, IConfigurableOcrLayoutService
{
    private readonly PaddleOcrService _paddle;
    private readonly TesseractOcrService _tesseract;

    public HybridOcrService(PaddleOcrService paddle, TesseractOcrService tesseract)
        => (_paddle, _tesseract) = (paddle, tesseract);

    public async Task<string> RecognizeAsync(Bitmap image, CancellationToken ct = default)
    {
        using var paddleImage = (Bitmap)image.Clone();
        using var tesseractImage = (Bitmap)image.Clone();
        var paddleTask = TryPaddleTextAsync(paddleImage, ct);
        var tesseractTask = _tesseract.RecognizeAsync(tesseractImage, ct);
        await Task.WhenAll(paddleTask, tesseractTask);
        return string.Join(Environment.NewLine,
            new[] { await paddleTask, await tesseractTask }
                .Where(text => !string.IsNullOrWhiteSpace(text)));
    }

    public Task<IReadOnlyList<OcrTextBlock>> RecognizeBlocksAsync(
        Bitmap image, CancellationToken ct = default)
        => RecognizeBlocksAsync(image, useAllLanguageOrders: true, ct);

    public Task<IReadOnlyList<OcrTextBlock>> RecognizeNeuralBlocksAsync(
        Bitmap image, CancellationToken ct = default)
        => TryPaddleBlocksAsync(image, ct);

    public async Task<IReadOnlyList<OcrTextBlock>> RecognizeBlocksAsync(
        Bitmap image, bool useAllLanguageOrders, CancellationToken ct = default)
    {
        using var paddleImage = (Bitmap)image.Clone();
        using var tesseractImage = (Bitmap)image.Clone();
        var paddleTask = TryPaddleBlocksAsync(paddleImage, ct);
        var tesseractTask = _tesseract.RecognizeBlocksAsync(
            tesseractImage, useAllLanguageOrders, ct);
        await Task.WhenAll(paddleTask, tesseractTask);
        return Merge(await paddleTask, await tesseractTask);
    }

    private async Task<string> TryPaddleTextAsync(Bitmap image, CancellationToken ct)
    {
        try { return await _paddle.RecognizeAsync(image, ct); }
        catch when (!ct.IsCancellationRequested) { return ""; }
    }

    private async Task<IReadOnlyList<OcrTextBlock>> TryPaddleBlocksAsync(
        Bitmap image, CancellationToken ct)
    {
        try { return await _paddle.RecognizeBlocksAsync(image, ct); }
        catch when (!ct.IsCancellationRequested) { return []; }
    }

    private static IReadOnlyList<OcrTextBlock> Merge(
        IReadOnlyList<OcrTextBlock> preferred,
        IReadOnlyList<OcrTextBlock> fallback)
        => preferred.Concat(fallback)
            .GroupBy(block => (Normalize(block.Text), block.Bounds.X / 8, block.Bounds.Y / 8))
            .Select(group => group.OrderByDescending(block => block.Confidence).First())
            .ToList();

    private static string Normalize(string text) =>
        new(text.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
}
