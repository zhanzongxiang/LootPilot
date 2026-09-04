using System.Windows;
using System.Windows.Input;
using TarkovPriceOverlay.Configuration;
using WinForms = System.Windows.Forms;

namespace TarkovPriceOverlay;

public partial class OverlayPositionPickerWindow : Window
{
    private readonly AppSettings _settings;

    public OverlayPositionPickerWindow(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        Loaded += (_, _) => MoveToSavedPosition();
    }

    private void MoveToSavedPosition()
    {
        var screen = WinForms.Screen.AllScreens.FirstOrDefault(candidate =>
                         candidate.DeviceName.Equals(_settings.FixedOverlayScreen,
                             StringComparison.OrdinalIgnoreCase))
                     ?? WinForms.Screen.PrimaryScreen
                     ?? WinForms.Screen.AllScreens[0];
        var area = screen.WorkingArea;
        var scale = Services.ScreenCoordinateMapper.GetScale(screen);
        var width = Width * scale;
        var height = Height * scale;
        var left = Math.Clamp(area.Left + area.Width * Math.Clamp(_settings.FixedOverlayXRatio, 0d, 1d),
            area.Left + 8, area.Right - width - 8);
        var top = Math.Clamp(area.Top + area.Height * Math.Clamp(_settings.FixedOverlayYRatio, 0d, 1d),
            area.Top + 8, area.Bottom - height - 8);
        Services.ScreenCoordinateMapper.MoveToPhysical(this, left, top);
    }

    private void DragSurface_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed && e.OriginalSource is not System.Windows.Controls.Button)
            DragMove();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var bounds = Services.ScreenCoordinateMapper.GetPhysicalBounds(this);
        var center = new System.Drawing.Point(
            bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
        var screen = WinForms.Screen.FromPoint(center);
        var area = screen.WorkingArea;
        _settings.FixedOverlayScreen = screen.DeviceName;
        _settings.FixedOverlayXRatio = Math.Clamp(
            (bounds.Left - area.Left) / Math.Max(1d, area.Width), 0d, 1d);
        _settings.FixedOverlayYRatio = Math.Clamp(
            (bounds.Top - area.Top) / Math.Max(1d, area.Height), 0d, 1d);
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
