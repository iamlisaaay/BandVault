using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using BandVault.Web.Data;
using BandVault.Web.Models;
using Xunit;

namespace BandVault.Tests
{
    // Автентифікує користувача з БД за заголовком X-Test-Email (рівень підписки береться з бази)
    public class EmailAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public EmailAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
            : base(options, logger, encoder) { }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var email = Context.Request.Headers["X-Test-Email"].ToString();
            if (string.IsNullOrEmpty(email)) return Task.FromResult(AuthenticateResult.NoResult());
            var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, email), new Claim("IsAdmin", "false") }, "Email");
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), "Email")));
        }
    }

    // QA3 + B6: доступ до функцій за планом підписки, захист форм, вимкнений тестовий вхід
    public class FeatureFlagTests : IClassFixture<WebApplicationFactory<BandVault.Web.Program>>
    {
        private const string FanEmail = "fan@test.ua";
        private const string VipEmail = "vip@test.ua";
        private readonly WebApplicationFactory<BandVault.Web.Program> _factory;

        public FeatureFlagTests(WebApplicationFactory<BandVault.Web.Program> factory)
        {
            _factory = factory.WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ConnectionStrings:RabbitMQ", "");
                builder.UseSetting("Storage:Provider", "Local");
                builder.ConfigureTestServices(services =>
                {
                    var dbDescriptors = services.Where(d =>
                        d.ServiceType == typeof(DbContextOptions<ApplicationDbContext>) ||
                        (d.ServiceType.IsGenericType && d.ServiceType.GetGenericTypeDefinition().Name.Contains("IDbContextOptionsConfiguration")))
                        .ToList();
                    foreach (var d in dbDescriptors) services.Remove(d);
                    services.AddDbContext<ApplicationDbContext>(options => options.UseInMemoryDatabase("FeatureFlagsDb"));

                    var cacheDescriptors = services.Where(d => d.ServiceType == typeof(IDistributedCache)).ToList();
                    foreach (var d in cacheDescriptors) services.Remove(d);
                    services.AddDistributedMemoryCache();

                    services.AddAuthentication(options =>
                    {
                        options.DefaultAuthenticateScheme = "Email";
                        options.DefaultChallengeScheme = Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme;
                    }).AddScheme<AuthenticationSchemeOptions, EmailAuthHandler>("Email", _ => { });
                });
            });
        }

        // Плани сіються при старті (GUEST 0, FAN 1, INSIDER 2, VIP CULT 3); додаємо користувачів і закритий трек
        private async Task<int> SeedAsync()
        {
            _factory.CreateClient(); // запускає хост і DbInitializer
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var tiers = await db.SubscriptionTiers.ToListAsync();
            if (!await db.Users.AnyAsync(u => u.Email == FanEmail))
            {
                db.Users.Add(new User { Email = FanEmail, DisplayName = "fan", SubscriptionTierId = tiers.Single(t => t.Level == 1).Id });
                db.Users.Add(new User { Email = VipEmail, DisplayName = "vip", SubscriptionTierId = tiers.Single(t => t.Level == 3).Id });
                var release = new Release { Title = "Demo EP", Description = "", ReleaseDate = DateTime.UtcNow, GenreId = 1 };
                db.Releases.Add(release);
                await db.SaveChangesAsync();
                db.Tracks.Add(new Track { Title = "VIP demo", ReleaseId = release.Id, AudioFileUrl = "/audio/x.mp3", RequiredTierLevel = 3 });
                await db.SaveChangesAsync();
            }
            return await db.Tracks.Where(t => t.RequiredTierLevel == 3).Select(t => t.Id).FirstAsync();
        }

        private HttpClient ClientFor(string? email)
        {
            var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            if (email != null) client.DefaultRequestHeaders.Add("X-Test-Email", email);
            return client;
        }

        [Fact]
        public async Task NewUsers_GetFreeGuestTier()
        {
            await SeedAsync();
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var lowest = await db.SubscriptionTiers.OrderBy(t => t.Level).FirstAsync();
            Assert.Equal(0, lowest.Level);
            Assert.Equal(0m, lowest.Price);
        }

        [Fact]
        public async Task Backstage_RedirectsFanToPlanSelection()
        {
            await SeedAsync();
            var response = await ClientFor(FanEmail).GetAsync("/Backstage");
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Contains("changeTier=1", response.Headers.Location?.ToString());
        }

        [Fact]
        public async Task Backstage_IsAvailableForVip()
        {
            await SeedAsync();
            var response = await ClientFor(VipEmail).GetAsync("/Backstage");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("VIP demo", await response.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task LockedTrack_CannotBePlayedByLowerTier()
        {
            var trackId = await SeedAsync();
            var response = await ClientFor(FanEmail).PostAsync($"/Releases/RegisterPlay/{trackId}", null);
            Assert.True(response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Redirect);
        }

        [Fact]
        public async Task LockedTrack_UrlIsNotRenderedForLowerTier()
        {
            await SeedAsync();
            using var scope = _factory.Services.CreateScope();
            var releaseId = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Releases.Where(r => r.Title == "Demo EP").Select(r => r.Id).FirstAsync();

            var html = await (await ClientFor(FanEmail).GetAsync($"/Releases/Details/{releaseId}")).Content.ReadAsStringAsync();
            Assert.DoesNotContain("/audio/x.mp3", html);
            Assert.Contains("TIER 3+", html);
        }

        [Fact]
        public async Task ChangeTier_WithoutAntiforgeryToken_IsRejected()
        {
            await SeedAsync();
            var response = await ClientFor(FanEmail).PostAsync("/Users/ChangeTier", new FormUrlEncodedContent(new Dictionary<string, string> { ["tierId"] = "1" }));
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task DevLogin_IsDisabledOutsideDevelopment()
        {
            var production = _factory.WithWebHostBuilder(b => b.UseEnvironment("Production"));
            var client = production.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            var response = await client.PostAsync("/Auth/Login", new FormUrlEncodedContent(new Dictionary<string, string> { ["email"] = "hacker@test.ua" }));
            Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
            var cookies = response.Headers.TryGetValues("Set-Cookie", out var values) ? values : Array.Empty<string>();
            Assert.DoesNotContain(cookies, c => c.StartsWith(".AspNetCore.Cookies"));
        }
    }
}
