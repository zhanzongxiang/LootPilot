using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using TarkovPriceOverlay.Configuration;
using TarkovPriceOverlay.Interop;
using TarkovPriceOverlay.Models;
using TarkovPriceOverlay.Services;

namespace TarkovPriceOverlay;

public partial class MainWindow : Window
{
    private readonly AppSettings _settings;
    private readonly ItemCatalogService _catalog;
    private readonly ScanCoordinator _coordinator;
    private readonly InventoryScanCoordinator _inventoryCoordinator;
    private readonly ScanHistoryService _history;
    private readonly GlobalHotkey _hotkey = new();
    private readonly GlobalHotkey _inventoryHotkey = new();
    private readonly DispatcherTimer _refreshTimer = new();
    private readonly CancellationTokenSource _lifetimeCts = new();
    private CancellationTokenSource? _scanCts;
    private DateTimeOffset? _nextRefreshAt;
    private bool _busy;
    private bool _loadingSettings;

    public MainWindow(AppSettings settings, ItemCatalogService catalog,
        ScanCoordinator coordinator, InventoryScanCoordinator inventoryCoordinator,
        ScanHistoryService history)
    {
        InitializeComponent();
        (_settings, _catalog, _coordinator, _inventoryCoordinator, _history) =
            (settings, catalog, coordinator, inventoryCoordinator, history);
        LoadSettingsIntoUi();
        _refreshTimer.Interval = TimeSpan.FromMinutes(Math.Max(1, settings.RefreshIntervalMinutes));
        _refreshTimer.Tick += async (_, _) => await AutomaticRefreshAsync();
        Loaded += OnLoaded;
        Closed += (_, _) =>
        {
            _refreshTimer.Stop();
            _lifetimeCts.Cancel();
            _scanCts?.Cancel();
            _hotkey.Dispose();
            _inventoryHotkey.Dispose();
            _lifetimeCts.Dispose();
        };
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        string? hotkeyWarning = null;
        try
        {
            try
            {
                NormalizeHotkeySettings();
                RegisterHotkeys();
                _settings.Save();
            }
            catch (Exception ex)
            {
                hotkeyWarning = ex.Message;
                _hotkey.Unregister();
                _inventoryHotkey.Unregister();
            }
            StatusText.Text = "正在加载本地缓存并刷新物价…";
            var refreshed = await _catalog.InitializeAsync();
            UpdateCacheStatus();
            ScheduleNextRefresh();
            _refreshTimer.Start();
            StatusText.Text = hotkeyWarning is not null ? "快捷键尚未启用" : refreshed ? "已就绪" : _catalog.Count > 0
                ? "联网刷新失败，正在使用本地缓存" : "物价接口暂时不可用";
            if (hotkeyWarning is not null)
                LastResultText.Text = hotkeyWarning + " 请在右侧重新设置后点击“保存并应用”。";
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private void NormalizeHotkeySettings()
    {
        _settings.Hotkey = GlobalHotkey.Normalize(_settings.Hotkey);
        _settings.InventoryHotkey = GlobalHotkey.Normalize(_settings.InventoryHotkey);
        LoadSettingsIntoUi();
    }

    private void RegisterHotkeys()
    {
        _hotkey.Unregister();
        _inventoryHotkey.Unregister();
        _hotkey.Pressed -= HotkeyPressed;
        _inventoryHotkey.Pressed -= InventoryHotkeyPressed;
        _hotkey.Pressed += HotkeyPressed;
        _inventoryHotkey.Pressed += InventoryHotkeyPressed;
        try
        {
            _hotkey.Register(this, _settings.Hotkey);
            _inventoryHotkey.Register(this, _settings.InventoryHotkey);
        }
        catch
        {
            _hotkey.Unregister();
            _inventoryHotkey.Unregister();
            throw;
        }
        HotkeyRun.Text = _settings.Hotkey;
        InventoryHotkeyRun.Text = _settings.InventoryHotkey;
    }

    private async void HotkeyPressed(object? sender, EventArgs e)
    {
        if (_busy) { _scanCts?.Cancel(); return; }
        await ScanAsync();
    }

    private async void InventoryHotkeyPressed(object? sender, EventArgs e)
    {
        if (_busy) { _scanCts?.Cancel(); return; }
        await ScanInventoryAsync();
    }

    private async Task ScanAsync()
    {
        if (_busy) return;
        _busy = true;
        using var scanCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
        _scanCts = scanCts;
        ScanFeedbackWindow? feedback = null;
        CloseScanOverlays();
        await Task.Delay(40);
        try
        {
            StatusText.Text = "正在截图并识别…";
            var stopwatch = Stopwatch.StartNew();
            var result = await _coordinator.ScanAsync(
                () => feedback = ShowFeedback("正在识别单个物品…"), scanCts.Token);
            stopwatch.Stop();
            ShowResult(result);
            LastResultText.Text += $" · {RecognitionModeText()} {stopwatch.Elapsed.TotalSeconds:0.00}秒";
            feedback?.SetMessage(result.Item is null ? "没有可靠结果" : $"完成 {stopwatch.Elapsed.TotalSeconds:0.0}秒");
            await Task.Delay(500);
            StatusText.Text = "已就绪";
        }
        catch (OperationCanceledException) { StatusText.Text = "扫描已取消"; }
        catch (Exception ex) { ShowError(ex); feedback?.SetMessage("识别失败"); await Task.Delay(800); }
        finally { feedback?.Close(); if (ReferenceEquals(_scanCts, scanCts)) _scanCts = null; _busy = false; }
    }

    private async Task ScanInventoryAsync()
    {
        if (_busy) return;
        _busy = true;
        using var scanCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
        _scanCts = scanCts;
        ScanFeedbackWindow? feedback = null;
        CloseScanOverlays();
        await Task.Delay(40);
        try
        {
            StatusText.Text = "正在扫描仓库和随身物品区域…";
            var stopwatch = Stopwatch.StartNew();
            var items = await _inventoryCoordinator.ScanAsync(
                () => feedback = ShowFeedback("正在扫描并读取物价…"), scanCts.Token);
            stopwatch.Stop();
            if (items.Count == 0)
            {
                LastResultText.Text = "整屏扫描完成，但没有找到足够可靠的物品名称。";
                feedback?.SetMessage("没有可靠结果");
                await Task.Delay(900);
            }
            else
            {
                LastResultText.Text = $"整屏扫描完成：标注 {items.Count} 件物品。";
                feedback?.SetMessage($"{items.Count} 件 · {stopwatch.Elapsed.TotalSeconds:0.0}秒");
                var fleaTotal = items.Sum(x => (long)(x.Item.FleaPrice ?? 0));
                var traderTotal = items.Sum(x => (long)(x.Item.TraderPrice ?? 0));
                var bestTotal = items.Sum(x => (long)(x.Item.BestPrice ?? 0));
                feedback?.SetMessage($"{items.Count} 件 · 估值 {FormatPrice(bestTotal)}");
                await _history.AddAsync(new ScanHistoryEntry(
                    DateTimeOffset.Now, _settings.GameMode, items.Count, fleaTotal, traderTotal));
                LastResultText.Text = $"识别 {items.Count} 件 · {RecognitionModeText()} {stopwatch.Elapsed.TotalSeconds:0.00}秒 · " +
                                      $"最佳总价值 {FormatPrice(bestTotal)} · 跳蚤合计 {FormatPrice(fleaTotal)} · " +
                                      $"商人合计 {FormatPrice(traderTotal)}";
                new InventoryOverlayWindow(items, _settings.InventoryOverlayDurationMs,
                    _settings.MinimumDisplayPrice).Show();
                await Task.Delay(450);
            }
            StatusText.Text = "已就绪";
        }
        catch (OperationCanceledException) { StatusText.Text = "扫描已取消"; }
        catch (Exception ex) { ShowError(ex); feedback?.SetMessage("扫描失败"); await Task.Delay(800); }
        finally { feedback?.Close(); if (ReferenceEquals(_scanCts, scanCts)) _scanCts = null; _busy = false; }
    }

    private static void CloseScanOverlays()
    {
        foreach (var window in System.Windows.Application.Current.Windows.Cast<Window>()
                     .Where(window => window is OverlayWindow or InventoryOverlayWindow or ScanFeedbackWindow)
                     .ToArray())
        {
            window.Close();
        }
    }

    private ScanFeedbackWindow ShowFeedback(string message)
    {
        var feedback = new ScanFeedbackWindow(message);
        feedback.Show();
        feedback.UpdateLayout();
        return feedback;
    }

    private void LoadSettingsIntoUi()
    {
        _loadingSettings = true;
        HotkeyInput.Text = _settings.Hotkey;
        InventoryHotkeyInput.Text = _settings.InventoryHotkey;
        MinimumPriceInput.Text = _settings.MinimumDisplayPrice.ToString();
        FixedOverlayPosition.IsChecked = _settings.UseFixedOverlayPosition;
        DisplayScalingModeInput.SelectedValue = _settings.DisplayScalingMode;
        if (DisplayScalingModeInput.SelectedIndex < 0) DisplayScalingModeInput.SelectedValue = "Auto";
        GameUiScaleInput.SelectedValue = _settings.GameUiScalePercent.ToString();
        if (GameUiScaleInput.SelectedIndex < 0) GameUiScaleInput.SelectedValue = "100";
        EnableLocalGridCalibration.IsChecked = _settings.EnableLocalGridCalibration;
        UpdateFixedOverlayPositionText();
        SeasonMode.IsChecked = _settings.GameMode.Equals("Season", StringComparison.OrdinalIgnoreCase);
        PveMode.IsChecked = _settings.GameMode.Equals("PvE", StringComparison.OrdinalIgnoreCase);
        PvpMode.IsChecked = PveMode.IsChecked != true && SeasonMode.IsChecked != true;
        RecognitionFast.IsChecked = _settings.RecognitionMode.Equals("Fast", StringComparison.OrdinalIgnoreCase);
        RecognitionParallel.IsChecked = _settings.RecognitionMode.Equals("Parallel", StringComparison.OrdinalIgnoreCase);
        RecognitionBalanced.IsChecked = RecognitionFast.IsChecked != true && RecognitionParallel.IsChecked != true;
        var themeButton = new[] { ThemeMist, ThemeNight, ThemeMint, ThemeSand, ThemeMono }
            .FirstOrDefault(x => Equals(x.Tag, _settings.Theme)) ?? ThemeNight;
        themeButton.IsChecked = true;
        _loadingSettings = false;
        UpdateModeBadge();
    }

    private void ThemeChoice_Checked(object sender, RoutedEventArgs e)
    {
        if (_loadingSettings || sender is not System.Windows.Controls.RadioButton { Tag: string themeName }) return;
        _settings.Theme = themeName;
        AppThemeManager.Apply(themeName);
        _settings.Save();
        if (StatusText is not null) StatusText.Text = $"已切换界面主题：{themeName}";
    }

    private void RecognitionMode_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || _loadingSettings) return;
        _settings.RecognitionMode = RecognitionFast.IsChecked == true ? "Fast"
            : RecognitionParallel.IsChecked == true ? "Parallel" : "Balanced";
        _settings.Save();
        StatusText.Text = $"识别性能已切换为：{RecognitionModeText()}";
    }

