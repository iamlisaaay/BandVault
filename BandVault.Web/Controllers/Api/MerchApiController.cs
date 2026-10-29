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
            int actualLimit = limit ?? (pageSize < 1 ? 10 : pageSize);
            int actualSkip = skip ?? ((page < 1 ? 0 : page - 1) * actualLimit);
            int currentPage = (actualSkip / actualLimit) + 1;

            string cacheKey = $"merch_list_{query ?? "all"}_{actualSkip}_{actualLimit}";

            // 1. СПРОБА ПРОЧИТАТИ З КЕШУ
            try
            {
                var cachedData = await _cache.GetStringAsync(cacheKey);
                if (!string.IsNullOrEmpty(cachedData))
                {
                    // Повертаємо кеш з правильним Content-Type
                    return Content(cachedData, "application/json");
                }
            }
            catch { /* Якщо Redis лежить, ігноруємо помилку і йдемо в БД */ }

            // 2. БД
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
                totalItems = totalItems, // Примусово CamelCase для Postman та UI
                totalPages = totalPages,
                currentPage = currentPage,
                pageSize = actualLimit,
                skip = actualSkip,
                limit = actualLimit,
                nextLink = nextLink,
                data = items
            };

            // 3. ЗАПИС У КЕШ
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
            catch { /* Ігноруємо, якщо Redis недоступний */ }

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
            var results = await query.Select(c => new { id = c.Id, text = c.Name }).ToListAsync();
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
            return NoContent();
        }

        private bool MerchItemExists(int id)
        {
            return _context.MerchItems.Any(e => e.Id == id);
        }
    }
}