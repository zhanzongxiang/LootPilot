using System.Drawing;
using System.Drawing.Imaging;
using System.Net;
using System.Net.Http;
using TarkovPriceOverlay.Services;
using TarkovPriceOverlay.Models;

namespace TarkovPriceOverlay.Tests;

public sealed class InventoryImageMatcherTests
{
    [Fact]
    public void SimilarIconsScoreHigherThanDifferentIcons()
    {
        using var reference = CreateIcon(Color.FromArgb(220, 150, 40));
        using var same = CreateIcon(Color.FromArgb(218, 148, 42));
        using var different = CreateIcon(Color.FromArgb(40, 80, 210));

        var sameScore = InventoryImageMatcher.Compare(reference, same);
        var differentScore = InventoryImageMatcher.Compare(reference, different);

        Assert.True(sameScore > 0.98, $"Expected similar icons to score highly, got {sameScore:0.000}");
        Assert.True(sameScore > differentScore + 0.10,
            $"Expected a clear separation, got same={sameScore:0.000}, different={differentScore:0.000}");
    }

    [Fact]
    public async Task MatchAsyncUsesIconToChooseBetweenSameShortNameCandidates()
    {
        using var temp = new TemporaryDirectory();
        using var screenshot = CreateIcon(Color.FromArgb(220, 150, 40));
        using var matchingIcon = CreateIcon(Color.FromArgb(218, 148, 42));
        using var otherIcon = CreateIcon(Color.FromArgb(40, 80, 210));
        using var http = new HttpClient(new IconHandler(
            ToPng(matchingIcon), ToPng(otherIcon)));
        var matcher = new InventoryImageMatcher(temp.Path, http);
        var first = new ItemPrice("one", "First", "PM", 1, 1, 100, null, null, false, false)
        {
            IconUrl = "https://example.test/one.png"
        };
        var second = new ItemPrice("two", "Second", "PM", 1, 1, 100, null, null, false, false)
        {
            IconUrl = "https://example.test/two.png"
        };

        var result = await matcher.MatchAsync(screenshot, [(first, 1), (second, 1)]);

        Assert.Equal("one", result.Item?.Id);
        Assert.True(result.Score >= 0.62);
    }

    private static Bitmap CreateIcon(Color color)
    {
        var bitmap = new Bitmap(64, 64);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.FromArgb(10, 20, 24));
        graphics.FillEllipse(new SolidBrush(color), 14, 10, 36, 44);
        return bitmap;
    }

    private static byte[] ToPng(Bitmap bitmap)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }

    private sealed class IconHandler(byte[] first, byte[] second) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var bytes = request.RequestUri?.AbsolutePath.EndsWith("one.png") == true
                ? first : second;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(bytes)
            });
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "LootPilotTests", Guid.NewGuid().ToString("N"));

        public TemporaryDirectory() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}
