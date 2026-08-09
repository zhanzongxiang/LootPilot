using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;
using TarkovPriceOverlay.Configuration;

namespace TarkovPriceOverlay.Services;

public sealed class ScreenCaptureService
{
    private readonly AppSettings _settings;
    public ScreenCaptureService(AppSettings settings) => _settings = settings;

    public IReadOnlyList<CapturedRegion> CaptureCandidates()
    {
        // Auto must stay anchored to the cursor. Scanning the fixed center area
        // on every press can return a perfectly spelled but unrelated item.
        return string.Equals(_settings.CaptureMode, "InspectWindow", StringComparison.OrdinalIgnoreCase)
            ? [CaptureInspectTitleRegion()]
            : [CaptureAroundCursor()];
    }

    public CapturedRegion CaptureCurrentScreen()
    {
        var bounds = Screen.FromPoint(Cursor.Position).Bounds;
        return new("当前完整屏幕", Capture(bounds.Left, bounds.Top, bounds.Width, bounds.Height),
            new Point(bounds.Left, bounds.Top));
    }

    private CapturedRegion CaptureAroundCursor()
    {
        var width = Math.Min(_settings.CaptureWidth, SystemInformation.VirtualScreen.Width);
        var height = Math.Min(_settings.CaptureHeight, SystemInformation.VirtualScreen.Height);
        var cursor = Cursor.Position;
        var bounds = SystemInformation.VirtualScreen;
        var left = Math.Clamp(cursor.X - width / 2, bounds.Left, bounds.Right - width);
        var top = Math.Clamp(cursor.Y - height / 2, bounds.Top, bounds.Bottom - height);
        return new("鼠标附近", Capture(left, top, width, height), new Point(left, top), cursor);
    }

    private CapturedRegion CaptureInspectTitleRegion()
    {
        var screen = Screen.FromPoint(Cursor.Position).Bounds;
        var width = Math.Min(_settings.CaptureWidth, screen.Width);
        var height = Math.Min(_settings.CaptureHeight, screen.Height);
        var left = screen.Left + (screen.Width - width) / 2;
        var top = screen.Top + Math.Max(0, (int)(screen.Height * 0.08));
        return new("检查窗口标题区", Capture(left, top, width, height), new Point(left, top));
    }

    private static Bitmap Capture(int left, int top, int width, int height)
    {
        var bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(left, top, 0, 0, bitmap.Size, CopyPixelOperation.SourceCopy);
        return bitmap;
    }
}

public sealed record CapturedRegion(
    string Name,
    Bitmap Image,
    Point Origin = default,
    Point? TargetScreenPoint = null) : IDisposable
{
    public void Dispose() => Image.Dispose();
}
