using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using TarkovPriceOverlay.Models;
using TarkovPriceOverlay.Configuration;

namespace TarkovPriceOverlay.Services;

public sealed class EftarkovClient : IPriceDataSource
{
    private HttpClient _http = null!;
    private readonly Func<string> _urlProvider;
    private readonly Func<string>? _modeProvider;
    public string Name => _modeProvider is null ? "eftarkov.com" : $"eftarkov.com ({_modeProvider()})";
    public int MinimumExpectedItemCount => 1_000;

    public EftarkovClient(string url)
    {
        _urlProvider = () => url;
        InitializeClient();
    }

    public EftarkovClient(AppSettings settings)
    {
        _urlProvider = () => settings.EffectiveEftarkovApiUrl;
        _modeProvider = () => settings.GameMode;
        InitializeClient();
    }

    private void InitializeClient()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0");
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(ProductInfo.UserAgent);
        _http.DefaultRequestHeaders.Referrer = new Uri("https://www.eftarkov.com/news/web_209.html");
    }

    public async Task<List<ItemPrice>> FetchItemsAsync(CancellationToken ct = default)
    {
        using var response = await _http.GetAsync(_urlProvider(), HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
            throw new HttpRequestException(
                $"eftarkov 拒绝了本次请求（{(int)response.StatusCode}），程序将在下个刷新周期再试。",
                null, response.StatusCode);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<EftResponse>(cancellationToken: ct)
            ?? throw new InvalidDataException("eftarkov 返回了空响应。");
        var items = payload.RawApiData?.Data?.Items
            ?? throw new InvalidDataException("eftarkov 响应中没有物品数据。");

        return items.Select(x =>
        {
            var bestTrader = x.TraderPrices?
                .OrderByDescending(p => p.PriceRub ?? p.Price ?? 0)
                .FirstOrDefault();
            return new ItemPrice(
                x.Id, x.Name, x.ShortName, Math.Max(1, x.Width), Math.Max(1, x.Height),
                x.LastLowPrice, bestTrader?.PriceRub ?? bestTrader?.Price,
                bestTrader?.Trader?.Name, false, false)
            {
                IconUrl = x.Image8xLink ?? x.IconLink ?? FallbackIconUrl(x.Id)
            };
        }).ToList();
    }

    private static string FallbackIconUrl(string id) =>
        $"https://assets.tarkov.dev/{Uri.EscapeDataString(id)}-icon.jpg";

    private sealed class EftResponse
    {
        [JsonPropertyName("raw_api_data")] public RawApiData? RawApiData { get; set; }
    }
    private sealed class RawApiData
    {
        [JsonPropertyName("data")] public EftData? Data { get; set; }
    }
    private sealed class EftData
    {
        [JsonPropertyName("items")] public List<EftItem> Items { get; set; } = [];
    }
    private sealed class EftItem
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("shortName")] public string ShortName { get; set; } = "";
        [JsonPropertyName("iconLink")] public string? IconLink { get; set; }
        [JsonPropertyName("image8xLink")] public string? Image8xLink { get; set; }
        [JsonPropertyName("width")] public int Width { get; set; }
        [JsonPropertyName("height")] public int Height { get; set; }
        [JsonPropertyName("lastLowPrice")] public int? LastLowPrice { get; set; }
        [JsonPropertyName("traderPrices")] public List<EftTraderPrice>? TraderPrices { get; set; }
    }
    private sealed class EftTraderPrice
    {
        [JsonPropertyName("price")] public int? Price { get; set; }
        [JsonPropertyName("priceRUB")] public int? PriceRub { get; set; }
        [JsonPropertyName("trader")] public EftTrader? Trader { get; set; }
    }
    private sealed class EftTrader
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";
    }
}
