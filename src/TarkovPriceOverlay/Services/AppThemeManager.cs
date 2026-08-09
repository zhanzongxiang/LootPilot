using System.Windows;
using System.Windows.Media;
using MediaColor = System.Windows.Media.Color;

namespace TarkovPriceOverlay.Services;

public sealed record AppThemePalette(
    string Name,
    MediaColor Canvas,
    MediaColor Surface,
    MediaColor TintSurface,
    MediaColor SoftSurface,
    MediaColor Accent,
    MediaColor AccentSoft,
    MediaColor Button,
    MediaColor Ink,
    MediaColor Muted,
    MediaColor OnAccent);

public static class AppThemeManager
{
    public static IReadOnlyList<AppThemePalette> Themes { get; } =
    [
        Theme("雾紫", "#F5F7FB", "#FFFFFF", "#F9F8FF", "#F9FAFD", "#4C63DC", "#EFECFF", "#EEF1F7", "#1D2432", "#687182", "#FFFFFF"),
        Theme("极夜蓝", "#141822", "#1D2230", "#20263A", "#1B202F", "#7C8CFF", "#2D3558", "#2A3144", "#F2F5FF", "#AAB2C8", "#FFFFFF"),
        Theme("薄荷灰", "#F2F7F5", "#FFFFFF", "#F1FAF6", "#F4F8F6", "#3BAA83", "#DFF5EC", "#E5F0EB", "#1F302A", "#6B7F77", "#FFFFFF"),
        Theme("暖砂", "#F8F5EF", "#FFFFFF", "#FCF7EE", "#F8F4EC", "#C9824D", "#F7E7D8", "#F1E8DC", "#392C24", "#8B7667", "#FFFFFF"),
        Theme("黑白极简", "#F3F3F3", "#FFFFFF", "#F8F8F8", "#F6F6F6", "#202020", "#E6E6E6", "#EBEBEB", "#181818", "#707070", "#FFFFFF")
    ];

    public static AppThemePalette Current { get; private set; } = Themes[1];

    public static void Apply(string? name)
    {
        Current = Themes.FirstOrDefault(x => x.Name == name) ?? Themes[1];
        var resources = System.Windows.Application.Current.Resources;
        Set(resources, "CanvasBrush", Current.Canvas);
        Set(resources, "HeaderBrush", Current.TintSurface);
        Set(resources, "SurfaceBrush", Current.Surface);
        Set(resources, "TintSurfaceBrush", Current.TintSurface);
        Set(resources, "SoftSurfaceBrush", Current.SoftSurface);
        Set(resources, "AccentBrush", Current.Accent);
        Set(resources, "AccentSoftBrush", Current.AccentSoft);
        Set(resources, "ButtonBrush", Current.Button);
        Set(resources, "TextBrush", Current.Ink);
        Set(resources, "MutedBrush", Current.Muted);
        Set(resources, "OnAccentBrush", Current.OnAccent);
        Set(resources, "BorderBrush", Blend(Current.Muted, Current.Surface, 0.72));
        Set(resources, "InputBrush", Current.SoftSurface);
    }

    private static void Set(ResourceDictionary resources, string key, MediaColor color) =>
        resources[key] = new SolidColorBrush(color);

    private static AppThemePalette Theme(string name, params string[] colors) => new(
        name, Parse(colors[0]), Parse(colors[1]), Parse(colors[2]), Parse(colors[3]), Parse(colors[4]),
        Parse(colors[5]), Parse(colors[6]), Parse(colors[7]), Parse(colors[8]), Parse(colors[9]));

    private static MediaColor Parse(string value) =>
        (MediaColor)System.Windows.Media.ColorConverter.ConvertFromString(value);

    private static MediaColor Blend(MediaColor foreground, MediaColor background, double backgroundWeight)
    {
        var foregroundWeight = 1d - backgroundWeight;
        return MediaColor.FromRgb(
            (byte)(foreground.R * foregroundWeight + background.R * backgroundWeight),
            (byte)(foreground.G * foregroundWeight + background.G * backgroundWeight),
            (byte)(foreground.B * foregroundWeight + background.B * backgroundWeight));
    }
}
