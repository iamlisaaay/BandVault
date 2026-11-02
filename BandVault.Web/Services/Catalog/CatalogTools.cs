using BandVault.Web.Data;
using BandVault.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace BandVault.Web.Services.Catalog
{
    // ============================================
    // Інструменти каталогу фан-клубу - спільні для MCP-сервера (C22) та ШІ-агента (C21)
    // Читання: list_merch, get_merch_item, list_releases, search_catalog
    // Запис:  create_merch_item
    // ============================================
    public class CatalogTools
    {
        private readonly ApplicationDbContext _context;
        private readonly SearchIndexService _search;
        private readonly CatalogChangeNotifier _catalog;

        public CatalogTools(ApplicationDbContext context, SearchIndexService search, CatalogChangeNotifier catalog)
        {
            _context = context;
            _search = search;
            _catalog = catalog;
        }

        public async Task<object> ListMerchAsync(string? query, int limit)
        {
            var q = _context.MerchItems.Include(m => m.Category).AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(query))
            {
                var pattern = $"%{query.Trim()}%";
                q = _context.Database.IsNpgsql()
                    ? q.Where(m => EF.Functions.ILike(m.Title, pattern))
                    : q.Where(m => m.Title.ToLower().Contains(query.Trim().ToLower()));
            }

            var items = await q.OrderBy(m => m.Id).Take(Math.Clamp(limit, 1, 50))
                .Select(m => new
                {
                    id = m.Id,
                    title = m.Title,
                    category = m.Category != null ? m.Category.Name : null,
                    price = m.Price,
                    fanPrice = m.FanPrice,
                    stock = m.StockQuantity ?? 0
                }).ToListAsync();
            return new { total = items.Count, items };
        }

        public async Task<object> GetMerchItemAsync(int id)
        {
            var m = await _context.MerchItems.Include(x => x.Category).AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (m == null) return new { error = $"Товар з id={id} не знайдено" };
            return new
            {
                id = m.Id,
                title = m.Title,
                description = m.Description,
                category = m.Category?.Name,
                price = m.Price,
                fanPrice = m.FanPrice,
                stock = m.StockQuantity ?? 0,
                imageUrl = m.ImageUrl,
                url = $"/Merch/Details/{m.Id}"
            };
        }

        public async Task<object> ListReleasesAsync()
        {
            var releases = await _context.Releases.Include(r => r.Genre).Include(r => r.Tracks).AsNoTracking()
                .OrderByDescending(r => r.ReleaseDate)
                .ToListAsync();
            return new
            {
                total = releases.Count,
                releases = releases.Select(r => new
                {
                    id = r.Id,
                    title = r.Title,
                    genre = r.Genre?.Name,
                    releaseDate = r.ReleaseDate.ToString("yyyy-MM-dd"),
                    tracks = r.Tracks.Select(t => t.Title).ToList(),
                    url = $"/Releases/Details/{r.Id}"
                })
            };
        }

        public async Task<object> SearchCatalogAsync(string query, string? type)
        {
            if (string.IsNullOrWhiteSpace(query)) return new { error = "Порожній запит" };
            var hits = await _search.SearchAsync(query, type, 10);
            return new { query, total = hits.Count, results = hits };
        }

        public async Task<object> CreateMerchItemAsync(string title, string description, decimal price, int stock, string category, decimal? fanPrice)
        {
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(description))
                return new { error = "Назва та опис обов'язкові" };
            if (price <= 0 || price > 100000) return new { error = "Ціна має бути в межах 0.01 - 100000" };
            if (stock < 0) return new { error = "Залишок не може бути від'ємним" };

            var categoryEntity = await _context.MerchCategories.FirstOrDefaultAsync(c => c.Name.ToLower() == category.Trim().ToLower());
            if (categoryEntity == null)
            {
                var available = await _context.MerchCategories.Select(c => c.Name).ToListAsync();
                return new { error = $"Категорію '{category}' не знайдено", availableCategories = available };
            }

            var item = new MerchItem
            {
                Title = title.Trim(),
                Description = description.Trim(),
                Price = price,
                FanPrice = fanPrice,
                StockQuantity = stock,
                CategoryId = categoryEntity.Id
            };
            _context.MerchItems.Add(item);
            await _context.SaveChangesAsync();

            await _catalog.MerchChangedAsync();

            return new { created = true, id = item.Id, title = item.Title, url = $"/Merch/Details/{item.Id}" };
        }
    }
}
