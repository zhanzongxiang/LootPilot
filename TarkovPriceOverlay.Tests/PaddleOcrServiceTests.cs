using System.Drawing;
using TarkovPriceOverlay.Services;

namespace TarkovPriceOverlay.Tests;

public sealed class PaddleOcrServiceTests
{
    [Fact]
    public async Task EnginePoolHandlesTwoConcurrentRequests()
    {
        using var service = new PaddleOcrService();
        using var first = CreateBlankImage();
        using var second = CreateBlankImage();

        var results = await Task.WhenAll(
            service.RecognizeBlocksAsync(first),
            service.RecognizeBlocksAsync(second));

        Assert.All(results, result => Assert.NotNull(result));
    }

    private static Bitmap CreateBlankImage()
    {
        var bitmap = new Bitmap(160, 64);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.White);
        return bitmap;
    }
}
