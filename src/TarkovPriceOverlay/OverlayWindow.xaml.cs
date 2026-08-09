using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using TarkovPriceOverlay.Models;
using TarkovPriceOverlay.Configuration;
using WinForms = System.Windows.Forms;

namespace TarkovPriceOverlay;

public partial class OverlayWindow : Window
{
    private readonly int _durationMs;
    public OverlayWindow(ItemPrice item, int durationMs)
    {
        InitializeComponent();
        _durationMs = durationMs;
        NameText.Text = item.Name;
        FleaText.Text = Money(item.FleaPrice);
        TraderText.Text = Money(item.TraderPrice);
        TraderNameText.Text = item.TraderName ?? "—";
        PerSlotText.Text = $"每格 {Money(item.PricePerSlot)} · {item.Width}×{item.Height}";
        UsageText.Text = string.Join("  ", new[]
        {
            item.UsedInTasks ? "任务用途" : null,
            item.UsedInHideout ? "藏身处用途" : null
        }.Where(x => x is not null));
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var style = GetWindowLong(hwnd, -20);
            SetWindowLong(hwnd, -20, style | 0x00000020 | 0x08000000);
        };
    }

    public void ShowNearCursor()
    {
        var cursor = WinForms.Cursor.Position;
        var area = WinForms.Screen.FromPoint(cursor).WorkingArea;
        Left = Math.Min(cursor.X + 24, area.Right - Width - 8);
        Top = Math.Min(cursor.Y + 24, area.Bottom - 210);
        Show();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(_durationMs) };
        timer.Tick += (_, _) => { timer.Stop(); Close(); };
        timer.Start();
    }

    public void ShowAtConfiguredPosition(AppSettings settings)
    {
        var screen = WinForms.Screen.AllScreens.FirstOrDefault(candidate =>
                         candidate.DeviceName.Equals(settings.FixedOverlayScreen,
                             StringComparison.OrdinalIgnoreCase))
                     ?? WinForms.Screen.PrimaryScreen
                     ?? WinForms.Screen.AllScreens[0];
        var area = screen.WorkingArea;
        var xRatio = Math.Clamp(settings.FixedOverlayXRatio, 0d, 1d);
        var yRatio = Math.Clamp(settings.FixedOverlayYRatio, 0d, 1d);
        Left = Math.Clamp(area.Left + area.Width * xRatio, area.Left + 8, area.Right - Width - 8);
        Top = Math.Clamp(area.Top + area.Height * yRatio, area.Top + 8, area.Bottom - 210);
        ShowWithTimer();
    }

    private void ShowWithTimer()
    {
        Show();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(_durationMs) };
        timer.Tick += (_, _) => { timer.Stop(); Close(); };
        timer.Start();
    }

    private static string Money(int? value) => value is > 0
        ? $"{value.Value / 10_000d:0.##}万 ₽"
        : "--";
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
}