    private async void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        var oldSingle = _settings.Hotkey;
        var oldInventory = _settings.InventoryHotkey;
        var previousMode = _settings.GameMode;
        var oldRecognitionMode = _settings.RecognitionMode;
        var oldMinimumPrice = _settings.MinimumDisplayPrice;
        var oldFixedPosition = _settings.UseFixedOverlayPosition;
        var oldDisplayScalingMode = _settings.DisplayScalingMode;
        var oldGameUiScalePercent = _settings.GameUiScalePercent;
        var oldLocalCalibration = _settings.EnableLocalGridCalibration;
        try
        {
            var single = GlobalHotkey.Normalize(HotkeyInput.Text.Trim());
            var inventory = GlobalHotkey.Normalize(InventoryHotkeyInput.Text.Trim());
            if (single.Equals(inventory, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("两个快捷键不能相同。");
            if (!int.TryParse(MinimumPriceInput.Text.Trim(), out var minimumPrice) || minimumPrice < 0)
                throw new InvalidOperationException("最低显示价值必须是大于或等于 0 的整数。");

            _settings.Hotkey = single;
            _settings.InventoryHotkey = inventory;
            _settings.GameMode = SeasonMode.IsChecked == true ? "Season"
                : PveMode.IsChecked == true ? "PvE" : "PvP";
            _settings.RecognitionMode = RecognitionFast.IsChecked == true ? "Fast"
                : RecognitionParallel.IsChecked == true ? "Parallel" : "Balanced";
            _settings.MinimumDisplayPrice = minimumPrice;
            _settings.UseFixedOverlayPosition = FixedOverlayPosition.IsChecked == true;
            _settings.DisplayScalingMode = DisplayScalingModeInput.SelectedValue?.ToString() ?? "Auto";
            if (!int.TryParse(GameUiScaleInput.SelectedValue?.ToString(), out var gameUiScale))
                gameUiScale = 100;
            _settings.GameUiScalePercent = Math.Clamp(gameUiScale, 70, 130);
            _settings.EnableLocalGridCalibration = EnableLocalGridCalibration.IsChecked == true;
            RegisterHotkeys();
            _settings.Save();
            UpdateModeBadge();
            StatusText.Text = "设置已保存并应用";
            LastResultText.Text = $"{GameModeText()} 数据 · {RecognitionModeText()} · " +
                                  $"{_settings.Hotkey} 单物品 · {_settings.InventoryHotkey} 整屏";

            if (!previousMode.Equals(_settings.GameMode, StringComparison.OrdinalIgnoreCase))
            {
                StatusText.Text = $"正在切换到 {GameModeText()} 物价…";
                await _catalog.ReloadCacheAsync();
                var refreshed = await _catalog.RefreshAsync();
                UpdateCacheStatus();
                StatusText.Text = refreshed || _catalog.Count > 0
                    ? $"已切换到 {GameModeText()} 数据"
                    : $"{GameModeText()} 接口暂不可用，且没有该模式的缓存";
            }
        }
        catch (Exception ex)
        {
            _settings.Hotkey = oldSingle;
            _settings.InventoryHotkey = oldInventory;
            _settings.GameMode = previousMode;
            _settings.RecognitionMode = oldRecognitionMode;
            _settings.MinimumDisplayPrice = oldMinimumPrice;
            _settings.UseFixedOverlayPosition = oldFixedPosition;
            _settings.DisplayScalingMode = oldDisplayScalingMode;
            _settings.GameUiScalePercent = oldGameUiScalePercent;
            _settings.EnableLocalGridCalibration = oldLocalCalibration;
            try { RegisterHotkeys(); } catch { }
            ShowError(ex);
            LoadSettingsIntoUi();
        }
    }

    private void HotkeyInput_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftAlt or Key.RightAlt or Key.LeftCtrl or Key.RightCtrl or
            Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            return;
        if (key is Key.Tab or Key.Escape) return;

        if (sender is System.Windows.Controls.TextBox input)
        {
            input.Text = GlobalHotkey.Format(new GlobalHotkey.ParsedHotkey(key, Keyboard.Modifiers));
            input.CaretIndex = input.Text.Length;
            e.Handled = true;
        }
    }

