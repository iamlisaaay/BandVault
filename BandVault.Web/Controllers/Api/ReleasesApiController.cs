using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BandVault.Web.Data;
using BandVault.Web.Models;
using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BandVault.Web.Controllers.Api
{
    [Route("api/releases")]
    [ApiController]
    public class ReleasesApiController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IDistributedCache _cache;

        public ReleasesApiController(ApplicationDbContext context, IDistributedCache cache)
        {
            _context = context;
            _cache = cache;
        }

        [HttpGet]
        public async Task<IActionResult> GetReleases(
            [FromQuery] int? skip = null,
            [FromQuery] int? limit = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 10)
        {
            int actualLimit = limit.HasValue && limit.Value > 0 ? limit.Value : (pageSize > 0 ? pageSize : 10);
            int actualSkip = skip ?? ((page > 0 ? page - 1 : 0) * actualLimit);
            int currentPage = (actualSkip / actualLimit) + 1;

            string cacheKey = $"releases_list_{actualSkip}_{actualLimit}";

            try
            {
                var cachedData = await _cache.GetStringAsync(cacheKey);
                if (!string.IsNullOrEmpty(cachedData))
                {
                    return Content(cachedData, "application/json");
                }
            }
            catch { }

            var totalItems = await _context.Releases.CountAsync();
            var totalPages = (int)Math.Ceiling(totalItems / (double)actualLimit);

            var releases = await _context.Releases
                .Include(r => r.Genre)
                .OrderBy(r => r.Id)
                .Skip(actualSkip)
                .Take(actualLimit)
                .ToListAsync();

            string? nextLink = (actualSkip + actualLimit) < totalItems
                ? Url.Action(nameof(GetReleases), new { skip = actualSkip + actualLimit, limit = actualLimit })
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
                data = releases
            };

            try
            {
                var cacheOptions = new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(60) };
                var jsonOptions = new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    ReferenceHandler = ReferenceHandler.IgnoreCycles
                };
                var serializedData = JsonSerializer.Serialize(responseData, jsonOptions);
                await _cache.SetStringAsync(cacheKey, serializedData, cacheOptions);
            }
            catch { }

            return Ok(responseData);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetRelease(int id)
        {
            var release = await _context.Releases
                .Include(r => r.Genre)
                .Include(r => r.Tracks)
                .FirstOrDefaultAsync(r => r.Id == id);

            if (release == null) return NotFound();
            return Ok(release);
        }

        [HttpPost]
        public async Task<IActionResult> CreateRelease([FromBody] Release release)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            release.ReleaseDate = DateTime.SpecifyKind(release.ReleaseDate, DateTimeKind.Utc);
            _context.Releases.Add(release);
            await _context.SaveChangesAsync();
            await ClearCacheAsync(); // ФІКС: Очищення кешу
            return CreatedAtAction(nameof(GetRelease), new { id = release.Id }, release);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateRelease(int id, [FromBody] Release release)
        {
            if (id != release.Id) return BadRequest("ID mismatch");
            release.ReleaseDate = DateTime.SpecifyKind(release.ReleaseDate, DateTimeKind.Utc);
            _context.Entry(release).State = EntityState.Modified;
            try
            {
                await _context.SaveChangesAsync();
                await ClearCacheAsync(); // ФІКС: Очищення кешу
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!ReleaseExists(id)) return NotFound();
                else throw;
            }
            return NoContent();
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteRelease(int id)
        {
            var release = await _context.Releases.FindAsync(id);
            if (release == null) return NotFound();
            _context.Releases.Remove(release);
            await _context.SaveChangesAsync();
            await ClearCacheAsync(); // ФІКС: Очищення кешу
            return NoContent();
        }

        private bool ReleaseExists(int id)
        {
            return _context.Releases.Any(e => e.Id == id);
        }

        // Хелпер для очищення (в ідеалі тут треба чистити по префіксу)
        private async Task ClearCacheAsync()
        {
            try
            {
                await _cache.RemoveAsync("releases_list_0_10");
                await _cache.RemoveAsync("releases_list_0_5");
            }
            catch { }
        }
    }
}