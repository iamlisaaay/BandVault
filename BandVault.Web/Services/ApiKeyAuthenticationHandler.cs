using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace BandVault.Web.Services
{
    // Автентифікація сервісних клієнтів (Postman, MCP-агенти, Azure Functions) за заголовком X-Api-Key.
    // Ключ задається в конфігурації Api:Key (user-secrets / змінна середовища Api__Key).
    public class ApiKeyAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public const string SchemeName = "ApiKey";
        public const string HeaderName = "X-Api-Key";

        private readonly IConfiguration _config;

        public ApiKeyAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, IConfiguration config)
            : base(options, logger, encoder)
        {
            _config = config;
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(HeaderName, out var provided) || string.IsNullOrEmpty(provided))
                return Task.FromResult(AuthenticateResult.NoResult());

            var expected = _config["Api:Key"];
            if (string.IsNullOrEmpty(expected))
                return Task.FromResult(AuthenticateResult.Fail("API-ключ не налаштовано на сервері"));

            var providedBytes = Encoding.UTF8.GetBytes(provided.ToString());
            var expectedBytes = Encoding.UTF8.GetBytes(expected);
            if (providedBytes.Length != expectedBytes.Length || !CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes))
                return Task.FromResult(AuthenticateResult.Fail("Невірний API-ключ"));

            var identity = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.Name, "api-client"),
                new Claim("DisplayName", "API client"),
                new Claim("IsAdmin", "true")
            }, SchemeName);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }
}
