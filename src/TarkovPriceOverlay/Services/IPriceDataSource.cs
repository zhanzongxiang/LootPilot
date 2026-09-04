using TarkovPriceOverlay.Models;

namespace TarkovPriceOverlay.Services;

public interface IPriceDataSource
{
    string Name { get; }
    int MinimumExpectedItemCount => 1;
    Task<List<ItemPrice>> FetchItemsAsync(CancellationToken ct = default);
}
