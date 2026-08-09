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
        var userPath = UserSettingsPath;
        var bundledPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        var path = File.Exists(userPath) ? userPath : bundledPath;
        if (!File.Exists(path)) return new();
        var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
        if (settings.TesseractLanguage.Contains("chi_sim", StringComparison.OrdinalIgnoreCase))
            settings.OcrImageScale = Math.Max(3, settings.OcrImageScale);
        return settings;
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(UserSettingsPath)!);
        File.WriteAllText(UserSettingsPath, JsonSerializer.Serialize(this,
            new JsonSerializerOptions { WriteIndented = true }));
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
