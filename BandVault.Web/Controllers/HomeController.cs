using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BandVault.Web.Data;
using BandVault.Web.Models;

namespace BandVault.Web.Controllers
{
    public class HomeController : Controller
    {
        private readonly ApplicationDbContext _context;

        public HomeController(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            // 5 випадкових треків для правого блоку
            var randomTracks = await _context.Tracks
                .Include(t => t.Release)
                .OrderBy(t => Guid.NewGuid())
                .Take(5)
                .ToListAsync();

            // Останні новини (ТІЛЬКИ ОПУБЛІКОВАНІ)
            var latestPosts = await _context.Posts
                .Where(p => p.IsPublished == true) // <--- Фільтр по опублікованих
                .OrderByDescending(p => p.CreatedAt)
                .Take(10)
                .ToListAsync();

            ViewBag.RandomTracks = randomTracks;

            return View(latestPosts);
        }
    }
}