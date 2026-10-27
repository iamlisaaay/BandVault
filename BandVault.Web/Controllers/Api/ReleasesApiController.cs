using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BandVault.Web.Data;
using BandVault.Web.Models;

namespace BandVault.Web.Controllers.Api
{
    [Route("api/releases")]
    [ApiController]
    public class ReleasesApiController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public ReleasesApiController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: api/releases
        [HttpGet]
        public async Task<IActionResult> GetReleases(
            [FromQuery] int? skip = null,
            [FromQuery] int? limit = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 10)
        {
            int actualLimit = limit ?? (pageSize < 1 ? 10 : pageSize);
            int actualSkip = skip ?? ((page < 1 ? 0 : page - 1) * actualLimit);
            int currentPage = (actualSkip / actualLimit) + 1;

            var totalItems = await _context.Releases.CountAsync();
            var totalPages = (int)Math.Ceiling(totalItems / (double)actualLimit);

            var releases = await _context.Releases
                .Include(r => r.Genre)
                .Skip(actualSkip)
                .Take(actualLimit)
                .ToListAsync();

            string? nextLink = (actualSkip + actualLimit) < totalItems
                ? Url.Action(nameof(GetReleases), new { skip = actualSkip + actualLimit, limit = actualLimit })
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
                Data = releases
            });
        }

        // GET: api/releases/5
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

        // POST: api/releases
        [HttpPost]
        public async Task<IActionResult> CreateRelease([FromBody] Release release)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            release.ReleaseDate = DateTime.SpecifyKind(release.ReleaseDate, DateTimeKind.Utc);

            _context.Releases.Add(release);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetRelease), new { id = release.Id }, release);
        }

        // PUT: api/releases/5
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateRelease(int id, [FromBody] Release release)
        {
            if (id != release.Id) return BadRequest("ID mismatch");

            release.ReleaseDate = DateTime.SpecifyKind(release.ReleaseDate, DateTimeKind.Utc);
            _context.Entry(release).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!ReleaseExists(id)) return NotFound();
                else throw;
            }

            return NoContent();
        }

        // DELETE: api/releases/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteRelease(int id)
        {
            var release = await _context.Releases.FindAsync(id);
            if (release == null) return NotFound();

            _context.Releases.Remove(release);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        private bool ReleaseExists(int id)
        {
            return _context.Releases.Any(e => e.Id == id);
        }
    }
}