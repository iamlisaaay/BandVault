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
    // C5: REST API релізів. Відповіді - DTO, запис - лише адмін або X-Api-Key
    [Route("api/releases")]
    [ApiController]
    public class ReleasesApiController : ControllerBase
    {
        private const string CacheArea = CatalogChangeNotifier.ReleasesCacheArea;
        private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        private readonly ApplicationDbContext _context;
        private readonly ApiCache _cache;
        private readonly CatalogChangeNotifier _catalog;

        public ReleasesApiController(ApplicationDbContext context, ApiCache cache, CatalogChangeNotifier catalog)
        {
            _context = context;
            _cache = cache;
            _catalog = catalog;
        }

        public record TrackDto(int Id, string Title, int DurationSeconds, int RequiredTierLevel);

        public record ReleaseDto(int Id, string Title, string Description, DateTime ReleaseDate, string? CoverImageUrl,
            int GenreId, string? Genre, int TrackCount);

        public record ReleaseDetailsDto(int Id, string Title, string Description, DateTime ReleaseDate, string? CoverImageUrl,
            int GenreId, string? Genre, IEnumerable<TrackDto> Tracks);

        public class ReleaseInput
        {
            public int? Id { get; set; }
            [Required(ErrorMessage = "Назва обов'язкова"), MaxLength(200)]
            public string Title { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            [Required(ErrorMessage = "Дата релізу обов'язкова")]
            public DateTime? ReleaseDate { get; set; }
            [Url(ErrorMessage = "coverImageUrl має бути абсолютним URL")]
            public string? CoverImageUrl { get; set; }
            [Range(1, int.MaxValue, ErrorMessage = "Жанр обов'язковий")]
            public int GenreId { get; set; }
        }

        [HttpGet]
        public async Task<IActionResult> GetReleases(
            [FromQuery] int? skip = null,
            [FromQuery] int? limit = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 10)
        {
            if (PageRequest.From(skip, limit, page, pageSize) is not PageRequest paging)
                return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { ["skip"] = new[] { "skip і page не можуть бути від'ємними" } }));

            string cacheKey = $"list:{paging.Skip}:{paging.Limit}";
            var cached = await _cache.GetAsync(CacheArea, cacheKey);
            if (cached != null) return Content(cached, "application/json");

            var totalItems = await _context.Releases.CountAsync();
            var releases = await _context.Releases
                .OrderBy(r => r.Id)
                .Skip(paging.Skip)
                .Take(paging.Limit)
                .Select(r => new ReleaseDto(r.Id, r.Title, r.Description, r.ReleaseDate, r.CoverImageUrl, r.GenreId,
                    r.Genre != null ? r.Genre.Name : null, r.Tracks.Count))
                .ToListAsync();

            var responseData = new
            {
                totalItems,
                totalPages = paging.TotalPages(totalItems),
                currentPage = paging.CurrentPage,
                pageSize = paging.Limit,
                skip = paging.Skip,
                limit = paging.Limit,
                nextLink = paging.HasNext(totalItems) ? Url.Action(nameof(GetReleases), new { skip = paging.Skip + paging.Limit, limit = paging.Limit }) : null,
                prevLink = paging.HasPrevious ? Url.Action(nameof(GetReleases), new { skip = paging.PreviousSkip, limit = paging.Limit }) : null,
                data = releases
            };

            var json = JsonSerializer.Serialize(responseData, Json);
            await _cache.SetAsync(CacheArea, cacheKey, json);
            return Content(json, "application/json");
        }

        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetRelease(int id)
        {
            var release = await _context.Releases
                .Include(r => r.Genre)
                .Include(r => r.Tracks)
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == id);

            if (release == null) return NotFound();
            // URL аудіо не віддаємо: закриті демо (B6) доступні лише через сайт з перевіркою рівня
            return Ok(new ReleaseDetailsDto(release.Id, release.Title, release.Description, release.ReleaseDate, release.CoverImageUrl,
                release.GenreId, release.Genre?.Name,
                release.Tracks.OrderBy(t => t.Id).Select(t => new TrackDto(t.Id, t.Title, t.DurationSeconds, t.RequiredTierLevel))));
        }

        [HttpPost]
        [Authorize(Policy = "ApiWrite")]
        public async Task<IActionResult> CreateRelease([FromBody] ReleaseInput input)
        {
            if (!await _context.Genres.AnyAsync(g => g.Id == input.GenreId))
                return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { ["GenreId"] = new[] { "Жанр не знайдено" } }));

            var release = new Release { CoverImageUrl = input.CoverImageUrl };
            Apply(release, input);
            _context.Releases.Add(release);
            await _context.SaveChangesAsync();
            await _catalog.ReleasesChangedAsync();

            return CreatedAtAction(nameof(GetRelease), new { id = release.Id },
                new ReleaseDto(release.Id, release.Title, release.Description, release.ReleaseDate, release.CoverImageUrl, release.GenreId, null, 0));
        }

        [HttpPut("{id:int}")]
        [Authorize(Policy = "ApiWrite")]
        public async Task<IActionResult> UpdateRelease(int id, [FromBody] ReleaseInput input)
        {
            if (input.Id.HasValue && input.Id != id) return BadRequest("ID mismatch");

            var release = await _context.Releases.FindAsync(id);
            if (release == null) return NotFound();
            if (!await _context.Genres.AnyAsync(g => g.Id == input.GenreId))
                return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { ["GenreId"] = new[] { "Жанр не знайдено" } }));

            Apply(release, input);
            if (input.CoverImageUrl != null) release.CoverImageUrl = input.CoverImageUrl;

            await _context.SaveChangesAsync();
            await _catalog.ReleasesChangedAsync();
            return NoContent();
        }

        [HttpDelete("{id:int}")]
        [Authorize(Policy = "ApiWrite")]
        public async Task<IActionResult> DeleteRelease(int id)
        {
            var release = await _context.Releases.FindAsync(id);
            if (release == null) return NotFound();
            _context.Releases.Remove(release);
            await _context.SaveChangesAsync();
            await _catalog.ReleasesChangedAsync();
            return NoContent();
        }

        private static void Apply(Release release, ReleaseInput input)
        {
            release.Title = input.Title.Trim();
            release.Description = input.Description?.Trim() ?? "";
            release.ReleaseDate = DateTime.SpecifyKind(input.ReleaseDate!.Value, DateTimeKind.Utc);
            release.GenreId = input.GenreId;
        }
    }
}
