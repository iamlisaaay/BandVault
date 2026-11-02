using System.Security.Claims;
using BandVault.Web.Data;
using BandVault.Web.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

namespace BandVault.Web.Services
{
    // C16: створення/оновлення користувача після підтвердження особи (Google OAuth2) і вхід через cookie.
    // Вхід без пароля в застосунку відсутній: особу підтверджує лише зовнішній провайдер.
    public class UserSignInService
    {
        private readonly ApplicationDbContext _context;
        private readonly IConfiguration _config;

        public UserSignInService(ApplicationDbContext context, IConfiguration config)
        {
            _context = context;
            _config = config;
        }

        public async Task<User> SignInAsync(HttpContext httpContext, string userEmail, string? displayName, string? avatarUrl)
        {
            userEmail = userEmail.Trim().ToLowerInvariant();
            var superAdminEmail = _config["AdminSettings:SuperAdminEmail"]?.ToLowerInvariant() ?? "danchenko_anastasiia@knu.ua";
            bool isSuperAdmin = userEmail == superAdminEmail;

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == userEmail);

            if (user == null)
            {
                var lowestTier = await _context.SubscriptionTiers.OrderBy(t => t.Level).FirstOrDefaultAsync();
                var highestTier = await _context.SubscriptionTiers.OrderByDescending(t => t.Level).FirstOrDefaultAsync();

                user = new User
                {
                    Email = userEmail,
                    DisplayName = displayName ?? userEmail.Split('@')[0],
                    IsAdmin = isSuperAdmin,
                    SubscriptionTierId = isSuperAdmin ? highestTier?.Id ?? 1 : lowestTier?.Id ?? 1,
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
                    if (highestTier != null) user.SubscriptionTierId = highestTier.Id;
                    needsUpdate = true;
                }

                if (string.IsNullOrEmpty(user.AvatarUrl) && !string.IsNullOrEmpty(avatarUrl))
                {
                    user.AvatarUrl = avatarUrl;
                    needsUpdate = true;
                }

                if (needsUpdate) await _context.SaveChangesAsync();
            }

            var userClaims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, user.Email),
                new Claim("UserId", user.Id.ToString()),
                new Claim("DisplayName", user.DisplayName ?? user.Email.Split('@')[0]),
                new Claim("IsAdmin", user.IsAdmin.ToString().ToLower()),
                new Claim("AvatarUrl", user.AvatarUrl ?? AvatarPlaceholder.For(user.DisplayName ?? user.Email))
            };

            var principal = new ClaimsPrincipal(new ClaimsIdentity(userClaims, CookieAuthenticationDefaults.AuthenticationScheme));
            await httpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);
            return user;
        }
    }
}
