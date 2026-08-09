namespace TarkovPriceOverlay.Models;

public sealed record ScanHistoryEntry(
    DateTimeOffset ScannedAt,
    string GameMode,
    int ItemCount,
    long FleaTotal,
    long TraderTotal)
{
    public string TimeText => ScannedAt.ToLocalTime().ToString("MM-dd HH:mm");
    public string Summary => $"{GameMode} · {ItemCount} 件";
    public string FleaText => $"跳蚤 {Format(FleaTotal)}";
    public string TraderText => $"商人 {Format(TraderTotal)}";

    private static string Format(long value) => $"{value / 10_000d:0.##}万 ₽";
}
