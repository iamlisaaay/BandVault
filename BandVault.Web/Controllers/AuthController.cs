using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using BandVault.Web.Data;
using BandVault.Web.Models;

namespace BandVault.Web.Controllers
{
    public class AuthController : Controller
    {
        private readonly ApplicationDbContext _context;

        public AuthController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public IActionResult Login()
        {
            return View();
        }
        [HttpPost]
        public async Task<IActionResult> Login(string email)
        {
            if (string.IsNullOrWhiteSpace(email)) return View();

            string userEmail = email.Trim().ToLower();

            // Перевіряємо, чи це ти (або дефолтний адмін з appsettings)
            bool isSuperAdmin = userEmail == "danchenko_anastasiia@knu.ua";

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == userEmail);

            if (user == null)
            {
                // ВИПРАВЛЕННЯ: Дістаємо реальні ID з бази замість 0 та 4
                var lowestTier = await _context.SubscriptionTiers.OrderBy(t => t.Level).FirstOrDefaultAsync();
                var highestTier = await _context.SubscriptionTiers.OrderByDescending(t => t.Level).FirstOrDefaultAsync();

                int guestTierId = lowestTier != null ? lowestTier.Id : 1;
                int adminTierId = highestTier != null ? highestTier.Id : 1;

                user = new User
                {
                    Email = userEmail,
                    DisplayName = userEmail.Split('@')[0],
                    IsAdmin = isSuperAdmin,
                    // Якщо адмін - даємо макс. рівень, інакше - базовий
                    SubscriptionTierId = isSuperAdmin ? adminTierId : guestTierId
                };

                _context.Users.Add(user);
                await _context.SaveChangesAsync(); // Тепер база прийме правильні ID
            }
            else if (isSuperAdmin && !user.IsAdmin)
            {
                // Якщо ти вже була в базі, але без адмінки — апгрейдимо тебе
                var highestTier = await _context.SubscriptionTiers.OrderByDescending(t => t.Level).FirstOrDefaultAsync();

                user.IsAdmin = true;
                if (highestTier != null) user.SubscriptionTierId = highestTier.Id;

                await _context.SaveChangesAsync();
            }

            // Створюємо "паспорт" сесії
            var claims = new List<Claim>
    {
        new Claim(ClaimTypes.Name, user.Email),
        new Claim("UserId", user.Id.ToString()),
        new Claim("DisplayName", user.DisplayName ?? user.Email.Split('@')[0]),
        new Claim("IsAdmin", user.IsAdmin.ToString().ToLower()),
        new Claim("AvatarUrl", user.AvatarUrl ?? "https://via.placeholder.com/150/1a1a1a/ffffff?text=" + user.Email[0].ToString().ToUpper())
    };

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(identity);

            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);

            return RedirectToAction("Index", "Home");
        }
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Index", "Home");
        }
    }
}