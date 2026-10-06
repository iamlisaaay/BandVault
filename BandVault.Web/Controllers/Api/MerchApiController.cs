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

        // GET: api/merch?page=1&pageSize=10
        [HttpGet]
        public async Task<IActionResult> GetMerch([FromQuery] int page = 1, [FromQuery] int pageSize = 10)
        {
            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 10;

            var totalItems = await _context.MerchItems.CountAsync();
            var totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);

            var items = await _context.MerchItems
                .Include(m => m.Category)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

        
            string? nextLink = page < totalPages
                ? Url.Action(nameof(GetMerch), new { page = page + 1, pageSize })
                : null;

            return Ok(new
            {
                TotalItems = totalItems,
                TotalPages = totalPages,
                CurrentPage = page,
                PageSize = pageSize,
                NextLink = nextLink,
                Data = items
            });
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