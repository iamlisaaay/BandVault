using BandVault.Web.Data;
using BandVault.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace BandVault.Web.Controllers
{
    public class ReleasesController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _webHostEnvironment;

        // Dependency Injection для доступу до бази та файлової системи
        public ReleasesController(ApplicationDbContext context, IWebHostEnvironment webHostEnvironment)
        {
            _context = context;
            _webHostEnvironment = webHostEnvironment;
        }

        // GET: /Releases
        public async Task<IActionResult> Index()
        {
            // Витягуємо всі релізи разом із їхніми жанрами
            var releases = await _context.Releases.Include(r => r.Genre).ToListAsync();
            return View(releases);
        }

        // GET: /Releases/Create
        public IActionResult Create()
        {
            // Передаємо список жанрів у випадаючий список
            ViewData["GenreId"] = new SelectList(_context.Genres, "Id", "Name");
            return View();
        }

        // POST: /Releases/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("Id,Title,Description,ReleaseDate,GenreId")] Release release, IFormFile? coverImage)
        {
            // Видаляємо валідацію для навігаційних властивостей, інакше форма не відправиться
            ModelState.Remove("Genre");

            if (ModelState.IsValid)
            {
                // Логіка збереження файлу (Закриваємо вимогу C1 по файлах)
                if (coverImage != null && coverImage.Length > 0)
                {
                    string uploadsFolder = Path.Combine(_webHostEnvironment.WebRootPath, "images", "releases");
                    string uniqueFileName = Guid.NewGuid().ToString() + "_" + coverImage.FileName;
                    string filePath = Path.Combine(uploadsFolder, uniqueFileName);

                    using (var fileStream = new FileStream(filePath, FileMode.Create))
                    {
                        await coverImage.CopyToAsync(fileStream);
                    }

                    release.CoverImageUrl = "/images/releases/" + uniqueFileName;
                }

                _context.Add(release);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }

            ViewData["GenreId"] = new SelectList(_context.Genres, "Id", "Name", release.GenreId);
            return View(release);
        }
    }
}