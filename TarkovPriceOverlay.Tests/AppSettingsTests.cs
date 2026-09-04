using TarkovPriceOverlay.Configuration;

namespace TarkovPriceOverlay.Tests;

public sealed class AppSettingsTests
{
    [Fact]
    public void CorruptUserSettingsFallBackToBundledSettings()
    {
        using var temp = new TemporaryDirectory();
        var user = System.IO.Path.Combine(temp.Path, "user.json");
        var bundled = System.IO.Path.Combine(temp.Path, "bundled.json");
        File.WriteAllText(user, "{ invalid json");
        File.WriteAllText(bundled, "{\"GameMode\":\"PvE\",\"Hotkey\":\"Alt+E\"}");

        var settings = AppSettings.LoadFromPaths(user, bundled);

        Assert.Equal("PvE", settings.GameMode);
        Assert.Equal("Alt+E", settings.Hotkey);
    }

    [Fact]
    public void SemanticallyInvalidValuesAreReplacedWithSafeDefaults()
    {
        using var temp = new TemporaryDirectory();
        var path = System.IO.Path.Combine(temp.Path, "settings.json");
        File.WriteAllText(path,
            "{\"Theme\":null,\"TesseractLanguage\":null,\"CaptureWidth\":-1,\"OverlayDurationMs\":0}");

        var settings = AppSettings.LoadFromPaths(path);

        Assert.Equal("极夜蓝", settings.Theme);
        Assert.Equal("eng+chi_sim", settings.TesseractLanguage);
        Assert.Equal(900, settings.CaptureWidth);
        Assert.Equal(3500, settings.OverlayDurationMs);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "LootPilotTests", Guid.NewGuid().ToString("N"));

        public TemporaryDirectory() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}
