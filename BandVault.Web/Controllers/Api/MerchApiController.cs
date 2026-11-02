using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BandVault.Web.Data;
using BandVault.Web.Models;
using BandVault.Web.Services;

namespace BandVault.Web.Controllers.Api
{
    // C5: REST API мерчу. Відповіді - DTO (не EF-сутності), запис - лише адмін або X-Api-Key
    [Route("api/merch")]
    [ApiController]
    public class MerchApiController : ControllerBase
    {
        private const string CacheArea = CatalogChangeNotifier.MerchCacheArea;
        private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        private readonly ApplicationDbContext _context;
        private readonly ApiCache _cache;
        private readonly CatalogChangeNotifier _catalog;

        public MerchApiController(ApplicationDbContext context, ApiCache cache, CatalogChangeNotifier catalog)
        {
            _context = context;
            _cache = cache;
            _catalog = catalog;
        }

        public record MerchDto(int Id, string Title, string Description, decimal Price, decimal? FanPrice,
            int StockQuantity, string? ImageUrl, int CategoryId, string? Category);

        // Вхідна модель: id береться з URL, а не з тіла; imageUrl необов'язковий (не затирається при PUT)
        public class MerchInput
        {
            public int? Id { get; set; }
            [Required(ErrorMessage = "Назва обов'язкова"), MaxLength(200)]
            public string Title { get; set; } = string.Empty;
            [Required(ErrorMessage = "Опис обов'язковий")]
            public string Description { get; set; } = string.Empty;
            [Range(0.01, 100000, ErrorMessage = "Ціна має бути більше 0")]
            public decimal Price { get; set; }
            [Range(0.01, 100000, ErrorMessage = "Ціна для фанів має бути більше 0")]
            public decimal? FanPrice { get; set; }
            [Range(0, 1000000, ErrorMessage = "Залишок не може бути від'ємним")]
            public int? StockQuantity { get; set; }
            [Url(ErrorMessage = "imageUrl має бути абсолютним URL")]
            public string? ImageUrl { get; set; }
            [Range(1, int.MaxValue, ErrorMessage = "Категорія обов'язкова")]
            public int CategoryId { get; set; }
        }

        private static readonly System.Linq.Expressions.Expression<Func<MerchItem, MerchDto>> ToDto = m =>
            new MerchDto(m.Id, m.Title, m.Description, m.Price, m.FanPrice, m.StockQuantity ?? 0, m.ImageUrl, m.CategoryId,
                m.Category != null ? m.Category.Name : null);

        // B1: skip/limit (або page/pageSize), nextLink/prevLink, limit не більше 100
        [HttpGet]
        public async Task<IActionResult> GetMerch(
            [FromQuery] string? query = null,
            [FromQuery] int? skip = null,
            [FromQuery] int? limit = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 10)
        {
            if (PageRequest.From(skip, limit, page, pageSize) is not PageRequest paging)
                return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { ["skip"] = new[] { "skip і page не можуть бути від'ємними" } }));

            string normalizedQuery = ApiCache.NormalizeQuery(query);
            string cacheKey = $"list:{normalizedQuery}:{paging.Skip}:{paging.Limit}";
            var cached = await _cache.GetAsync(CacheArea, cacheKey);
            if (cached != null) return Content(cached, "application/json");

            var merchQuery = _context.MerchItems.AsQueryable();
            if (normalizedQuery != "all")
            {
                // %, _ і \ з рядка користувача - звичайні символи, а не шаблони LIKE
                var escaped = normalizedQuery.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
                merchQuery = merchQuery.Where(m => EF.Functions.ILike(m.Title, $"%{escaped}%", "\\"));
            }

            var totalItems = await merchQuery.CountAsync();
            var items = await merchQuery
                .OrderBy(m => m.Id)
                .Skip(paging.Skip)
                .Take(paging.Limit)
                .Select(m => new { id = m.Id, title = m.Title, price = m.Price, stockQuantity = m.StockQuantity ?? 0, imageUrl = m.ImageUrl })
                .ToListAsync();

