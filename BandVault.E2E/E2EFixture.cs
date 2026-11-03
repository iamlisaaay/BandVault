using System.Net;
using System.Net.Sockets;
using BandVault.Web.Data;
using BandVault.Web.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace BandVault.E2E
{
    // ============================================
    // QA2: середовище E2E-тестів
    // Піднімає справжній Kestrel-сервер застосунку на окремій базі PostgreSQL (bandvault_e2e)
    // та браузер через Playwright. Redis/RabbitMQ вимкнені - застосунок працює на запасних варіантах.
    //   E2E_DB          - рядок підключення (за замовчуванням docker-compose Postgres на 5433)
    //   E2E_BROWSER     - msedge | chrome | chromium (за замовчуванням msedge, вбудований у Windows)
    //   E2E_HEADED=1    - показати вікно браузера
    // ============================================
    public class E2EFixture : IAsyncLifetime
    {
        public const string AdminEmail = "e2e-admin@knu.ua";
        public const string FanEmail = "e2e-fan@knu.ua";

        private WebApplicationFactory<BandVault.Web.Program>? _factory;
        private IPlaywright? _playwright;

        public IBrowser Browser { get; private set; } = null!;
        public string BaseUrl { get; private set; } = "";

        public async Task InitializeAsync()
        {
            var connectionString = Environment.GetEnvironmentVariable("E2E_DB")
                ?? "Host=localhost;Port=5433;Database=bandvault_e2e;Username=bandvault_user;Password=SecretPassword123!";

            // Кожен прогін - з чистої бази
            using (var context = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connectionString).Options))
            {
                await context.Database.EnsureDeletedAsync();
            }

            _factory = new WebApplicationFactory<BandVault.Web.Program>().WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                builder.UseSetting("ConnectionStrings:DefaultConnection", connectionString);
                builder.UseSetting("ConnectionStrings:Redis", "");
                builder.UseSetting("ConnectionStrings:RabbitMQ", "");
                builder.UseSetting("Storage:Provider", "Local");
                builder.UseSetting("AdminSettings:SuperAdminEmail", AdminEmail);
                builder.UseSetting("TelegramBot:Token", "");
                builder.UseSetting("Assistant:ApiKey", "");
                // Локальний appsettings.Local.json містить справжні ключі - у тестах їх перекриваємо,
                // щоб тест замовлення не надсилав повідомлень у справжній Telegram-канал
                builder.ConfigureAppConfiguration(config => config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["TelegramBot:Token"] = "",
                    ["Assistant:ApiKey"] = "",
                    ["Authentication:Google:ClientId"] = "e2e-client",
                    ["Authentication:Google:ClientSecret"] = "e2e-secret"
                }));
                // Google OAuth у тестах недоступний - вхід через маршрут, що існує ЛИШЕ в тестовому хості
                builder.ConfigureTestServices(services => services.AddTransient<IStartupFilter, E2ELoginStartupFilter>());
            });
            _factory.UseKestrel(GetFreePort());
            _factory.StartServer();
            BaseUrl = _factory.ClientOptions.BaseAddress.ToString().TrimEnd('/');
            Console.WriteLine($"E2E server: {BaseUrl}");

            // Запобіжник: справжній токен Telegram не повинен потрапити в тестовий сервер
            var telegramToken = _factory.Services.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>()["TelegramBot:Token"];
            if (!string.IsNullOrEmpty(telegramToken))
                throw new InvalidOperationException("E2E: тестовий сервер підхопив справжній токен Telegram - тести зупинено");

            // C21: асистент у тестах говорить з імітацією провайдера на цьому ж сервері
            var assistant = _factory.Services.GetRequiredService<BandVault.Web.Services.Assistant.AssistantOptions>();
            assistant.BaseUrl = BaseUrl + "/__fake-llm";
            assistant.Model = "fake-model";

            _playwright = await Playwright.CreateAsync();
            var channel = Environment.GetEnvironmentVariable("E2E_BROWSER") ?? "msedge";
            Browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Channel = channel == "chromium" ? null : channel,
                Headless = Environment.GetEnvironmentVariable("E2E_HEADED") != "1"
            });
        }

        public async Task<IPage> NewPageAsync(string? loginEmail = null)
        {
            var context = await Browser.NewContextAsync(new BrowserNewContextOptions { BaseURL = BaseUrl, Locale = "uk-UA" });
            context.SetDefaultTimeout(15000);
            var page = await context.NewPageAsync();
            if (loginEmail != null) await LoginAsync(page, loginEmail);
            return page;
        }

        // Аналог успішного повернення з Google: той самий UserSignInService, що й у реальному вході
        public static async Task LoginAsync(IPage page, string email)
        {
            await page.GotoAsync($"/__e2e/login?email={Uri.EscapeDataString(email)}");
            await page.WaitForURLAsync(url => !url.Contains("/__e2e/"));
        }

        public async Task DisposeAsync()
        {
            if (Browser != null) await Browser.CloseAsync();
            _playwright?.Dispose();
            if (_factory != null) await _factory.DisposeAsync();
        }

        private static int GetFreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }
    }

    // Додається до конвеєра лише через ConfigureTestServices у фікстурі - у самому застосунку цього маршруту немає
    public class E2ELoginStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            // Не app.Map: він переносить шлях у PathBase, і cookie отримала б Path=/__e2e/login
            app.Use(async (context, nextMiddleware) =>
            {
                if (context.Request.Path != "/__e2e/login")
                {
                    await nextMiddleware();
                    return;
                }
                var email = context.Request.Query["email"].ToString();
                var signIn = context.RequestServices.GetRequiredService<UserSignInService>();
                await signIn.SignInAsync(context, email, null, null);
                context.Response.Redirect("/");
            });

            // Імітація OpenAI-сумісного провайдера (як Ollama): спершу стрімить виклик інструменту частинами,
            // після отримання результату - відповідь із блоком <think>, розірваним між фрагментами
            app.Map("/__fake-llm/chat/completions", branch => branch.Run(async context =>
            {
                var body = await new StreamReader(context.Request.Body).ReadToEndAsync();
                bool hasToolResult = body.Contains("\"role\":\"tool\"");
                context.Response.ContentType = "text/event-stream";

                string[] chunks = hasToolResult
                    ? new[]
                    {
                        "{\"choices\":[{\"delta\":{\"content\":\"<thi\"}}]}",
                        "{\"choices\":[{\"delta\":{\"content\":\"nk>Перевіряю каталог</think>У наявності є \"}}]}",
                        "{\"choices\":[{\"delta\":{\"content\":\"**E2E Assistant Hoodie**.\"},\"finish_reason\":\"stop\"}]}"
                    }
                    : new[]
                    {
                        "{\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"id\":\"call_1\",\"type\":\"function\",\"function\":{\"name\":\"list_merch\",\"arguments\":\"{\\\"que\"}}]}}]}",
                        "{\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"function\":{\"arguments\":\"ry\\\":\\\"hoodie\\\"}\"}}]},\"finish_reason\":\"tool_calls\"}]}"
                    };

                foreach (var chunk in chunks)
                {
                    await context.Response.WriteAsync($"data: {chunk}\n\n");
                    await context.Response.Body.FlushAsync();
                }
                await context.Response.WriteAsync("data: [DONE]\n\n");
            }));
            next(app);
        };
    }

    [CollectionDefinition("e2e")]
    public class E2ECollection : ICollectionFixture<E2EFixture> { }
}
