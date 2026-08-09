using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace TarkovPriceOverlay.Services;

public static class OcrImagePreprocessor
{
    public static Bitmap CreatePrimaryVariant(Bitmap source, int scale) =>
        Transform(source, Math.Clamp(scale, 1, 4), threshold: null);

    public static Bitmap CreateColorVariant(Bitmap source, int scale) =>
        ScalePreservingColor(source, Math.Clamp(scale, 1, 4));

    public static IReadOnlyList<Bitmap> CreateVariants(Bitmap source, int scale)
    {
        scale = Math.Clamp(scale, 1, 4);
        return
        [
            Transform(source, scale, threshold: null),
            ScalePreservingColor(source, scale),
            Transform(source, scale, threshold: 0.58f)
        ];
    }

    private static Bitmap ScalePreservingColor(Bitmap source, int scale)
    {
        var target = new Bitmap(source.Width * scale, source.Height * scale, PixelFormat.Format24bppRgb);
        target.SetResolution(96 * scale, 96 * scale);
        using var graphics = Graphics.FromImage(target);
        graphics.Clear(Color.Black);
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.CompositingQuality = CompositingQuality.HighQuality;
        graphics.DrawImage(source, new Rectangle(0, 0, target.Width, target.Height));
        return target;
    }

    private static Bitmap Transform(Bitmap source, int scale, float? threshold)
    {
        var target = new Bitmap(source.Width * scale, source.Height * scale, PixelFormat.Format24bppRgb);
        target.SetResolution(96 * scale, 96 * scale);
        using var graphics = Graphics.FromImage(target);
        graphics.Clear(Color.White);
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

        var matrix = new ColorMatrix(new[]
        {
            new[] { 0.45f, 0.45f, 0.45f, 0f, 0f },
            new[] { 0.45f, 0.45f, 0.45f, 0f, 0f },
            new[] { 0.45f, 0.45f, 0.45f, 0f, 0f },
            new[] { 0f, 0f, 0f, 1f, 0f },
            new[] { -0.18f, -0.18f, -0.18f, 0f, 1f }
        });
        using var attributes = new ImageAttributes();
        attributes.SetColorMatrix(matrix);
        if (threshold is float value) attributes.SetThreshold(value);
        graphics.DrawImage(source,
            new Rectangle(0, 0, target.Width, target.Height),
            0, 0, source.Width, source.Height, GraphicsUnit.Pixel, attributes);
        return target;
    }
}
