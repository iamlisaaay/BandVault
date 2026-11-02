using BandVault.Web.Data;
using BandVault.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace BandVault.Web.Services
{
    // ============================================
    // B6: FEATURE FLAGS НА ОСНОВІ ПЛАНУ ПІДПИСКИ
    // Мінімальний рівень для кожної функції задається в конфігурації (FeatureFlags:*),
    // тож доступ можна перемкнути без зміни коду.
    // ============================================
    public static class Features
    {
        public const string FanPrices = "fan_prices";           // знижені ціни на мерч
        public const string ExclusiveDemos = "exclusive_demos"; // закриті демо-записи
        public const string Backstage = "backstage";             // сторінка Backstage

        public static readonly IReadOnlyDictionary<string, int> DefaultMinLevels = new Dictionary<string, int>
        {
            [FanPrices] = 1,
            [ExclusiveDemos] = 2,
            [Backstage] = 3
        };
    }

    public class FeatureFlags
    {
        public const int AdminLevel = 999;

        private readonly ApplicationDbContext _context;
        private readonly IHttpContextAccessor _http;
        private readonly IConfiguration _config;
        private User? _user;
        private bool _loaded;

        public FeatureFlags(ApplicationDbContext context, IHttpContextAccessor http, IConfiguration config)
        {
            _context = context;
            _http = http;
            _config = config;
        }

        // Поточний користувач завантажується один раз на запит
        public async Task<User?> GetCurrentUserAsync()
        {
            if (_loaded) return _user;
            _loaded = true;
            var email = _http.HttpContext?.User.Identity?.IsAuthenticated == true ? _http.HttpContext.User.Identity.Name : null;
            if (email != null)
            {
                _user = await _context.Users.Include(u => u.SubscriptionTier).AsNoTracking().FirstOrDefaultAsync(u => u.Email == email);
            }
            return _user;
        }

        public async Task<bool> IsAdminAsync() => (await GetCurrentUserAsync())?.IsAdmin ?? false;

        public async Task<int> GetTierLevelAsync()
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return 0;
            return user.IsAdmin ? AdminLevel : user.SubscriptionTier?.Level ?? 0;
        }

        public int MinLevel(string feature) =>
            _config.GetValue<int?>($"FeatureFlags:{feature}") ?? Features.DefaultMinLevels.GetValueOrDefault(feature, int.MaxValue);

        public async Task<bool> IsEnabledAsync(string feature) => await GetTierLevelAsync() >= MinLevel(feature);
    }

    // [RequireFeature(Features.Backstage)] - без потрібного рівня користувач потрапляє на вибір плану
    public class RequireFeatureAttribute : TypeFilterAttribute
    {
        public RequireFeatureAttribute(string feature) : base(typeof(RequireFeatureFilter))
        {
            Arguments = new object[] { feature };
        }

        private class RequireFeatureFilter : IAsyncActionFilter
        {
            private readonly string _feature;
            private readonly FeatureFlags _flags;

            public RequireFeatureFilter(string feature, FeatureFlags flags)
            {
                _feature = feature;
                _flags = flags;
            }

            public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
            {
                if (!await _flags.IsEnabledAsync(_feature))
                {
                    context.Result = new RedirectResult("/?changeTier=1");
                    return;
                }
                await next();
            }
        }
    }
}