    private void UpdateModeBadge() => ModeBadge.Text = $"{GameModeText()} 数据";

    private string GameModeText() => _settings.GameMode.Equals("Season", StringComparison.OrdinalIgnoreCase)
        ? "赛季服"
        : _settings.GameMode;

    private void CaptureOverlayPosition_Click(object sender, RoutedEventArgs e)
    {
        var picker = new OverlayPositionPickerWindow(_settings) { Owner = this };
        if (picker.ShowDialog() != true) return;
        FixedOverlayPosition.IsChecked = true;
        _settings.UseFixedOverlayPosition = true;
        _settings.Save();
        UpdateFixedOverlayPositionText();
        StatusText.Text = "已记录单物品价格窗口位置";
    }

    private void UpdateFixedOverlayPositionText()
    {
        if (FixedOverlayPositionText is null) return;
        FixedOverlayPositionText.Text = _settings.FixedOverlayScreen is null
            ? "尚未记录位置"
            : $"已记录：{_settings.FixedOverlayScreen} · " +
              $"横向 {_settings.FixedOverlayXRatio:P0} / 纵向 {_settings.FixedOverlayYRatio:P0}";
    }

    private string RecognitionModeText() => _settings.RecognitionMode.ToLowerInvariant() switch
    {
        "fast" => "极速识别",
        "parallel" => "精确识别",
        _ => "平衡识别"
    };

