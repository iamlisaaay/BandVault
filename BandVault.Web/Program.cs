using BandVault.Web.Data;
using BandVault.Web.Services;
using BandVault.Web.Services.Catalog;
using BandVault.Web.Services.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.HttpOverrides;

namespace BandVault.Web
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // C2: хмарні платформи (Render, Railway, Heroku) віддають PostgreSQL як DATABASE_URL у форматі URI
            builder.Services.AddDbContext<ApplicationDbContext>(options =>
                options.UseNpgsql(DatabaseUrl.Resolve(builder.Configuration)));

            // ============================================
            // АВТОРИЗАЦІЯ ЧЕРЕЗ COOKIE ТА GOOGLE OAUTH2 (C16) + API-КЛЮЧ ДЛЯ СЕРВІСНИХ КЛІЄНТІВ
            // ============================================
            builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
            {
                options.LoginPath = "/Auth/Login";
                options.AccessDeniedPath = "/Auth/AccessDenied";
                // API та MCP мають отримувати 401/403, а не редирект на HTML-сторінку логіну
                options.Events.OnRedirectToLogin = ctx => RedirectOrStatus(ctx, StatusCodes.Status401Unauthorized);
                options.Events.OnRedirectToAccessDenied = ctx => RedirectOrStatus(ctx, StatusCodes.Status403Forbidden);
                // C16: права з cookie звіряються з БД раз на 5 хв - зняті права адміна перестають діяти без чекання кінця сесії
                options.Events.OnValidatePrincipal = ValidatePrincipalAsync;
            })
            .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(ApiKeyAuthenticationHandler.SchemeName, null)
            .AddGoogle(GoogleDefaults.AuthenticationScheme, googleOptions =>
            {
                var clientId = builder.Configuration["Authentication:Google:ClientId"];
                var clientSecret = builder.Configuration["Authentication:Google:ClientSecret"];

                googleOptions.ClientId = string.IsNullOrWhiteSpace(clientId) ? "DUMMY_CLIENT_ID" : clientId;
                googleOptions.ClientSecret = string.IsNullOrWhiteSpace(clientSecret) ? "DUMMY_CLIENT_SECRET" : clientSecret;
                googleOptions.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                googleOptions.ClaimActions.MapJsonKey("urn:google:picture", "picture");
            });

            builder.Services.AddAuthorization(options =>
            {
                // Запис через REST API / MCP: адміністратор (cookie) або сервісний клієнт з X-Api-Key
                options.AddPolicy("ApiWrite", policy => policy
                    .AddAuthenticationSchemes(CookieAuthenticationDefaults.AuthenticationScheme, ApiKeyAuthenticationHandler.SchemeName)
                    .RequireClaim("IsAdmin", "true"));
            });

            // ============================================
            // B9: КЕШУВАННЯ (REDIS DISTRIBUTED CACHE)
            // ============================================
            var redisConnectionString = builder.Configuration.GetConnectionString("Redis");
            if (!string.IsNullOrWhiteSpace(redisConnectionString))
            {
                builder.Services.AddStackExchangeRedisCache(options =>
                {
                    // Додано таймаути, щоб додаток не вис на 5с, якщо локальний Redis лежить
                    options.Configuration = $"{redisConnectionString},connectTimeout=300,syncTimeout=300,abortConnect=false";
                    options.InstanceName = "BandVault_";
                });
            }
            else
            {
                builder.Services.AddDistributedMemoryCache();
            }
            builder.Services.AddSignalR();
            // УВІМКНЕННЯ СЕСІЙ ДЛЯ КОШИКА
            builder.Services.AddSession(options =>
            {
                options.IdleTimeout = TimeSpan.FromHours(2);
                options.Cookie.HttpOnly = true;
                options.Cookie.IsEssential = true;
            });
            builder.Services.AddControllersWithViews()
                .AddJsonOptions(options =>
                {
                    options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
                });

            // C19: медіа - у Blob Storage (Azure / Azurite); Storage:Provider=Local - у wwwroot/uploads
            if (builder.Configuration["Storage:Provider"] == "Local" || string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("AzureBlobStorage")))
            {
                builder.Services.AddScoped<IFileService, LocalFileService>();
            }
            else
            {
                builder.Services.AddSingleton<IFileService, AzureBlobService>();
            }

            // C10/C11: залишки мерчу наживо; B4: пошуковий індекс Lucene; B9: кеш API; зміни каталогу
            builder.Services.AddSingleton<StockNotifier>();
            builder.Services.AddSingleton<SearchIndexService>();
            builder.Services.AddSingleton<ApiCache>();
            builder.Services.AddSingleton<CatalogChangeNotifier>();
            builder.Services.AddMemoryCache();

            // B6: feature flags на основі плану підписки
            builder.Services.AddHttpContextAccessor();
            builder.Services.AddScoped<FeatureFlags>();

            // B8: черга замовлень (RabbitMQ) + фоновий обробник; B2: Telegram
            QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
            builder.Services.AddSingleton<OrderQueue>();
            builder.Services.AddHostedService<OrderProcessingWorker>();
            builder.Services.AddHttpClient();
            builder.Services.AddSingleton<TelegramNotifier>(sp => new TelegramNotifier(
                sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(TelegramNotifier)),
                sp.GetRequiredService<IConfiguration>(),
                sp.GetRequiredService<ILogger<TelegramNotifier>>()));

            // C21/C22: інструменти каталогу для ШІ-агента та MCP-сервера
            builder.Services.AddScoped<CatalogTools>();
            builder.Services.AddMcpServer()
                .WithHttpTransport()
                .WithTools<CatalogMcpTools>();

            // C2: за зворотним проксі хмарного провайдера (HTTPS термінується на балансувальнику)
            builder.Services.Configure<ForwardedHeadersOptions>(options =>
            {
                options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
                options.KnownIPNetworks.Clear();
                options.KnownProxies.Clear();
            });

            var app = builder.Build();

            app.UseForwardedHeaders();

            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Home/Error");
                app.UseHsts();
            }

            app.UseHttpsRedirection();

            // C1: українські формати дат, але десяткова крапка - як у полях type="number" (інакше "25.50" не проходить валідацію)
            var appCulture = (System.Globalization.CultureInfo)System.Globalization.CultureInfo.GetCultureInfo("uk-UA").Clone();
            appCulture.NumberFormat.NumberDecimalSeparator = ".";
            appCulture.NumberFormat.CurrencyDecimalSeparator = ".";
            app.UseRequestLocalization(new RequestLocalizationOptions
            {
                DefaultRequestCulture = new Microsoft.AspNetCore.Localization.RequestCulture(appCulture),
                SupportedCultures = new[] { appCulture },
                SupportedUICultures = new[] { appCulture },
                RequestCultureProviders = new List<Microsoft.AspNetCore.Localization.IRequestCultureProvider>()
            });

            app.UseSession();
            app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(30) });
            app.UseRouting();
            app.UseAuthentication();
            app.UseAuthorization();

            app.MapStaticAssets();
            app.MapControllers();
            app.MapHub<BandVault.Web.Hubs.FeedHub>("/feedHub");
            app.MapHub<BandVault.Web.Hubs.StockHub>("/stockHub");
            app.MapHub<BandVault.Web.Hubs.BoardHub>("/boardHub");
            app.MapHub<BandVault.Web.Hubs.EditorHub>("/editorHub");

            // C10: "сирий" WebSocket для залишків мерчу
            app.Map("/ws/stock", async (HttpContext context, StockNotifier stock) =>
            {
                if (!context.WebSockets.IsWebSocketRequest)
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    return;
                }
                using var socket = await context.WebSockets.AcceptWebSocketAsync();
                await stock.HandleWebSocketAsync(socket, context.RequestAborted);
            });

            // C22: MCP-сервер (Streamable HTTP). Доступ лише з API-ключем або для адміна
            app.MapMcp("/mcp").RequireAuthorization("ApiWrite");

            // C2/C3: перевірка живучості для хмари та CI/CD
            app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

            app.MapControllerRoute(
                name: "default",
                pattern: "{controller=Home}/{action=Index}/{id?}")
                .WithStaticAssets();

            using (var scope = app.Services.CreateScope())
            {
                var services = scope.ServiceProvider;
                try
                {
                    var context = services.GetRequiredService<ApplicationDbContext>();
                    var config = services.GetRequiredService<IConfiguration>();

                    DbInitializer.SeedDataAsync(context, config).Wait();
                }
                catch (Exception ex)
                {
                    var logger = services.GetRequiredService<ILogger<Program>>();
                    logger.LogError(ex, "Сталася помилка під час заповнення бази даних.");
                }
            }

            app.Run();
        }

        private static async Task ValidatePrincipalAsync(CookieValidatePrincipalContext ctx)
        {
            const string CheckedKey = "bv_checked";
            if (ctx.Properties.Items.TryGetValue(CheckedKey, out var raw) &&
                DateTimeOffset.TryParse(raw, out var lastCheck) && DateTimeOffset.UtcNow - lastCheck < TimeSpan.FromMinutes(5))
            {
                return;
            }

            var email = ctx.Principal?.Identity?.Name;
            if (string.IsNullOrEmpty(email)) return;

            var db = ctx.HttpContext.RequestServices.GetRequiredService<ApplicationDbContext>();
            var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Email == email);
            bool claimAdmin = ctx.Principal!.HasClaim("IsAdmin", "true");
            if (user == null || user.IsAdmin != claimAdmin)
            {
                // Користувача видалили або змінили права - змушуємо увійти заново з актуальними claims
                ctx.RejectPrincipal();
                await ctx.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                return;
            }

            ctx.Properties.Items[CheckedKey] = DateTimeOffset.UtcNow.ToString("O");
            ctx.ShouldRenew = true;
        }

        private static Task RedirectOrStatus(RedirectContext<CookieAuthenticationOptions> ctx, int statusCode)
        {
            var path = ctx.Request.Path;
            if (path.StartsWithSegments("/api") || path.StartsWithSegments("/mcp"))
            {
                ctx.Response.StatusCode = statusCode;
                return Task.CompletedTask;
            }
            ctx.Response.Redirect(ctx.RedirectUri);
            return Task.CompletedTask;
        }
    }
}
