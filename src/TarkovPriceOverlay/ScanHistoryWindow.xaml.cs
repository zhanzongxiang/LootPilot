using System.Windows;
using TarkovPriceOverlay.Services;

namespace TarkovPriceOverlay;

public partial class ScanHistoryWindow : Window
{
    private readonly ScanHistoryService _history;

    public ScanHistoryWindow(ScanHistoryService history)
    {
        InitializeComponent();
        _history = history;
        Loaded += async (_, _) =>
        {
            var entries = await _history.LoadAsync();
            HistoryList.ItemsSource = entries;
            EmptyText.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        };
    }
}
