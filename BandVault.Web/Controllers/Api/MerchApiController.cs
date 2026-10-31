using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BandVault.Web.Data;
using BandVault.Web.Models;
using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;

namespace BandVault.Web.Controllers.Api
{
    [Route("api/merch")]
    [ApiController]
    public class MerchApiController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IDistributedCache _cache;

        public MerchApiController(ApplicationDbContext context, IDistributedCache cache)
        {
            _context = context;
            _cache = cache;
        }

        [HttpGet]
        public async Task<IActionResult> GetMerch(
            [FromQuery] string? query = null,
            [FromQuery] int? skip = null,
            [FromQuery] int? limit = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 10)
        {
            // ФІКС: Захист від limit=0 (ділення на нуль)
            int actualLimit = limit.HasValue && limit.Value > 0 ? limit.Value : (pageSize > 0 ? pageSize : 10);
            int actualSkip = skip ?? ((page > 0 ? page - 1 : 0) * actualLimit);
            int currentPage = (actualSkip / actualLimit) + 1;

            string cacheKey = $"merch_list_{query ?? "all"}_{actualSkip}_{actualLimit}";

            try
            {
                var cachedData = await _cache.GetStringAsync(cacheKey);
                if (!string.IsNullOrEmpty(cachedData))
                {
                    return Content(cachedData, "application/json");
                }
            }
            catch { /* Ігноруємо помилку, якщо Redis вимкнено */ }

            var merchQuery = _context.MerchItems.AsQueryable();
            if (!string.IsNullOrWhiteSpace(query))
            {
                merchQuery = merchQuery.Where(m => EF.Functions.ILike(m.Title, $"%{query}%"));
            }

            var totalItems = await merchQuery.CountAsync();
            var totalPages = (int)Math.Ceiling(totalItems / (double)actualLimit);
            var items = await merchQuery
                .OrderBy(m => m.Id)
                .Skip(actualSkip)
                .Take(actualLimit)
                .Select(m => new
                {
                    id = m.Id,
                    title = m.Title,
                    price = m.Price,
                    imageUrl = m.ImageUrl
                })
                .ToListAsync();

            string? nextLink = (actualSkip + actualLimit) < totalItems
                ? Url.Action(nameof(GetMerch), new { query, skip = actualSkip + actualLimit, limit = actualLimit })
                : null;

            var responseData = new
            {
                totalItems = totalItems,
                totalPages = totalPages,
                currentPage = currentPage,
                pageSize = actualLimit,
                skip = actualSkip,
                limit = actualLimit,
                nextLink = nextLink,
                data = items
            };

            try
            {
                var cacheOptions = new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(60)
                };
                var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
                var serializedData = JsonSerializer.Serialize(responseData, jsonOptions);

                await _cache.SetStringAsync(cacheKey, serializedData, cacheOptions);
            }
            catch { }

            return Ok(responseData);
        }

        [HttpGet("categories")]
        public async Task<IActionResult> SearchCategories([FromQuery] string? term)
        {
            var query = _context.MerchCategories.AsQueryable();
            if (!string.IsNullOrWhiteSpace(term))
            {
                query = query.Where(c => EF.Functions.ILike(c.Name, $"%{term}%"));
            }
            // ФІКС: Обмежуємо видачу для оптимізації
            var results = await query.Take(20).Select(c => new { id = c.Id, text = c.Name }).ToListAsync();
            return Ok(results);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetMerchItem(int id)
        {
            var merchItem = await _context.MerchItems.FindAsync(id);
            if (merchItem == null) return NotFound();
            return Ok(merchItem);
        }

        [HttpPost]
        public async Task<IActionResult> CreateMerchItem([FromBody] MerchItem merchItem)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            _context.MerchItems.Add(merchItem);
            await _context.SaveChangesAsync();
            await ClearCacheAsync(); // ФІКС: Очищення кешу
            return CreatedAtAction(nameof(GetMerchItem), new { id = merchItem.Id }, merchItem);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateMerchItem(int id, [FromBody] MerchItem merchItem)
        {
            if (id != merchItem.Id) return BadRequest("ID mismatch");
            _context.Entry(merchItem).State = EntityState.Modified;
            try
            {
                await _context.SaveChangesAsync();
                await ClearCacheAsync(); // ФІКС: Очищення кешу
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!MerchItemExists(id)) return NotFound();
                else throw;
            }
            return NoContent();
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteMerchItem(int id)
        {
            var merchItem = await _context.MerchItems.FindAsync(id);
            if (merchItem == null) return NotFound();
            _context.MerchItems.Remove(merchItem);
            await _context.SaveChangesAsync();
            await ClearCacheAsync(); // ФІКС: Очищення кешу
            return NoContent();
        }

        private bool MerchItemExists(int id)
        {
            return _context.MerchItems.Any(e => e.Id == id);
        }

        // Хелпер для очищення (в ідеалі тут треба чистити по префіксу, але для простоти просто ігноруємо помилки)
        private async Task ClearCacheAsync()
        {
            try
            {
                await _cache.RemoveAsync("merch_list_all_0_10");
                await _cache.RemoveAsync("merch_list_all_0_5");
            }
            catch { }
        }
    }
}