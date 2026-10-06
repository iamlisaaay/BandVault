using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using BandVault.Web.Data;
using BandVault.Web.Models;

namespace BandVault.Web.Controllers
{
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

        public IActionResult Create()
        {
            ViewBag.Genres = new SelectList(_context.Genres, "Id", "Name");
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Release release, IFormFile coverImage)
        {
            if (ModelState.IsValid)
            {
                if (coverImage != null && coverImage.Length > 0)
                {
                    string fileName = Guid.NewGuid().ToString() + Path.GetExtension(coverImage.FileName);
                    string uploadPath = Path.Combine(_env.WebRootPath, "images", "releases");
                    Directory.CreateDirectory(uploadPath);

                    string filePath = Path.Combine(uploadPath, fileName);
                    using (var stream = new FileStream(filePath, FileMode.Create))
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

        // Видалення треку

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteTrack(int trackId, int releaseId)
        {
            var track = await _context.Tracks.FindAsync(trackId);
            if (track != null)
            {
                // 1. Видаляємо фізичний аудіофайл з папки
                if (!string.IsNullOrEmpty(track.AudioFileUrl))
                {
                    // Забираємо перший слеш з "/audio/ім'я.mp3", щоб правильно склеїти шлях
                    string filePath = Path.Combine(_env.WebRootPath, track.AudioFileUrl.TrimStart('/'));

                    // Перевіряємо, чи файл дійсно існує на диску, і видаляємо його
                    if (System.IO.File.Exists(filePath))
                    {
                        System.IO.File.Delete(filePath);
                    }
                }

                // 2. Видаляємо запис про трек з бази даних
                _context.Tracks.Remove(track);
                await _context.SaveChangesAsync();
            }
            return RedirectToAction(nameof(Details), new { id = releaseId });
        }


        // Редагування релізу (GET)

        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();

            var release = await _context.Releases.FindAsync(id);
            if (release == null) return NotFound();

            ViewBag.Genres = new SelectList(_context.Genres, "Id", "Name", release.GenreId);
            return View(release);
        }


        // Редагування релізу (POST)

        // ==========================================
        // 8. Редагування релізу (POST) - ВИПРАВЛЕНО
        // ==========================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, Release release, IFormFile? coverImage)
        {
            if (id != release.Id) return NotFound();

            if (ModelState.IsValid)
            {
                try
                {
                    // 1. Дістаємо оригінальний реліз з бази даних
                    var existingRelease = await _context.Releases.FindAsync(id);
                    if (existingRelease == null) return NotFound();

                    // 2. Оновлюємо тільки ті поля, які прийшли з форми (треки залишаються недоторканими!)
                    existingRelease.Title = release.Title;
                    existingRelease.Description = release.Description;
                    existingRelease.GenreId = release.GenreId;

                    // UTC-фікс для PostgreSQL
                    existingRelease.ReleaseDate = DateTime.SpecifyKind(release.ReleaseDate, DateTimeKind.Utc);

                    // 3. Якщо завантажили нову обкладинку
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

                    // 4. Зберігаємо зміни
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


        // Видалення релізу

        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null) return NotFound();

            var release = await _context.Releases
                .Include(r => r.Genre)
                .FirstOrDefaultAsync(m => m.Id == id);

            if (release == null) return NotFound();

            return View(release);
        }

       
        // Видалення релізу
 
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var release = await _context.Releases.FindAsync(id);
            if (release != null)
            {
                _context.Releases.Remove(release);
                await _context.SaveChangesAsync();
            }
            return RedirectToAction(nameof(Index));
        }
        public async Task<IActionResult> Details(int id)
        {
            var release = await _context.Releases
                .Include(r => r.Genre)
                .Include(r => r.Tracks)
                .FirstOrDefaultAsync(r => r.Id == id);

            if (release == null) return NotFound();
            return View(release);
        }

        [HttpPost]
        public async Task<IActionResult> AddTrack(int releaseId, string title, IFormFile audioFile)
        {
            if (audioFile != null && audioFile.Length > 0)
            {
                string fileName = Guid.NewGuid().ToString() + Path.GetExtension(audioFile.FileName);
                string uploadPath = Path.Combine(_env.WebRootPath, "audio");
                Directory.CreateDirectory(uploadPath);

                string filePath = Path.Combine(uploadPath, fileName);
                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await audioFile.CopyToAsync(stream);
                }

                var track = new Track
                {
                    ReleaseId = releaseId,
                    Title = title,
                    AudioFileUrl = "/audio/" + fileName, // Твоє поле з моделі
                    DurationSeconds = 0, // Можна додати вирахування пізніше
                    PlayCount = 0,
                    RequiredTierLevel = 0
                };

                _context.Tracks.Add(track);
                await _context.SaveChangesAsync();
            }
            return RedirectToAction(nameof(Details), new { id = releaseId });
        }
    }
}