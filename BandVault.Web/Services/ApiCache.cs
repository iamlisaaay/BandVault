using Microsoft.Extensions.Caching.Distributed;

namespace BandVault.Web.Services
{
    // ============================================
    // B9: КЕШ ВІДПОВІДЕЙ API з версіонуванням
    // Ключ = "{область}:v{версія}:{запит}". Будь-яка зміна даних збільшує версію області,
    // тож застарілими стають УСІ сторінки та пошукові запити, а не лише кілька захардкожених ключів.
    // Якщо Redis недоступний - кеш вимикається на 30 с, щоб не чекати тайм-аути на кожному запиті.
    // ============================================
    public class ApiCache
    {
        private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(60);
        private static readonly TimeSpan BreakerPause = TimeSpan.FromSeconds(30);

        private readonly IDistributedCache _cache;
        private readonly ILogger<ApiCache> _logger;
        private DateTime _disabledUntil = DateTime.MinValue;

        public ApiCache(IDistributedCache cache, ILogger<ApiCache> logger)
        {
            _cache = cache;
            _logger = logger;
        }

        public async Task<string?> GetAsync(string area, string key)
        {
            if (DateTime.UtcNow < _disabledUntil) return null;
            try
            {
                return await _cache.GetStringAsync(await BuildKeyAsync(area, key));
            }
            catch (Exception ex)
            {
                Trip(ex);
                return null;
            }
        }

        public async Task SetAsync(string area, string key, string json)
        {
            if (DateTime.UtcNow < _disabledUntil) return;
            try
            {
                await _cache.SetStringAsync(await BuildKeyAsync(area, key), json,
                    new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = Ttl });
            }
            catch (Exception ex)
            {
                Trip(ex);
            }
        }

        public async Task InvalidateAsync(string area)
        {
            try
            {
                var version = await GetVersionAsync(area);
                await _cache.SetStringAsync(VersionKey(area), (version + 1).ToString());
            }
            catch (Exception ex)
            {
                Trip(ex);
            }
        }

        // Пошуковий текст нормалізуємо й обрізаємо, щоб простір ключів був обмеженим
        public static string NormalizeQuery(string? query)
        {
            var q = (query ?? "").Trim().ToLowerInvariant();
            return q.Length == 0 ? "all" : q.Length > 50 ? q[..50] : q;
        }

        private async Task<string> BuildKeyAsync(string area, string key) => $"{area}:v{await GetVersionAsync(area)}:{key}";

        private async Task<long> GetVersionAsync(string area) =>
            long.TryParse(await _cache.GetStringAsync(VersionKey(area)), out var v) ? v : 0;

        private static string VersionKey(string area) => $"{area}:version";

        private void Trip(Exception ex)
        {
            _disabledUntil = DateTime.UtcNow + BreakerPause;
            _logger.LogWarning("Кеш недоступний ({Message}), вимикаємо на {Seconds} с", ex.Message, BreakerPause.TotalSeconds);
        }
    }
}
