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

        // GET: api/releases?page=1&pageSize=10
        [HttpGet]
        public async Task<IActionResult> GetReleases([FromQuery] int page = 1, [FromQuery] int pageSize = 10)
        {
            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 10;

            var totalItems = await _context.Releases.CountAsync();
            var totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);

           
            var releases = await _context.Releases
                .Include(r => r.Genre)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            string? nextLink = page < totalPages
                ? Url.Action(nameof(GetReleases), new { page = page + 1, pageSize })
                : null;

            return Ok(new
            {
                TotalItems = totalItems,
                TotalPages = totalPages,
                CurrentPage = page,
                PageSize = pageSize,
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