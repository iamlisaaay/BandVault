namespace BandVault.Web.Services
{
    // Єдина точка "каталог змінився": кеш API (B9), залишки наживо (C10), пошуковий індекс (B4).
    // Викликається з адмінки, REST API, MCP/асистента та оформлення замовлень.
    public class CatalogChangeNotifier
    {
        public const string MerchCacheArea = "merch";
        public const string ReleasesCacheArea = "releases";

        private readonly ApiCache _cache;
        private readonly StockNotifier _stock;
        private readonly SearchIndexService _search;

        public CatalogChangeNotifier(ApiCache cache, StockNotifier stock, SearchIndexService search)
        {
            _cache = cache;
            _stock = stock;
            _search = search;
        }

        public async Task MerchChangedAsync()
        {
            await _cache.InvalidateAsync(MerchCacheArea);
            await _stock.RefreshAsync();
            _search.MarkDirty();
        }

        public async Task ReleasesChangedAsync()
        {
            await _cache.InvalidateAsync(ReleasesCacheArea);
            _search.MarkDirty();
        }
    }
}
