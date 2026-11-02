using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BandVault.Web.Data;
using BandVault.Web.Models;

namespace BandVault.Web.Controllers
{
    [Authorize]
    public class SiteSettingsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly Microsoft.Extensions.Caching.Memory.IMemoryCache _memoryCache;

        public SiteSettingsController(ApplicationDbContext context, Microsoft.Extensions.Caching.Memory.IMemoryCache memoryCache)
        {
            _context = context;
            _memoryCache = memoryCache;
        }

        public async Task<IActionResult> Index()
        {
            if (!User.HasClaim("IsAdmin", "true")) return Forbid();

            // ФІКС F1: Збираємо статистику підписників для графіка Chart.js
            var usersByTier = await _context.Users
                .Include(u => u.SubscriptionTier)
                .GroupBy(u => u.SubscriptionTier.Name)
                .Select(g => new { TierName = g.Key, Count = g.Count() })
                .ToListAsync();

            ViewBag.ChartLabels = usersByTier.Select(u => u.TierName).ToArray();
            ViewBag.ChartData = usersByTier.Select(u => u.Count).ToArray();

            // F1: топ-10 треків за прослуховуваннями
            var topTracks = await _context.Tracks
                .OrderByDescending(t => t.PlayCount)
                .Take(10)
                .Select(t => new { t.Title, t.PlayCount })
                .ToListAsync();
            ViewBag.PlaysLabels = topTracks.Select(t => t.Title).ToArray();
            ViewBag.PlaysData = topTracks.Select(t => t.PlayCount).ToArray();

            // F1: динаміка замовлень мерчу за останні 14 днів (кількість і виручка по днях)
            var since = DateTime.UtcNow.Date.AddDays(-13);
            var ordersByDay = await _context.Orders
                .Where(o => o.OrderDate >= since)
                .GroupBy(o => o.OrderDate.Date)
                .Select(g => new { Day = g.Key, Count = g.Count(), Revenue = g.Sum(o => o.TotalAmount) })
                .ToListAsync();
            var days = Enumerable.Range(0, 14).Select(i => since.AddDays(i)).ToList();
            ViewBag.OrdersLabels = days.Select(d => d.ToString("dd.MM")).ToArray();
            ViewBag.OrdersCount = days.Select(d => ordersByDay.FirstOrDefault(o => o.Day == d)?.Count ?? 0).ToArray();
            ViewBag.OrdersRevenue = days.Select(d => ordersByDay.FirstOrDefault(o => o.Day == d)?.Revenue ?? 0m).ToArray();

            return View(await _context.SiteSettings.OrderBy(s => s.Key).ToListAsync());
        }

        public async Task<IActionResult> Edit(int? id)
        {
            if (!User.HasClaim("IsAdmin", "true")) return Forbid();

            if (id == null) return NotFound();

            var siteSetting = await _context.SiteSettings.FindAsync(id);
            if (siteSetting == null) return NotFound();

            return View(siteSetting);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("Id,Key,Value")] SiteSetting siteSetting)
        {
            if (!User.HasClaim("IsAdmin", "true")) return Forbid();

            if (id != siteSetting.Id) return NotFound();

            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(siteSetting);
                    await _context.SaveChangesAsync();
                    // Соцмережі/контакти в шаблоні кешуються - скидаємо, щоб зміни було видно одразу
                    _memoryCache.Remove("layout_social_settings");
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!SiteSettingExists(siteSetting.Id)) return NotFound();
                    else throw;
                }

                return RedirectToAction(nameof(Index));
            }
            return View(siteSetting);
        }

        private bool SiteSettingExists(int id)
        {
            return _context.SiteSettings.Any(e => e.Id == id);
        }
    }
}