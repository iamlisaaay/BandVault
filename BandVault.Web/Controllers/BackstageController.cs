using BandVault.Web.Data;
using BandVault.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BandVault.Web.Controllers
{
    // B6: "особлива" сторінка лише для плану з прапорцем backstage (за замовчуванням VIP CULT)
    [Authorize]
    [RequireFeature(Features.Backstage)]
    public class BackstageController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly FeatureFlags _features;

        public BackstageController(ApplicationDbContext context, FeatureFlags features)
        {
            _context = context;
            _features = features;
        }

        public async Task<IActionResult> Index()
        {
            int demoLevel = _features.MinLevel(Features.ExclusiveDemos);

            ViewBag.Demos = await _context.Tracks
                .Include(t => t.Release)
                .Where(t => t.RequiredTierLevel >= demoLevel)
                .OrderByDescending(t => t.Id)
                .ToListAsync();

            ViewBag.Posts = await _context.Posts
                .Where(p => p.IsPublished && p.RequiredTierLevel >= demoLevel)
                .OrderByDescending(p => p.CreatedAt)
                .Take(10)
                .ToListAsync();

            ViewBag.TierName = (await _features.GetCurrentUserAsync())?.SubscriptionTier?.Name ?? "VIP";
            return View();
        }
    }
}
