using System.Text.Json;

namespace TarkovPriceOverlay.Configuration;

public sealed class AppSettings
{
    public string GameMode { get; set; } = "PvP";
    public string Theme { get; set; } = "极夜蓝";
    public string RecognitionMode { get; set; } = "Balanced";
    public string Hotkey { get; set; } = "Alt+Q";
    public string InventoryHotkey { get; set; } = "Alt+W";
    public string ApiUrl { get; set; } = "https://api.tarkov.dev/graphql";
    public string EftarkovApiUrl { get; set; } = "https://api.eftarkov.com/boss.php?id=10";
    public string CachePath { get; set; } = "%LOCALAPPDATA%/LootPilot/items.json";
    public int RefreshIntervalMinutes { get; set; } = 60;
    public int CaptureWidth { get; set; } = 900;
    public int CaptureHeight { get; set; } = 220;
    public string CaptureMode { get; set; } = "Auto";
    public int OcrImageScale { get; set; } = 2;
    public int OverlayDurationMs { get; set; } = 3500;
    public bool UseFixedOverlayPosition { get; set; }
    public double FixedOverlayXRatio { get; set; } = 0.75;
    public double FixedOverlayYRatio { get; set; } = 0.15;
    public string? FixedOverlayScreen { get; set; }
    public int InventoryOverlayDurationMs { get; set; } = 8000;
    public int MinimumDisplayPrice { get; set; } = 0;
    public string TesseractPath { get; set; } = "ocr/tesseract.exe";
    public string TessdataPath { get; set; } = "ocr/tessdata";
    public string TesseractLanguage { get; set; } = "eng+chi_sim";
    public int OcrPageSegmentationMode { get; set; } = 6;
    public bool UseNeuralChineseOcr { get; set; } = true;
    public bool DebugSaveCaptures { get; set; }
    public string DisplayScalingMode { get; set; } = "Auto";
    public int GameUiScalePercent { get; set; } = 100;
    public bool EnableLocalGridCalibration { get; set; } = true;

    public string EffectiveEftarkovApiUrl
    {
        get
        {
            var id = GameMode.ToLowerInvariant() switch
            {
                "pve" => "9",
                "season" => "16",
                _ => "10"
            };
            var baseUrl = EftarkovApiUrl.Split('?')[0];
            return $"{baseUrl}?id={id}";
        }
    }

    public static string UserSettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LootPilot", "settings.json");

    public static AppSettings Load()
    {
        var bundledPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        return LoadFromPaths(UserSettingsPath, bundledPath);
    }

    internal static AppSettings LoadFromPaths(params string[] paths)
    {
        foreach (var path in paths
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(path)) continue;
            try
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (settings is null) continue;
                NormalizeLoadedSettings(settings);
                if (settings.TesseractLanguage.Contains("chi_sim", StringComparison.OrdinalIgnoreCase))
                    settings.OcrImageScale = Math.Max(3, settings.OcrImageScale);
                return settings;
            }
            catch (JsonException) { }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return new();
    }

    private static void NormalizeLoadedSettings(AppSettings settings)
    {
        var defaults = new AppSettings();
        settings.GameMode = OrDefault(settings.GameMode, defaults.GameMode);
        settings.Theme = OrDefault(settings.Theme, defaults.Theme);
        settings.RecognitionMode = OrDefault(settings.RecognitionMode, defaults.RecognitionMode);
        settings.Hotkey = OrDefault(settings.Hotkey, defaults.Hotkey);
        settings.InventoryHotkey = OrDefault(settings.InventoryHotkey, defaults.InventoryHotkey);
        settings.ApiUrl = OrDefault(settings.ApiUrl, defaults.ApiUrl);
        settings.EftarkovApiUrl = OrDefault(settings.EftarkovApiUrl, defaults.EftarkovApiUrl);
        settings.CachePath = OrDefault(settings.CachePath, defaults.CachePath);
        settings.CaptureMode = OrDefault(settings.CaptureMode, defaults.CaptureMode);
        settings.TesseractPath = OrDefault(settings.TesseractPath, defaults.TesseractPath);
        settings.TessdataPath = OrDefault(settings.TessdataPath, defaults.TessdataPath);
        settings.TesseractLanguage = OrDefault(
            settings.TesseractLanguage, defaults.TesseractLanguage);
        settings.DisplayScalingMode = OrDefault(
            settings.DisplayScalingMode, defaults.DisplayScalingMode);
        settings.RefreshIntervalMinutes = PositiveOrDefault(
            settings.RefreshIntervalMinutes, defaults.RefreshIntervalMinutes);
        settings.CaptureWidth = PositiveOrDefault(settings.CaptureWidth, defaults.CaptureWidth);
        settings.CaptureHeight = PositiveOrDefault(settings.CaptureHeight, defaults.CaptureHeight);
        settings.OcrImageScale = PositiveOrDefault(settings.OcrImageScale, defaults.OcrImageScale);
        settings.OverlayDurationMs = PositiveOrDefault(
            settings.OverlayDurationMs, defaults.OverlayDurationMs);
        settings.InventoryOverlayDurationMs = PositiveOrDefault(
            settings.InventoryOverlayDurationMs, defaults.InventoryOverlayDurationMs);
        settings.MinimumDisplayPrice = Math.Max(0, settings.MinimumDisplayPrice);
        settings.GameUiScalePercent = Math.Clamp(settings.GameUiScalePercent, 70, 130);
    }

    private static string OrDefault(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value;

    private static int PositiveOrDefault(int value, int fallback) => value > 0 ? value : fallback;

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(UserSettingsPath)!);
        var temp = UserSettingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(this,
                new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, UserSettingsPath, true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    public string ExpandedCachePath => Environment.ExpandEnvironmentVariables(CachePath)
        .Replace('/', Path.DirectorySeparatorChar);

    public string ExpandedModeCachePath
    {
        get
        {
            var legacy = ExpandedCachePath;
            var directory = Path.GetDirectoryName(legacy) ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LootPilot");
            var extension = Path.GetExtension(legacy);
            var stem = Path.GetFileNameWithoutExtension(legacy);
            var mode = GameMode.ToLowerInvariant() switch
            {
                "pve" => "pve",
                "season" => "season",
                _ => "pvp"
            };
            return Path.Combine(directory, $"{stem}-{mode}{extension}");
        }
    }
}
