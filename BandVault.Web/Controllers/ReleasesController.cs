using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using BandVault.Web.Data;
using BandVault.Web.Models;

namespace BandVault.Web.Controllers
{
    [Authorize]
    public class ReleasesController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _env;

        public ReleasesController(ApplicationDbContext context, IWebHostEnvironment env)
        {
            _context = context;
            _env = env;
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
        public async Task<IActionResult> Create(Release release, IFormFile coverImage)
        {
            if (!User.HasClaim("IsAdmin", "true")) return Forbid();

            if (ModelState.IsValid)
            {
                if (coverImage != null && coverImage.Length > 0)
                {
                    string fileName = Guid.NewGuid().ToString() + Path.GetExtension(coverImage.FileName);
                    string uploadPath = Path.Combine(_env.WebRootPath, "images", "releases");
                    Directory.CreateDirectory(uploadPath);

                    using (var stream = new FileStream(Path.Combine(uploadPath, fileName), FileMode.Create))
                    {
                        await coverImage.CopyToAsync(stream);
                    }
                    release.CoverImageUrl = "/images/releases/" + fileName;
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

                    if (coverImage != null && coverImage.Length > 0)
                    {
                        string fileName = Guid.NewGuid().ToString() + Path.GetExtension(coverImage.FileName);
                        string uploadPath = Path.Combine(_env.WebRootPath, "images", "releases");
                        Directory.CreateDirectory(uploadPath);

                        using (var stream = new FileStream(Path.Combine(uploadPath, fileName), FileMode.Create))
                        {
                            await coverImage.CopyToAsync(stream);
                        }
                        existingRelease.CoverImageUrl = "/images/releases/" + fileName;
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
                string fileName = Guid.NewGuid().ToString() + Path.GetExtension(audioFile.FileName);
                string uploadPath = Path.Combine(_env.WebRootPath, "audio");
                Directory.CreateDirectory(uploadPath);

                using (var stream = new FileStream(Path.Combine(uploadPath, fileName), FileMode.Create))
                {
                    await audioFile.CopyToAsync(stream);
                }

                var track = new Track
                {
                    ReleaseId = releaseId,
                    Title = title,
                    AudioFileUrl = "/audio/" + fileName,
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
                if (!string.IsNullOrEmpty(track.AudioFileUrl))
                {
                    string filePath = Path.Combine(_env.WebRootPath, track.AudioFileUrl.TrimStart('/'));
                    if (System.IO.File.Exists(filePath))
                    {
                        System.IO.File.Delete(filePath);
                    }
                }

                _context.Tracks.Remove(track);
                await _context.SaveChangesAsync();
            }
            return RedirectToAction(nameof(Details), new { id = releaseId });
        }
    }
}