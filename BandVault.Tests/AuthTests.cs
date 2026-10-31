using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using BandVault.Web.Data;
using Xunit;

namespace BandVault.Tests
{
    // Імітація авторизації для тестів
    public class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
            : base(options, logger, encoder) { }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var role = Context.Request.Headers["X-Test-Role"].ToString();
            if (string.IsNullOrEmpty(role)) return Task.FromResult(AuthenticateResult.NoResult());

            var claims = new List<Claim> { new Claim(ClaimTypes.Name, "testuser@knu.ua") };
            if (role == "Admin") claims.Add(new Claim("IsAdmin", "true"));

            var identity = new ClaimsIdentity(claims, "Test");
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, "Test");
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }

    public class AuthTests : IClassFixture<WebApplicationFactory<BandVault.Web.Program>>
    {
        private readonly WebApplicationFactory<BandVault.Web.Program> _factory;

        public AuthTests(WebApplicationFactory<BandVault.Web.Program> factory)
        {
            // Підміна конфігурації БД на In-Memory спеціально для тестів
            _factory = factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<ApplicationDbContext>));
                    if (descriptor != null) services.Remove(descriptor);

                    services.AddDbContext<ApplicationDbContext>(options => options.UseInMemoryDatabase("TestDb"));

                    // Додаємо тестового провайдера авторизації
                    services.AddAuthentication("Test")
                            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", options => { });
                });
            });
        }

        [Theory]
        [InlineData("/Posts/Create")]
        [InlineData("/SiteSettings/Index")]
        [InlineData("/Merch/Create")]
        public async Task Get_EndpointsReturnRedirectToLogin_ForUnauthenticatedUser(string url)
        {
            var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            var response = await client.GetAsync(url);

            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Contains("/Auth/Login", response.Headers.Location?.ToString());
        }

        [Fact]
        public async Task Get_Settings_ReturnsForbidden_ForNormalUser()
        {
            var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            client.DefaultRequestHeaders.Add("X-Test-Role", "User");

            var response = await client.GetAsync("/SiteSettings/Index");

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task Get_Settings_ReturnsSuccess_ForAdmin()
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Database.EnsureCreated(); // Гарантуємо, що In-Memory БД створена

            var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            client.DefaultRequestHeaders.Add("X-Test-Role", "Admin");

            var response = await client.GetAsync("/SiteSettings/Index");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }
}