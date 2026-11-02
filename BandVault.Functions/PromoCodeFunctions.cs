using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;

namespace BandVault.Functions
{
    public record PromoRequest(int DiscountPercent, int ValidDays, string? Campaign);

    // ============================================
    // B7: ПРОМОКОДИ ДЛЯ ФАН-КЛУБУ
    // Код підписаний HMAC-SHA256, тож перевірка не потребує бази даних:
    //   BV{знижка}-{yyMMdd до}-{випадкова частина}-{підпис}
    // ============================================
    public class PromoCodeFunctions
    {
        private readonly IConfiguration _config;

        public PromoCodeFunctions(IConfiguration config)
        {
            _config = config;
        }

        // Генерацію захищено function key (AuthorizationLevel.Function) - викликає лише адмінка / бекенд
        [Function("GeneratePromoCode")]
        public async Task<IActionResult> Generate(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "promo/generate")] HttpRequest req)
        {
            PromoRequest? request;
            try { request = await req.ReadFromJsonAsync<PromoRequest>(); }
            catch { return new BadRequestObjectResult(new { error = "Очікується JSON { discountPercent, validDays, campaign }" }); }

            if (request == null || request.DiscountPercent is < 1 or > 90 || request.ValidDays is < 1 or > 365)
                return new BadRequestObjectResult(new { error = "discountPercent: 1-90, validDays: 1-365" });

            var secret = Secret;
            if (secret == null) return new ObjectResult(new { error = "PROMO_SECRET не налаштовано" }) { StatusCode = 500 };

            var expires = DateTime.UtcNow.Date.AddDays(request.ValidDays);
            var random = Convert.ToHexString(RandomNumberGenerator.GetBytes(3));
            var payload = $"BV{request.DiscountPercent:D2}-{expires:yyMMdd}-{random}";
            var code = $"{payload}-{Sign(payload, secret)}";

            return new OkObjectResult(new
            {
                code,
                discountPercent = request.DiscountPercent,
                expiresAt = expires.ToString("yyyy-MM-dd"),
                campaign = request.Campaign
            });
        }

        [Function("ValidatePromoCode")]
        public IActionResult Validate(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "promo/validate")] HttpRequest req)
        {
            string code = req.Query["code"].ToString().Trim().ToUpperInvariant();
            var secret = Secret;
            if (secret == null) return new ObjectResult(new { error = "PROMO_SECRET не налаштовано" }) { StatusCode = 500 };

            var parts = code.Split('-');
            if (parts.Length != 4 || parts[0].Length != 4 || !parts[0].StartsWith("BV"))
                return new OkObjectResult(new { valid = false, reason = "Невірний формат коду" });

            var payload = string.Join('-', parts.Take(3));
            var expectedSig = Encoding.ASCII.GetBytes(Sign(payload, secret));
            if (!CryptographicOperations.FixedTimeEquals(expectedSig, Encoding.ASCII.GetBytes(parts[3])))
                return new OkObjectResult(new { valid = false, reason = "Підпис не збігається" });

            if (!DateTime.TryParseExact(parts[1], "yyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var expires))
                return new OkObjectResult(new { valid = false, reason = "Невірна дата" });
            if (expires < DateTime.UtcNow.Date)
                return new OkObjectResult(new { valid = false, reason = "Термін дії минув", expiresAt = expires.ToString("yyyy-MM-dd") });

            return new OkObjectResult(new
            {
                valid = true,
                discountPercent = int.Parse(parts[0][2..]),
                expiresAt = expires.ToString("yyyy-MM-dd")
            });
        }

        private string? Secret => string.IsNullOrWhiteSpace(_config["PROMO_SECRET"]) ? null : _config["PROMO_SECRET"];

        private static string Sign(string payload, string secret)
        {
            var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(payload));
            return Convert.ToHexString(hash)[..8];
        }
    }
}
