using TarkovPriceOverlay.Configuration;

namespace TarkovPriceOverlay.Services;

public readonly record struct InventoryCoordinateTransform(
    float ScaleX, float ScaleY, float OffsetX, float OffsetY)
{
    private const float ReferenceWidth = 1920f;
    private const float ReferenceHeight = 1080f;

    public int X(float referenceX) => (int)Math.Round(OffsetX + referenceX * ScaleX);
    public int Y(float referenceY) => (int)Math.Round(OffsetY + referenceY * ScaleY);

    public static InventoryCoordinateTransform Create(
        int width, int height, AppSettings settings)
    {
        var mode = settings.DisplayScalingMode.ToLowerInvariant();
        var rawX = width / ReferenceWidth;
        var rawY = height / ReferenceHeight;
        float scaleX;
        float scaleY;
        float offsetX;
        float offsetY;

        var aspect = width / (double)height;
        var knownNativeAspect = Math.Abs(aspect - 16d / 9d) < 0.025 ||
                                Math.Abs(aspect - 16d / 10d) < 0.025;
        if (mode is "native16x9" or "native16x10" ||
            mode == "auto" && knownNativeAspect)
        {
            var uniform = Math.Min(rawX, rawY);
            scaleX = scaleY = uniform;
            offsetX = (width - ReferenceWidth * uniform) / 2f;
            offsetY = (height - ReferenceHeight * uniform) / 2f;
        }
        else
        {
            // Auto preserves legacy behavior on unknown ratios. Explicit
            // Stretched uses the same independent X/Y mapping by design.
            scaleX = rawX;
            scaleY = rawY;
            offsetX = offsetY = 0;
        }

        var uiScale = Math.Clamp(settings.GameUiScalePercent, 70, 130) / 100f;
        if (Math.Abs(uiScale - 1f) > 0.001f)
        {
            var oldWidth = ReferenceWidth * scaleX;
            var oldHeight = ReferenceHeight * scaleY;
            scaleX *= uiScale;
            scaleY *= uiScale;
            offsetX += (oldWidth - ReferenceWidth * scaleX) / 2f;
            offsetY += (oldHeight - ReferenceHeight * scaleY) / 2f;
        }
        return new(scaleX, scaleY, offsetX, offsetY);
    }
}
