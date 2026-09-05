using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using TarkovPriceOverlay.Models;
using TarkovPriceOverlay.Configuration;

namespace TarkovPriceOverlay.Services;

public sealed class TarkovDevClient : IPriceDataSource
{
    private readonly HttpClient _http = CreateHttpClient();
    private readonly string _url;
    private readonly Func<bool>? _isAvailable;

    public TarkovDevClient(string url) => _url = url;
    public TarkovDevClient(AppSettings settings)
    {
        _url = settings.ApiUrl;
        _isAvailable = () => !settings.GameMode.Equals("Season", StringComparison.OrdinalIgnoreCase);
    }
    public string Name => "tarkov.dev";
    public int MinimumExpectedItemCount => 1_000;

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(ProductInfo.UserAgent);
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        return client;
    }

    public async Task<List<ItemPrice>> FetchItemsAsync(CancellationToken ct = default)
    {
        if (_isAvailable?.Invoke() == false)
            throw new InvalidOperationException("tarkov.dev 暂不提供独立赛季服经济数据。");
        const string query = """
        query Items {
          items {
            id name shortName width height avg24hPrice iconLink image8xLink
            sellFor { price vendor { name } }
            usedInTasks { id }
            hideoutModules { id }
          }
        }
        """;
        using var response = await _http.PostAsJsonAsync(_url, new { query }, ct);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<GraphResponse>(cancellationToken: ct)
            ?? throw new InvalidDataException("tarkov.dev returned an empty response.");
        if (payload.Errors?.Count > 0)
            throw new InvalidDataException(payload.Errors[0].Message);

        return payload.Data?.Items.Select(x =>
        {
            var bestTrader = x.SellFor?
                .Where(s => !string.Equals(s.Vendor.Name, "Flea Market", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(s => s.Price).FirstOrDefault();
            return new ItemPrice(x.Id, x.Name, x.ShortName, x.Width, x.Height,
                x.Avg24hPrice, bestTrader?.Price, bestTrader?.Vendor.Name,
                x.UsedInTasks?.Count > 0, x.HideoutModules?.Count > 0)
            {
                IconUrl = x.Image8xLink ?? x.IconLink ?? FallbackIconUrl(x.Id)
            };
        }).ToList() ?? [];
    }

    private static string FallbackIconUrl(string id) =>
        $"https://assets.tarkov.dev/{Uri.EscapeDataString(id)}-icon.jpg";

    private sealed class GraphResponse
    {
        [JsonPropertyName("data")] public GraphData? Data { get; set; }
        [JsonPropertyName("errors")] public List<GraphError>? Errors { get; set; }
    }
    private sealed class GraphData { [JsonPropertyName("items")] public List<ApiItem> Items { get; set; } = []; }
    private sealed class GraphError { [JsonPropertyName("message")] public string Message { get; set; } = ""; }
    private sealed class ApiItem
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("shortName")] public string ShortName { get; set; } = "";
        [JsonPropertyName("iconLink")] public string? IconLink { get; set; }
        [JsonPropertyName("image8xLink")] public string? Image8xLink { get; set; }
        [JsonPropertyName("width")] public int Width { get; set; }
        [JsonPropertyName("height")] public int Height { get; set; }
        [JsonPropertyName("avg24hPrice")] public int? Avg24hPrice { get; set; }
        [JsonPropertyName("sellFor")] public List<ApiSell>? SellFor { get; set; }
        [JsonPropertyName("usedInTasks")] public List<ApiRef>? UsedInTasks { get; set; }
        [JsonPropertyName("hideoutModules")] public List<ApiRef>? HideoutModules { get; set; }
    }
    private sealed class ApiSell
    {
        [JsonPropertyName("price")] public int Price { get; set; }
        [JsonPropertyName("vendor")] public ApiVendor Vendor { get; set; } = new();
    }
    private sealed class ApiVendor { [JsonPropertyName("name")] public string Name { get; set; } = ""; }
    private sealed class ApiRef { [JsonPropertyName("id")] public string Id { get; set; } = ""; }
}
