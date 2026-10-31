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

        public SiteSettingsController(ApplicationDbContext context)
        {
            _context = context;
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