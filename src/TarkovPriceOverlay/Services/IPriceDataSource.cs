using TarkovPriceOverlay.Models;

namespace TarkovPriceOverlay.Services;

public interface IPriceDataSource
{
    string Name { get; }
    Task<List<ItemPrice>> FetchItemsAsync(CancellationToken ct = default);
}
