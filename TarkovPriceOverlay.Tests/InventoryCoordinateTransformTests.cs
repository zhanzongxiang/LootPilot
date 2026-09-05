using TarkovPriceOverlay.Configuration;
using TarkovPriceOverlay.Services;
using System.Drawing;

namespace TarkovPriceOverlay.Tests;

public sealed class InventoryCoordinateTransformTests
{
    [Fact]
    public void NativeSixteenByNineCentersReferenceImageAtExpectedSize()
    {
        var transform = InventoryCoordinateTransform.Create(2560, 1440, new AppSettings
        {
            DisplayScalingMode = "Native16x9",
            GameUiScalePercent = 100
        });

        Assert.Equal(1.333f, transform.ScaleX, 3);
        Assert.Equal(1.333f, transform.ScaleY, 3);
        Assert.Equal(0f, transform.OffsetX, 3);
        Assert.Equal(0f, transform.OffsetY, 3);
        Assert.InRange(transform.X(1263), 1683, 1685);
    }

    [Fact]
    public void StretchedModeUsesIndependentAxes()
    {
        var transform = InventoryCoordinateTransform.Create(2560, 1600, new AppSettings
        {
            DisplayScalingMode = "Stretched",
            GameUiScalePercent = 100
        });

        Assert.Equal(2560f / 1920f, transform.ScaleX, 3);
        Assert.Equal(1600f / 1080f, transform.ScaleY, 3);
    }

    [Fact]
    public void InventoryGridDetectorCalibratesUiScaleAtTwoK()
    {
        var settings = new AppSettings
        {
            DisplayScalingMode = "Native16x9",
            GameUiScalePercent = 100,
            EnableLocalGridCalibration = true
        };
        using var image = new Bitmap(2560, 1440);
        using var graphics = Graphics.FromImage(image);
        graphics.Clear(Color.Black);
        using var pen = new Pen(Color.White, 2);
        var actual = InventoryCoordinateTransform.Create(2560, 1440, settings, 104);
        var left = actual.X(1263);
        var top = actual.Y(78);
        var cell = 63.5f * actual.ScaleX;
        for (var column = 0; column <= 8; column++)
        {
            var x = (int)Math.Round(left + column * cell);
            graphics.DrawLine(pen, x, top, x,
                Math.Min(image.Height - 1, (int)Math.Round(top + 12 * cell)));
        }
        for (var row = 0; row <= 12; row++)
        {
            var y = (int)Math.Round(top + row * cell);
            graphics.DrawLine(pen, left, y,
                Math.Min(image.Width - 1, (int)Math.Round(left + 8 * cell)), y);
        }

        var right = new InventoryGridDetector(settings).Detect(image)
            .FirstOrDefault(region => region.Kind is "仓库" or "容器");

        Assert.NotNull(right);
        Assert.InRange(right.CellWidth, cell - 0.5f, cell + 0.5f);
    }
}