            string? q = normalizedQuery == "all" ? null : query;
            var responseData = new
            {
                totalItems,
                totalPages = paging.TotalPages(totalItems),
                currentPage = paging.CurrentPage,
                pageSize = paging.Limit,
                skip = paging.Skip,
                limit = paging.Limit,
                nextLink = paging.HasNext(totalItems) ? Url.Action(nameof(GetMerch), new { query = q, skip = paging.Skip + paging.Limit, limit = paging.Limit }) : null,
                prevLink = paging.HasPrevious ? Url.Action(nameof(GetMerch), new { query = q, skip = paging.PreviousSkip, limit = paging.Limit }) : null,
                data = items
            };

            var json = JsonSerializer.Serialize(responseData, Json);
            await _cache.SetAsync(CacheArea, cacheKey, json);
            return Content(json, "application/json");
        }

        [HttpGet("categories")]
        public async Task<IActionResult> SearchCategories([FromQuery] string? term)
        {
            var query = _context.MerchCategories.AsQueryable();
            if (!string.IsNullOrWhiteSpace(term))
            {
                var escaped = term.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
                query = query.Where(c => EF.Functions.ILike(c.Name, $"%{escaped}%", "\\"));
            }
            // ФІКС: Обмежуємо видачу для оптимізації
            var results = await query.Take(20).Select(c => new { id = c.Id, text = c.Name }).ToListAsync();
            return Ok(results);
        }

        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetMerchItem(int id)
        {
            var merchItem = await _context.MerchItems.Where(m => m.Id == id).Select(ToDto).FirstOrDefaultAsync();
            if (merchItem == null) return NotFound();
            return Ok(merchItem);
        }

        [HttpPost]
        [Authorize(Policy = "ApiWrite")]
        public async Task<IActionResult> CreateMerchItem([FromBody] MerchInput input)
        {
            if (!await _context.MerchCategories.AnyAsync(c => c.Id == input.CategoryId))
                return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { ["CategoryId"] = new[] { "Категорію не знайдено" } }));

            var merchItem = new MerchItem();
            Apply(merchItem, input);
            merchItem.ImageUrl = input.ImageUrl;
            _context.MerchItems.Add(merchItem);
            await _context.SaveChangesAsync();
            await OnChangedAsync();

            var dto = await _context.MerchItems.Where(m => m.Id == merchItem.Id).Select(ToDto).FirstAsync();
            return CreatedAtAction(nameof(GetMerchItem), new { id = merchItem.Id }, dto);
        }

        [HttpPut("{id:int}")]
        [Authorize(Policy = "ApiWrite")]
        public async Task<IActionResult> UpdateMerchItem(int id, [FromBody] MerchInput input)
        {
            if (input.Id.HasValue && input.Id != id) return BadRequest("ID mismatch");

            var merchItem = await _context.MerchItems.FindAsync(id);
            if (merchItem == null) return NotFound();
            if (!await _context.MerchCategories.AnyAsync(c => c.Id == input.CategoryId))
                return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { ["CategoryId"] = new[] { "Категорію не знайдено" } }));

            Apply(merchItem, input);
            // Фото змінюємо, лише якщо його явно передали
            if (input.ImageUrl != null) merchItem.ImageUrl = input.ImageUrl;

            await _context.SaveChangesAsync();
            await OnChangedAsync();
            return NoContent();
        }

        [HttpDelete("{id:int}")]
        [Authorize(Policy = "ApiWrite")]
        public async Task<IActionResult> DeleteMerchItem(int id)
        {
            var merchItem = await _context.MerchItems.FindAsync(id);
            if (merchItem == null) return NotFound();
            if (await _context.OrderItems.AnyAsync(oi => oi.MerchItemId == id))
                return Conflict(new { error = "Товар є в замовленнях. Зніміть його з продажу (stockQuantity = 0) замість видалення." });

            _context.MerchItems.Remove(merchItem);
            await _context.SaveChangesAsync();
            await OnChangedAsync();
            return NoContent();
        }

        private static void Apply(MerchItem item, MerchInput input)
        {
            item.Title = input.Title.Trim();
            item.Description = input.Description.Trim();
            item.Price = input.Price;
            item.FanPrice = input.FanPrice;
            item.StockQuantity = input.StockQuantity ?? 0;
            item.CategoryId = input.CategoryId;
        }

        // Зміни мерчу мають бачити B9 (кеш), C10 (залишки наживо) та B4 (пошуковий індекс)
        private Task OnChangedAsync() => _catalog.MerchChangedAsync();
    }
}
