using TarkovPriceOverlay.Configuration;
using TarkovPriceOverlay.Services;

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
}
