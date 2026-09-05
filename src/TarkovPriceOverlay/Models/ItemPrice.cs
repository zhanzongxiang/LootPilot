namespace TarkovPriceOverlay.Models;

public sealed record ItemPrice(
    string Id,
    string Name,
    string ShortName,
    int Width,
    int Height,
    int? FleaPrice,
    int? TraderPrice,
    string? TraderName,
    bool UsedInTasks,
    bool UsedInHideout)
{
    public string? IconUrl { get; init; }
    public int Slots => Math.Max(1, Width * Height);
    public int? BestPrice => Math.Max(FleaPrice ?? 0, TraderPrice ?? 0) is var value && value > 0 ? value : null;
    public int? PricePerSlot => BestPrice is int value ? value / Slots : null;
}

public sealed record CacheEnvelope(
    DateTimeOffset UpdatedAt,
    List<ItemPrice> Items,
    string Source = "未知");

public sealed record RecognitionResult(
    string RawText,
    ItemPrice? Item,
    double Confidence,
    string SourceRegion = "");
