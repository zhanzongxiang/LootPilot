using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using WinForms = System.Windows.Forms;

namespace TarkovPriceOverlay;

public partial class ScanFeedbackWindow : Window
{
    public ScanFeedbackWindow(string message = "正在扫描…")
    {
        InitializeComponent();
        MessageText.Text = message;
        var screenDevice = WinForms.Screen.FromPoint(WinForms.Cursor.Position);
        var screen = screenDevice.Bounds;
        // Empty strip immediately above the headset slot in Tarkov's
        // equipment layout. This keeps feedback visible without covering loot.
        var physicalLeft = screen.Left + screen.Width * (66d / 1920d);
        var physicalTop = screen.Top + screen.Height * (88d / 1080d);
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var style = GetWindowLong(hwnd, -20);
            SetWindowLong(hwnd, -20, style | 0x00000020 | 0x08000000);
            Services.ScreenCoordinateMapper.MoveToPhysical(this, physicalLeft, physicalTop);
        };
    }

    public void SetMessage(string message) => MessageText.Text = message;

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
}
