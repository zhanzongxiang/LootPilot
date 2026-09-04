using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using WinForms = System.Windows.Forms;

namespace TarkovPriceOverlay.Services;

/// <summary>
/// Converts WinForms screen pixels to the WPF device-independent units used by
/// overlay windows. Screen captures and OCR always remain in physical pixels.
/// </summary>
public static class ScreenCoordinateMapper
{
    private const uint MonitorDefaultToNearest = 2;
    private const int EffectiveDpi = 0;
    private const uint NoSize = 0x0001;
    private const uint NoZOrder = 0x0004;
    private const uint NoActivate = 0x0010;

    public static double GetScale(WinForms.Screen screen)
    {
        try
        {
            var bounds = screen.Bounds;
            var native = new NativeRect(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom);
            var monitor = MonitorFromRect(ref native, MonitorDefaultToNearest);
            if (monitor != IntPtr.Zero && GetDpiForMonitor(monitor, EffectiveDpi,
                    out var dpiX, out _) == 0 && dpiX > 0)
                return dpiX / 96d;
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
        catch (ExternalException) { }
        return 1d;
    }

    public static double ToDip(double physicalPixels, WinForms.Screen screen) =>
        physicalPixels / GetScale(screen);

    public static System.Windows.Point ToDip(System.Drawing.Point physical,
        WinForms.Screen screen)
    {
        var scale = GetScale(screen);
        return new System.Windows.Point(physical.X / scale, physical.Y / scale);
    }

    public static void MoveToPhysical(Window window, double left, double top)
    {
        var handle = new WindowInteropHelper(window).EnsureHandle();
        SetWindowPos(handle, IntPtr.Zero, (int)Math.Round(left), (int)Math.Round(top),
            0, 0, NoSize | NoZOrder | NoActivate);
    }

    public static void SetBoundsPhysical(Window window, System.Drawing.Rectangle bounds)
    {
        var handle = new WindowInteropHelper(window).EnsureHandle();
        SetWindowPos(handle, IntPtr.Zero, bounds.Left, bounds.Top, bounds.Width, bounds.Height,
            NoZOrder | NoActivate);
    }

    public static System.Drawing.Rectangle GetPhysicalBounds(Window window)
    {
        var handle = new WindowInteropHelper(window).EnsureHandle();
        if (!GetWindowRect(handle, out var bounds))
            throw new InvalidOperationException("无法读取窗口位置。");
        return System.Drawing.Rectangle.FromLTRB(
            bounds.Left, bounds.Top, bounds.Right, bounds.Bottom);
    }

    [DllImport("Shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType,
        out uint dpiX, out uint dpiY);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromRect(ref NativeRect rect, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter,
        int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect(int left, int top, int right, int bottom)
    {
        public int Left = left;
        public int Top = top;
        public int Right = right;
        public int Bottom = bottom;
    }
}
