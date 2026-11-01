using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using BandVault.Web.Data;
using BandVault.Web.Models;
using BandVault.Web.Services;

namespace BandVault.Web.Controllers
{
    [Authorize]
    public class ReleasesController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _env;
        private readonly IFileService _fileService;

        public ReleasesController(ApplicationDbContext context, IWebHostEnvironment env, IFileService fileService)
        {
            _context = context;
            _env = env;
            _fileService = fileService;
        }

        public async Task<IActionResult> Index()
        {
            var releases = await _context.Releases.Include(r => r.Genre).ToListAsync();
            return View(releases);
        }

        public async Task<IActionResult> Details(int id)
        {
            var release = await _context.Releases
                .Include(r => r.Genre)
                .Include(r => r.Tracks)
                .FirstOrDefaultAsync(r => r.Id == id);

            if (release == null) return NotFound();

            var currentUserEmail = User.Identity?.Name;
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == currentUserEmail);
            ViewBag.IsAdmin = user?.IsAdmin ?? false;

            return View(release);
        }

        public IActionResult Create()
        {
            if (!User.HasClaim("IsAdmin", "true")) return Forbid();

            ViewBag.Genres = new SelectList(_context.Genres, "Id", "Name");
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        // ФІКС: Зробили IFormFile? опціональним, щоб не падала валідація, якщо фото не завантажили
        public async Task<IActionResult> Create(Release release, IFormFile? coverImage)
        {
            if (!User.HasClaim("IsAdmin", "true")) return Forbid();

            if (ModelState.IsValid)
            {
                // C19: Використання Azure Blob Storage
                if (coverImage != null && coverImage.Length > 0)
                {
                    release.CoverImageUrl = await _fileService.UploadFileAsync(coverImage, "releases");
                }

                release.ReleaseDate = DateTime.SpecifyKind(release.ReleaseDate, DateTimeKind.Utc);

                _context.Releases.Add(release);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            ViewBag.Genres = new SelectList(_context.Genres, "Id", "Name", release.GenreId);
            return View(release);
        }

        public async Task<IActionResult> Edit(int? id)
        {
            if (!User.HasClaim("IsAdmin", "true")) return Forbid();

            if (id == null) return NotFound();

            var release = await _context.Releases.FindAsync(id);
            if (release == null) return NotFound();

            ViewBag.Genres = new SelectList(_context.Genres, "Id", "Name", release.GenreId);
            return View(release);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, Release release, IFormFile? coverImage)
        {
            if (!User.HasClaim("IsAdmin", "true")) return Forbid();

            if (id != release.Id) return NotFound();

            if (ModelState.IsValid)
            {
                try
                {
                    var existingRelease = await _context.Releases.FindAsync(id);
                    if (existingRelease == null) return NotFound();

                    existingRelease.Title = release.Title;
                    existingRelease.Description = release.Description;
                    existingRelease.GenreId = release.GenreId;
                    existingRelease.ReleaseDate = DateTime.SpecifyKind(release.ReleaseDate, DateTimeKind.Utc);

                    // C19: Використання Azure Blob Storage
                    if (coverImage != null && coverImage.Length > 0)
                    {
                        if (!string.IsNullOrEmpty(existingRelease.CoverImageUrl))
                        {
                            await _fileService.DeleteFileAsync(existingRelease.CoverImageUrl);
                        }
                        existingRelease.CoverImageUrl = await _fileService.UploadFileAsync(coverImage, "releases");
                    }

                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!_context.Releases.Any(e => e.Id == release.Id)) return NotFound();
                    else throw;
                }
                return RedirectToAction(nameof(Index));
            }
            ViewBag.Genres = new SelectList(_context.Genres, "Id", "Name", release.GenreId);
            return View(release);
        }

        public async Task<IActionResult> Delete(int? id)
        {
            if (!User.HasClaim("IsAdmin", "true")) return Forbid();

            if (id == null) return NotFound();

            var release = await _context.Releases
                .Include(r => r.Genre)
                .FirstOrDefaultAsync(m => m.Id == id);

            if (release == null) return NotFound();

            return View(release);
        }

        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            if (!User.HasClaim("IsAdmin", "true")) return Forbid();

            var release = await _context.Releases.FindAsync(id);
            if (release != null)
            {
                if (!string.IsNullOrEmpty(release.CoverImageUrl))
                {
                    await _fileService.DeleteFileAsync(release.CoverImageUrl);
                }

                _context.Releases.Remove(release);
                await _context.SaveChangesAsync();
            }
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        public async Task<IActionResult> AddTrack(int releaseId, string title, IFormFile audioFile)
        {
            if (!User.HasClaim("IsAdmin", "true")) return Forbid();

            if (audioFile != null && audioFile.Length > 0)
            {
                // C19: Використання Azure Blob Storage для аудіо
                string fileUrl = await _fileService.UploadFileAsync(audioFile, "audio");

                var track = new Track
                {
                    ReleaseId = releaseId,
                    Title = title,
                    AudioFileUrl = fileUrl,
                    DurationSeconds = 0,
                    PlayCount = 0,
                    RequiredTierLevel = 0
                };

                _context.Tracks.Add(track);
                await _context.SaveChangesAsync();
            }
            return RedirectToAction(nameof(Details), new { id = releaseId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteTrack(int trackId, int releaseId)
        {
            if (!User.HasClaim("IsAdmin", "true")) return Forbid();

            var track = await _context.Tracks.FindAsync(trackId);
            if (track != null)
            {
                // C19: Видалення файлу з Azure
                if (!string.IsNullOrEmpty(track.AudioFileUrl))
                {
                    await _fileService.DeleteFileAsync(track.AudioFileUrl);
                }

                _context.Tracks.Remove(track);
                await _context.SaveChangesAsync();
            }
            return RedirectToAction(nameof(Details), new { id = releaseId });
        }
    }
}