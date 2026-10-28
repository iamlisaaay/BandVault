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
            var settings = await _context.SiteSettings.ToListAsync();

            ViewBag.HeroVideoUrl = settings.FirstOrDefault(s => s.Key == "HeroVideoUrl")?.Value ?? "https://www.youtube.com/watch?v=F0Bfcfv_4k4";
            ViewBag.BandName = settings.FirstOrDefault(s => s.Key == "BandName")?.Value ?? "MADMANS ESPRIT";
            ViewBag.PromoText = settings.FirstOrDefault(s => s.Key == "PromoText")?.Value ?? "Dandelion";
            ViewBag.Social_YouTube = settings.FirstOrDefault(s => s.Key == "Social_YouTube")?.Value ?? "#";
            ViewBag.Social_Discord = settings.FirstOrDefault(s => s.Key == "Social_Discord")?.Value ?? "#";
            ViewBag.Social_Telegram = settings.FirstOrDefault(s => s.Key == "Social_Telegram")?.Value ?? "#";
            ViewBag.Social_Twitter = settings.FirstOrDefault(s => s.Key == "Social_Twitter")?.Value ?? "#";
            ViewBag.Social_Instagram = settings.FirstOrDefault(s => s.Key == "Social_Instagram")?.Value ?? "#";
            ViewBag.ContactEmail = settings.FirstOrDefault(s => s.Key == "ContactEmail")?.Value ?? "booking@bandvault.com";

            bool isAuthenticated = User.Identity?.IsAuthenticated ?? false;
            int currentUserTierLevel = 0;

            if (isAuthenticated)
            {
                var currentUser = await _context.Users
                    .Include(u => u.SubscriptionTier)
                    .FirstOrDefaultAsync(u => u.Email == User.Identity.Name);

                if (currentUser != null)
                {
                    ViewBag.DaysJoined = (int)(DateTime.UtcNow - currentUser.CreatedAt).TotalDays;

                    // Фікс B6: Адмін завжди отримує максимальний Tier для перегляду новин
                    if (currentUser.IsAdmin)
                    {
                        currentUserTierLevel = 999;
                    }
                    else if (currentUser.SubscriptionTier != null)
                    {
                        currentUserTierLevel = currentUser.SubscriptionTier.Level;
                    }
                }
            }

            ViewBag.IsAuthenticated = isAuthenticated;
            ViewBag.UserTierLevel = currentUserTierLevel;

            var tiers = await _context.SubscriptionTiers
                .Include(t => t.Features)
                .OrderBy(t => t.Level)
                .ToListAsync();
            ViewBag.Tiers = tiers;

            var adminPosts = await _context.Posts
                .Include(p => p.Author)
                .Where(p => p.IsPublished && p.Author != null && p.Author.IsAdmin)
                .OrderByDescending(p => p.CreatedAt)
                .Take(5)
                .ToListAsync();

            return View(adminPosts);
        }
    }
}