using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BandVault.Web.Data;
using BandVault.Web.Models;

namespace BandVault.Web.Controllers
{
    public class AuthController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IConfiguration _config;

        public AuthController(ApplicationDbContext context, IConfiguration config)
        {
            _context = context;
            _config = config;
        }

        [HttpGet]
        public IActionResult Login()
        {
            // Якщо користувач вже авторизований, не показуємо йому сторінку логіну
            if (User.Identity != null && User.Identity.IsAuthenticated)
            {
                return RedirectToAction("Index", "Home");
            }
            return View();
        }

        // ============================================
        // GOOGLE OAUTH2 ЛОГІКА (C16)
        // ============================================
        [HttpPost]
        public IActionResult ExternalLogin(string returnUrl = "/")
        {
            var redirectUrl = Url.Action(nameof(ExternalLoginCallback), "Auth", new { returnUrl });
            var properties = new AuthenticationProperties { RedirectUri = redirectUrl };
            return Challenge(properties, GoogleDefaults.AuthenticationScheme);
        }

        [HttpGet]
        public async Task<IActionResult> ExternalLoginCallback(string returnUrl = "/")
        {
            var result = await HttpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);

            if (!result.Succeeded || result.Principal == null)
            {
                return RedirectToAction(nameof(Login));
            }

            var claims = result.Principal.Claims;
            var email = claims.FirstOrDefault(c => c.Type == ClaimTypes.Email)?.Value;
            var name = claims.FirstOrDefault(c => c.Type == ClaimTypes.Name)?.Value;
            var avatar = claims.FirstOrDefault(c => c.Type == "urn:google:picture")?.Value;

            if (string.IsNullOrEmpty(email))
            {
                return RedirectToAction(nameof(Login));
            }

            return await ProcessUserLogin(email.ToLower(), name, avatar, returnUrl);
        }

        // ============================================
        // СПІЛЬНИЙ МЕТОД ДЛЯ СТВОРЕННЯ/ОНОВЛЕННЯ ЮЗЕРА
        // ============================================
        private async Task<IActionResult> ProcessUserLogin(string userEmail, string? displayName, string? avatarUrl, string returnUrl)
        {
            var superAdminEmail = _config["AdminSettings:SuperAdminEmail"]?.ToLower() ?? "danchenko_anastasiia@knu.ua";
            bool isSuperAdmin = userEmail == superAdminEmail;

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == userEmail);

            if (user == null)
            {
                var lowestTier = await _context.SubscriptionTiers.OrderBy(t => t.Level).FirstOrDefaultAsync();
                var highestTier = await _context.SubscriptionTiers.OrderByDescending(t => t.Level).FirstOrDefaultAsync();

                int guestTierId = lowestTier?.Id ?? 1;
                int adminTierId = highestTier?.Id ?? 1;

                user = new User
                {
                    Email = userEmail,
                    DisplayName = displayName ?? userEmail.Split('@')[0],
                    IsAdmin = isSuperAdmin,
                    SubscriptionTierId = isSuperAdmin ? adminTierId : guestTierId,
                    AvatarUrl = avatarUrl,
                    CreatedAt = DateTime.UtcNow
                };

                _context.Users.Add(user);
                await _context.SaveChangesAsync();
            }
            else
            {
                bool needsUpdate = false;

                if (isSuperAdmin && !user.IsAdmin)
                {
                    var highestTier = await _context.SubscriptionTiers.OrderByDescending(t => t.Level).FirstOrDefaultAsync();
                    user.IsAdmin = true;
                    if (highestTier != null)
                    {
                        user.SubscriptionTierId = highestTier.Id;
                    }
                    needsUpdate = true;
                }

                if (string.IsNullOrEmpty(user.AvatarUrl) && !string.IsNullOrEmpty(avatarUrl))
                {
                    user.AvatarUrl = avatarUrl;
                    needsUpdate = true;
                }

                if (needsUpdate)
                {
                    await _context.SaveChangesAsync();
                }
            }

            var userClaims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, user.Email),
                new Claim("UserId", user.Id.ToString()),
                new Claim("DisplayName", user.DisplayName ?? user.Email.Split('@')[0]),
                new Claim("IsAdmin", user.IsAdmin.ToString().ToLower()),
                new Claim("AvatarUrl", user.AvatarUrl ?? $"https://via.placeholder.com/150/1a1a1a/ffffff?text={user.Email[..1].ToUpper()}")
            };

            var identity = new ClaimsIdentity(userClaims, CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(identity);

            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);

            if (Url.IsLocalUrl(returnUrl))
            {
                return LocalRedirect(returnUrl);
            }

            return RedirectToAction("Index", "Home");
        }

        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Index", "Home");
        }

        // ============================================
        // ЕКШЕН ДЛЯ ДОСТУПУ ЗАБОРОНЕНО
        // ============================================
        [HttpGet]
        public IActionResult AccessDenied()
        {
            // Якщо користувач не має прав доступу, покажемо йому гарну сторінку 403
            return View();
        }
    }
}