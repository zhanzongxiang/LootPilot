using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using TarkovPriceOverlay.Models;
using WinForms = System.Windows.Forms;
using MediaColor = System.Windows.Media.Color;

namespace TarkovPriceOverlay;

public partial class InventoryOverlayWindow : Window
{
    private readonly IReadOnlyList<DetectedInventoryItem> _items;
    private readonly int _durationMs;
    private readonly int _minimumDisplayPrice;

    public InventoryOverlayWindow(IReadOnlyList<DetectedInventoryItem> items, int durationMs,
        int minimumDisplayPrice = 0)
    {
        InitializeComponent();
        (_items, _durationMs, _minimumDisplayPrice) = (items, durationMs, minimumDisplayPrice);
        var point = items.Count > 0
            ? new System.Drawing.Point(items[0].ScreenBounds.X, items[0].ScreenBounds.Y)
            : WinForms.Cursor.Position;
        var screen = WinForms.Screen.FromPoint(point).Bounds;
        Left = screen.Left;
        Top = screen.Top;
        Width = screen.Width;
        Height = screen.Height;
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var style = GetWindowLong(hwnd, -20);
            SetWindowLong(hwnd, -20, style | 0x00000020 | 0x08000000);
        };
        Loaded += (_, _) => Populate(screen.Left, screen.Top);
    }

    private void Populate(int originX, int originY)
    {
        foreach (var detected in _items)
        {
            if ((detected.Item.BestPrice ?? 0) < _minimumDisplayPrice) continue;
            var lines = new StackPanel();
            if (detected.Item.FleaPrice is int flea && flea > 0)
                lines.Children.Add(PriceLine($"跳 {FormatPrice(flea)}", MediaColor.FromRgb(217, 189, 119)));
            if (detected.Item.TraderPrice is int trader && trader > 0)
                lines.Children.Add(PriceLine($"商 {FormatPrice(trader)}", MediaColor.FromRgb(181, 185, 162)));
            if (lines.Children.Count == 0) continue;
            var label = new Border
            {
                Background = new SolidColorBrush(MediaColor.FromArgb(232, 10, 12, 11)),
                BorderBrush = new SolidColorBrush(MediaColor.FromArgb(215, 141, 123, 85)),
                BorderThickness = new Thickness(2, 0, 0, 1),
                CornerRadius = new CornerRadius(0),
                Padding = new Thickness(3, 1, 4, 1),
                Child = lines
            };
            label.Measure(new System.Windows.Size(
                double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(label, detected.ScreenBounds.Left - originX + 2);
            Canvas.SetTop(label, detected.ScreenBounds.Bottom - originY -
                                 label.DesiredSize.Height - 2);
            OverlayCanvas.Children.Add(label);
        }
        AddSummary(originX, originY);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(_durationMs) };
        timer.Tick += (_, _) => { timer.Stop(); Close(); };
        timer.Start();
    }

    private void AddSummary(int originX, int originY)
    {
        var fleaTotal = _items.Sum(item => (long)(item.Item.FleaPrice ?? 0));
        var traderTotal = _items.Sum(item => (long)(item.Item.TraderPrice ?? 0));
        var bestTotal = _items.Sum(item => (long)(item.Item.BestPrice ?? 0));
        var content = new StackPanel();
        content.Children.Add(new TextBlock
        {
            Text = $"扫描估值 · {_items.Count} 件",
            Foreground = new SolidColorBrush(MediaColor.FromRgb(215, 210, 198)),
            FontFamily = new System.Windows.Media.FontFamily("Microsoft YaHei UI"),
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 5)
        });
        content.Children.Add(SummaryLine("最佳总价值", bestTotal,
            MediaColor.FromRgb(218, 190, 119)));
        content.Children.Add(SummaryLine("跳蚤合计", fleaTotal,
            MediaColor.FromRgb(194, 153, 218)));
        content.Children.Add(SummaryLine("商人合计", traderTotal,
            MediaColor.FromRgb(181, 185, 162)));
        content.Children.Add(new TextBlock
        {
            Text = "按已识别物品估算",
            Foreground = new SolidColorBrush(MediaColor.FromRgb(122, 126, 117)),
            FontFamily = new System.Windows.Media.FontFamily("Microsoft YaHei UI"),
            FontSize = 9,
            Margin = new Thickness(0, 4, 0, 0)
        });

        var summary = new Border
        {
            Background = new SolidColorBrush(MediaColor.FromArgb(242, 10, 12, 11)),
            BorderBrush = new SolidColorBrush(MediaColor.FromArgb(230, 141, 123, 85)),
            BorderThickness = new Thickness(2, 1, 1, 1),
            Padding = new Thickness(12, 9, 14, 9),
            Child = content
        };
        Canvas.SetLeft(summary, Math.Max(12, Width - 238));
        Canvas.SetTop(summary, 58);
        OverlayCanvas.Children.Add(summary);
    }

    private static TextBlock SummaryLine(string label, long value, MediaColor color) => new()
    {
        Text = $"{label}  {FormatPrice(value)} ₽",
        Foreground = new SolidColorBrush(color),
        FontFamily = new System.Windows.Media.FontFamily("Microsoft YaHei UI"),
        FontSize = 11,
        FontWeight = FontWeights.SemiBold,
        LineHeight = 17
    };

    private static TextBlock PriceLine(string text, MediaColor color) => new()
    {
        Text = text,
        Foreground = new SolidColorBrush(color),
        FontFamily = new System.Windows.Media.FontFamily("Microsoft YaHei UI"),
        FontSize = 9,
        FontWeight = FontWeights.SemiBold,
        LineHeight = 12
    };

    private static string FormatPrice(int value) => $"{value / 10_000d:0.##}万";
    private static string FormatPrice(long value) => $"{value / 10_000d:0.##}万";

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
}
