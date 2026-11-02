using BandVault.Web.Services;
using Microsoft.AspNetCore.SignalR;

namespace BandVault.Web.Hubs
{
    // C11: SignalR-канал для залишків мерчу (сервер пушить "StockUpdated" через StockNotifier)
    public class StockHub : Hub
    {
        private readonly StockNotifier _stock;

        public StockHub(StockNotifier stock)
        {
            _stock = stock;
        }

        // Клієнт одразу після підключення отримує поточний знімок
        public Task<StockSnapshot> GetSnapshot() => _stock.GetAsync();
    }
}
