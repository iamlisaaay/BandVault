using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BandVault.Web.Data;
using BandVault.Web.Models;

namespace BandVault.Web.Controllers.Api
{
    [Route("api/merch")]
    [ApiController]
    public class MerchApiController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public MerchApiController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: api/merch
        [HttpGet]
        public async Task<IActionResult> GetMerch(
            [FromQuery] string? query = null,
            [FromQuery] int? skip = null,
            [FromQuery] int? limit = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 10)
        {
            // Пріоритет віддається skip/limit. Якщо їх немає - використовуються page/pageSize
            int actualLimit = limit ?? (pageSize < 1 ? 10 : pageSize);
            int actualSkip = skip ?? ((page < 1 ? 0 : page - 1) * actualLimit);
            int currentPage = (actualSkip / actualLimit) + 1;

            var merchQuery = _context.MerchItems.AsQueryable();

            if (!string.IsNullOrWhiteSpace(query))
            {
                merchQuery = merchQuery.Where(m => EF.Functions.ILike(m.Title, $"%{query}%"));
            }

            var totalItems = await merchQuery.CountAsync();
            var totalPages = (int)Math.Ceiling(totalItems / (double)actualLimit);

            var items = await merchQuery
                .OrderBy(m => m.Id) // ДОДАНО СОРТУВАННЯ ДЛЯ ПАГІНАЦІЇ
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

            return Ok(new
            {
                TotalItems = totalItems,
                TotalPages = totalPages,
                CurrentPage = currentPage,
                PageSize = actualLimit,
                Skip = actualSkip,
                Limit = actualLimit,
                NextLink = nextLink,
                Data = items
            });
        }

        // Ендпоінт для задачі F2 (Autocomplete категорій у формах)
        [HttpGet("categories")]
        public async Task<IActionResult> SearchCategories([FromQuery] string? term)
        {
            var query = _context.MerchCategories.AsQueryable();
            if (!string.IsNullOrWhiteSpace(term))
            {
                query = query.Where(c => EF.Functions.ILike(c.Name, $"%{term}%"));
            }

            var results = await query
                .Select(c => new { id = c.Id, text = c.Name })
                .ToListAsync();

            return Ok(results);
        }

        // GET: api/merch/5
        [HttpGet("{id}")]
        public async Task<IActionResult> GetMerchItem(int id)
        {
            var merchItem = await _context.MerchItems.FindAsync(id);

            if (merchItem == null) return NotFound();

            return Ok(merchItem);
        }

        // POST: api/merch
        [HttpPost]
        public async Task<IActionResult> CreateMerchItem([FromBody] MerchItem merchItem)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            _context.MerchItems.Add(merchItem);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetMerchItem), new { id = merchItem.Id }, merchItem);
        }

        // PUT: api/merch/5
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

        // DELETE: api/merch/5
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