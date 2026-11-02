using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BandVault.Web.Data;
using BandVault.Web.Models;
using BandVault.Web.Services;

namespace BandVault.Web.Controllers
{
    [Authorize]
    public class UsersController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _env;
        private readonly IFileService _fileService;

        public UsersController(ApplicationDbContext context, IWebHostEnvironment env, IFileService fileService)
        {
            _context = context;
            _env = env;
            _fileService = fileService;
        }

        // GET: /Users/Details або /Users/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            User? targetUser;
            bool isOwnProfile = false;

            var currentEmail = User.Identity?.Name;
            var currentUser = await _context.Users
                .Include(u => u.SubscriptionTier)
                .FirstOrDefaultAsync(u => u.Email == currentEmail);

            if (currentUser == null) return NotFound();

            if (id == null || id == currentUser.Id)
            {
                targetUser = currentUser;
                isOwnProfile = true;
            }
            else
            {
                targetUser = await _context.Users
                    .Include(u => u.SubscriptionTier)
                    .FirstOrDefaultAsync(u => u.Id == id);

                if (targetUser == null) return NotFound();
            }

            if (string.IsNullOrEmpty(targetUser.AvatarUrl))
            {
                targetUser.AvatarUrl = AvatarPlaceholder.For(targetUser.DisplayName);
            }

            if (targetUser.IsAdmin)
            {
                targetUser.SubscriptionTier = new SubscriptionTier
                {
                    Level = 999,
                    Name = "ALL ACCESS"
                };
            }

            ViewBag.IsOwnProfile = isOwnProfile;
            return View(targetUser);
        }

        // POST: /Users/EditProfile
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditProfile(string displayName, string? bio, IFormFile? avatarFile)
        {
            var currentEmail = User.Identity?.Name;
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == currentEmail);

            if (user == null) return NotFound();

            user.DisplayName = displayName;
            user.Bio = bio;

            if (avatarFile != null && avatarFile.Length > 0)
            {
                // C19: аватари теж зберігаються у Blob Storage
                if (!string.IsNullOrEmpty(user.AvatarUrl))
                {
                    await _fileService.DeleteFileAsync(user.AvatarUrl);
                }
                user.AvatarUrl = await _fileService.UploadFileAsync(avatarFile, "avatars");
            }

            await _context.SaveChangesAsync();

            // ФІКС: Оновлюємо Cookie, щоб аватарка і нове ім'я одразу з'явилися в шапці
            var userClaims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, user.Email),
                new Claim("UserId", user.Id.ToString()),
                new Claim("DisplayName", user.DisplayName ?? user.Email.Split('@')[0]),
                new Claim("IsAdmin", user.IsAdmin.ToString().ToLower()),
                new Claim("AvatarUrl", user.AvatarUrl ?? AvatarPlaceholder.For(user.DisplayName ?? user.Email))
            };

            var identity = new ClaimsIdentity(userClaims, CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(identity);

            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);

            return RedirectToAction(nameof(Details));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangeTier(int tierId)
        {
            var currentEmail = User.Identity?.Name;
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == currentEmail);

            if (user == null) return RedirectToAction("Login", "Auth");

            var tier = await _context.SubscriptionTiers.FindAsync(tierId);
            if (tier == null) return NotFound();

            var currentTier = await _context.SubscriptionTiers.FindAsync(user.SubscriptionTierId);
            // B6: безкоштовний план або зниження рівня - одразу; платне підвищення - лише через підтвердження оплати
            if (tier.Price > 0 && tier.Level > (currentTier?.Level ?? 0) && !user.IsAdmin)
            {
                return RedirectToAction(nameof(Subscribe), new { id = tier.Id });
            }

            user.SubscriptionTierId = tier.Id;
            await _context.SaveChangesAsync();
            return RedirectToAction("Index", "Home");
        }

        // Сторінка підтвердження платного плану
        [HttpGet]
        public async Task<IActionResult> Subscribe(int id)
        {
            var tier = await _context.SubscriptionTiers.Include(t => t.Features).FirstOrDefaultAsync(t => t.Id == id);
            if (tier == null || tier.Price <= 0) return NotFound();
            return View(tier);
        }

        // Демо-оплата: у проєкті немає платіжного провайдера, тому підтвердження імітує успішний платіж.
        // Перехід на платний план можливий лише через цю явну дію користувача з токеном форми.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ConfirmSubscribe(int id, bool acceptTerms)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == User.Identity!.Name);
            var tier = await _context.SubscriptionTiers.FindAsync(id);
            if (user == null || tier == null) return NotFound();
            if (!acceptTerms)
            {
                TempData["SubscribeError"] = "Підтвердіть умови підписки.";
                return RedirectToAction(nameof(Subscribe), new { id });
            }

            user.SubscriptionTierId = tier.Id;
            await _context.SaveChangesAsync();
            TempData["SubscribeSuccess"] = $"План {tier.Name} активовано.";
            return RedirectToAction(nameof(Details));
        }
    }
}