    private void HistoryButton_Click(object sender, RoutedEventArgs e) =>
        new ScanHistoryWindow(_history) { Owner = this }.Show();

    private void ManualTest_Click(object sender, RoutedEventArgs e) => ShowResult(_coordinator.MatchText(ManualInput.Text));

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshButton.IsEnabled = false;
        try
        {
            StatusText.Text = $"正在刷新 {GameModeText()} 物价…";
            var refreshed = await _catalog.RefreshAsync();
            UpdateCacheStatus();
            ScheduleNextRefresh();
            StatusText.Text = refreshed ? "物价已刷新" : _catalog.Count > 0
                ? "刷新失败，继续使用本地缓存" : "刷新失败，当前没有可用数据";
        }
        catch (Exception ex) { ShowError(ex); }
        finally { RefreshButton.IsEnabled = true; }
    }

    private void ShowResult(RecognitionResult result)
    {
        if (result.Item is null)
        {
            LastResultText.Text = $"未找到可靠匹配（{result.Confidence:P0}）。OCR：{result.RawText}";
            return;
        }
        LastResultText.Text = $"已识别：{result.Item.Name}（匹配度 {result.Confidence:P0}）";
        var overlay = new OverlayWindow(result.Item, _settings.OverlayDurationMs);
        if (_settings.UseFixedOverlayPosition)
            overlay.ShowAtConfiguredPosition(_settings);
        else
            overlay.ShowNearCursor();
    }

    private void UpdateCacheStatus() => CacheText.Text =
        $"{_catalog.Count:N0} 件物品 · {GameModeText()} · 来源 {_catalog.CurrentSource} · 更新于 {_catalog.UpdatedAt?.ToLocalTime():g}";

    private void ScheduleNextRefresh()
    {
        _nextRefreshAt = DateTimeOffset.Now.Add(_refreshTimer.Interval);
        NextRefreshText.Text = $"下次自动刷新：{_nextRefreshAt:g}（每 {_settings.RefreshIntervalMinutes} 分钟）";
    }

    private async Task AutomaticRefreshAsync()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            StatusText.Text = "正在后台自动刷新物价…";
            var refreshed = await _catalog.RefreshAsync();
            UpdateCacheStatus();
            StatusText.Text = refreshed ? "自动刷新完成" : "自动刷新失败，继续使用缓存";
        }
        catch (Exception ex) { ShowError(ex); }
        finally { ScheduleNextRefresh(); _busy = false; }
    }

    private void ShowError(Exception ex)
    {
        StatusText.Text = "发生错误";
        LastResultText.Text = ex.Message;
    }

    private static string FormatPrice(long value) => $"{value / 10_000d:0.##}万 ₽";
}
