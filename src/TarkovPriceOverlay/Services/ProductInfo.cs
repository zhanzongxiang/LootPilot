namespace TarkovPriceOverlay.Services;

internal static class ProductInfo
{
    private static readonly string Version =
        typeof(ProductInfo).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    public static string UserAgent => $"LootPilot/{Version}";
}
