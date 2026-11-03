using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Playwright;

namespace BandVault.E2E
{
    // ============================================
    // QA2: END-TO-END СЦЕНАРІЇ (Playwright)
    // Покривають C1 (CRUD мерчу), C6/C7 (лендінг), F2 (autocomplete), C16/B6 (доступи),
    // C12/F3 (редактор), C13/C14 (синхронізація), C10 (залишки наживо), кошик/замовлення + B8 (інвойс)
    // ============================================
    [Collection("e2e")]
    public class BandVaultE2ETests
    {
        private const string ApiKey = "dev-bandvault-api-key"; // з appsettings.Development.json
        private readonly E2EFixture _fx;

        public BandVaultE2ETests(E2EFixture fx)
        {
            _fx = fx;
        }

        // Тестові дані створюємо через REST API (C5) - швидко і незалежно від UI
        private async Task<int> CreateMerchViaApiAsync(string title, int stock = 5, decimal price = 30)
        {
            using var http = new HttpClient { BaseAddress = new Uri(_fx.BaseUrl) };
            http.DefaultRequestHeaders.Add("X-Api-Key", ApiKey);
            var response = await http.PostAsJsonAsync("/api/merch", new
            {
                title,
                description = "E2E test item",
                price,
                stockQuantity = stock,
                categoryId = 1
            });
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            return json.GetProperty("id").GetInt32();
        }

        [Fact]
        public async Task Landing_ShowsBandHero_ContactsAndSeoTags()
        {
            var page = await _fx.NewPageAsync();
            await page.GotoAsync("/");

            await Assertions.Expect(page.Locator(".hero-content h1")).ToContainTextAsync("MADMANS ESPRIT");
            await Assertions.Expect(page.Locator("a:has-text('УВІЙТИ')")).ToBeVisibleAsync();
            await Assertions.Expect(page.Locator("meta[property='og:title']")).ToHaveCountAsync(1);
            await Assertions.Expect(page.Locator("#join a:has-text('TELEGRAM')")).ToBeVisibleAsync();
        }

        [Fact]
        public async Task PlayerVisualizer_SettingsPanel_AppliesAndRemembersChoice()
        {
            var page = await _fx.NewPageAsync();
            await page.SetViewportSizeAsync(1400, 900); // візуалізатор показується на широких екранах
            await page.GotoAsync("/Merch");

            await page.ClickAsync("#vizSettingsBtn");
            var panel = page.Locator("#vizPanel");
            await Assertions.Expect(panel).ToBeVisibleAsync();

            // F6: пресет змінює стиль і кольори, а вибір зберігається між перезавантаженнями
            await panel.Locator("[data-preset='neon']").ClickAsync();
            await Assertions.Expect(panel.Locator("[data-viz-group='style'] [data-value='mirror']")).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("active"));
            await panel.Locator("[data-viz-group='colorMode'] [data-value='rainbow']").ClickAsync();

            await page.Keyboard.PressAsync("Escape");
            await Assertions.Expect(panel).ToBeHiddenAsync();

            await page.ReloadAsync();
            await page.ClickAsync("#visualizer");
            await Assertions.Expect(page.Locator("#vizPanel [data-viz-group='colorMode'] [data-value='rainbow']")).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("active"));
            await Assertions.Expect(page.Locator("#vizPanel [data-viz-group='style'] [data-value='mirror']")).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("active"));
        }

        [Fact]
        public async Task Assistant_StreamsThinkingToolCallAndAnswer()
        {
            await CreateMerchViaApiAsync("E2E Assistant Hoodie");

            var page = await _fx.NewPageAsync(E2EFixture.FanEmail);
            await page.GotoAsync("/Assistant");
            await page.FillAsync("#chatInput", "Які є худі?");
            await page.ClickAsync("#sendBtn");

            // C21: у чаті видно виклик інструменту з результатом із БД, думки моделі та відповідь
            var toolStep = page.Locator(".msg-step", new() { HasText = "list_merch" });
            await Assertions.Expect(toolStep).ToContainTextAsync("✓ Інструмент: list_merch");
            await Assertions.Expect(page.Locator(".msg-step", new() { HasText = "Перевіряю каталог" })).ToHaveCountAsync(1);
            await Assertions.Expect(page.Locator(".msg-bot").Last).ToContainTextAsync("У наявності є E2E Assistant Hoodie.");
            await Assertions.Expect(page.Locator(".msg-bot strong")).ToHaveTextAsync("E2E Assistant Hoodie");
            // Тег <think> не потрапив у текст відповіді
            await Assertions.Expect(page.Locator(".msg-bot").Last).Not.ToContainTextAsync("think");
        }

        [Fact]
        public async Task Player_KeepsPlaying_WhileNavigatingAllSections()
        {
            var page = await _fx.NewPageAsync(E2EFixture.AdminEmail);
            await page.SetViewportSizeAsync(1600, 900);
            await page.GotoAsync("/");

            // 30 с тиші (WAV), щоб не залежати від завантажених треків
            await page.EvaluateAsync(@"() => {
                const rate = 8000, seconds = 30, n = rate * seconds;
                const buf = new ArrayBuffer(44 + n); const v = new DataView(buf);
                const w = (o, s) => [...s].forEach((c, i) => v.setUint8(o + i, c.charCodeAt(0)));
                w(0, 'RIFF'); v.setUint32(4, 36 + n, true); w(8, 'WAVEfmt '); v.setUint32(16, 16, true);
                v.setUint16(20, 1, true); v.setUint16(22, 1, true); v.setUint32(24, rate, true); v.setUint32(28, rate, true);
                v.setUint16(32, 1, true); v.setUint16(34, 8, true); w(36, 'data'); v.setUint32(40, n, true);
                for (let i = 0; i < n; i++) v.setUint8(44 + i, 128);
                const url = URL.createObjectURL(new Blob([buf], { type: 'audio/wav' }));
                window.playTrack(url, 'E2E тиша', 'BandVault', null, null, null);
                window.__noReloadMarker = 'alive';
            }");
            await page.WaitForFunctionAsync("() => !document.getElementById('main-audio').paused");

            // Кожен розділ меню відкривається без перезавантаження: плеєр той самий і грає далі
            foreach (var link in new[] { "Наживо", "Стіна", "Асистент", "Мапа", "Settings", "Кошик", "Мерч", "Стрічка", "Каталог релізів" })
            {
                await page.Locator("nav").GetByText(link, new() { Exact = true }).First.ClickAsync();
                await page.WaitForLoadStateAsync(Microsoft.Playwright.LoadState.NetworkIdle);
                Assert.Equal("alive", await page.EvaluateAsync<string>("() => window.__noReloadMarker"));
                Assert.False(await page.EvaluateAsync<bool>("() => document.getElementById('main-audio').paused"), $"Музика зупинилась на розділі «{link}»");
            }

            // Сторінки, що відкрились через Turbo, працюють: мапа намальована, графік є, редактор готовий
            await page.Locator("nav").GetByText("Мапа", new() { Exact = true }).ClickAsync();
            await Assertions.Expect(page.Locator("#adminMap .leaflet-tile-pane")).ToHaveCountAsync(1);
            await page.Locator("nav").GetByText("Мапа", new() { Exact = true }).ClickAsync(); // повторний візит без помилок const
            await Assertions.Expect(page.Locator("#adminMap .leaflet-tile-pane")).ToHaveCountAsync(1);
            await page.Locator("nav").GetByText("Settings", new() { Exact = true }).ClickAsync();
            await page.WaitForFunctionAsync("() => !!Chart.getChart(document.getElementById('ordersChart'))");
            await page.Locator("nav").GetByText("Стрічка", new() { Exact = true }).ClickAsync();
            await page.GetByText("+ Написати пост").ClickAsync();
            await Assertions.Expect(page.Locator(".block-item .CodeMirror")).ToHaveCountAsync(1);
            Assert.False(await page.EvaluateAsync<bool>("() => document.getElementById('main-audio').paused"));

            // Закриваємо: відкритий редактор нового поста синхронізував би свою чернетку з іншими тестами (C13)
            await page.Context.CloseAsync();
        }

        [Fact]
        public async Task LoginPage_OffersOnlyGoogle()
        {
            var page = await _fx.NewPageAsync();
            await page.GotoAsync("/Auth/Login");
            await Assertions.Expect(page.Locator("button:has-text('УВІЙТИ ЧЕРЕЗ GOOGLE')")).ToBeVisibleAsync();
            await Assertions.Expect(page.Locator("input[name=email]")).ToHaveCountAsync(0);
        }

        [Fact]
        public async Task Anonymous_IsRedirectedToLogin_ForClosedSections()
        {
            var page = await _fx.NewPageAsync();
            await page.GotoAsync("/Releases");
            Assert.Contains("/Auth/Login", page.Url);
        }

        [Fact]
        public async Task Fan_CannotOpenAdminForms()
        {
            var page = await _fx.NewPageAsync(E2EFixture.FanEmail);
            await page.GotoAsync("/Merch/Create");
            Assert.Contains("/Auth/AccessDenied", page.Url);
            await Assertions.Expect(page.Locator("a:has-text('Мапа')")).ToHaveCountAsync(0);
        }

        [Fact]
        public async Task Admin_CreatesMerchItem_WithCategoryAutocomplete()
        {
            var page = await _fx.NewPageAsync(E2EFixture.AdminEmail);
            await page.GotoAsync("/Merch/Create");

            var title = "E2E Худі " + Guid.NewGuid().ToString("N")[..6];
            await page.FillAsync("#Title", title);
            await page.FillAsync("#Description", "Чорне худі з логотипом");
            await page.FillAsync("#Price", "45");
            await page.FillAsync("#StockQuantity", "7");

            // F2: Select2 вантажить категорії з бекенду лише після 3 символів
            await page.ClickAsync(".select2-selection");
            await page.FillAsync(".select2-search__field", "Одя");
            await page.ClickAsync(".select2-results__option:has-text('Одяг')");

            await page.ClickAsync("button:has-text('ЗБЕРЕГТИ')");
            await page.WaitForURLAsync("**/Merch");

            var card = page.Locator(".card", new() { HasText = title });
            await Assertions.Expect(card).ToBeVisibleAsync();
            await Assertions.Expect(card).ToContainTextAsync("Одяг");
            await Assertions.Expect(card).ToContainTextAsync("7 ШТ.");
        }

        [Fact]
        public async Task MerchSearch_Autocomplete_OpensDetails()
        {
            var title = "Kimono " + Guid.NewGuid().ToString("N")[..6];
            await CreateMerchViaApiAsync(title);

            var page = await _fx.NewPageAsync();
            await page.GotoAsync("/Merch");
            await page.Locator("#merchSearchInput").PressSequentiallyAsync(title[..9], new() { Delay = 40 });

            var result = page.Locator("#searchResults .search-result-item", new() { HasText = title });
            await Assertions.Expect(result).ToBeVisibleAsync();
            await result.DispatchEventAsync("mousedown");

            await page.WaitForURLAsync("**/Merch/Details/**");
            await Assertions.Expect(page.Locator("h2")).ToHaveTextAsync(title.ToUpperInvariant(), new() { IgnoreCase = true });
        }

        [Fact]
        public async Task Fan_PlacesOrder_AndInvoiceIsGeneratedInBackground()
        {
            var title = "Poster " + Guid.NewGuid().ToString("N")[..6];
            var id = await CreateMerchViaApiAsync(title, stock: 3);

            var page = await _fx.NewPageAsync(E2EFixture.FanEmail);
            await page.GotoAsync($"/Merch/Details/{id}");
            await page.ClickAsync("button:has-text('У КОШИК')");
            await page.WaitForURLAsync("**/Cart");
            await Assertions.Expect(page.Locator("nav .badge")).ToHaveTextAsync("1");

            await page.GotoAsync("/Orders/Checkout");
            await page.FillAsync("input[name=customerPhone]", "+380501234567");
            await page.FillAsync("#addressInput", "Київ, Хрещатик 1");
            // Точка доставки кліком по мапі (без зовнішнього геокодера)
            await page.Locator("#map").ClickAsync(new() { Position = new() { X = 150, Y = 150 } });
            await page.ClickAsync("#submitOrderBtn");

            await Assertions.Expect(page.Locator("h1:has-text('ЗАМОВЛЕННЯ ПРИЙНЯТО')")).ToBeVisibleAsync();
            // B8: інвойс генерується фоновим обробником черги
            await Assertions.Expect(page.Locator("a:has-text('ЗАВАНТАЖИТИ РАХУНОК')")).ToBeVisibleAsync(new() { Timeout = 30000 });

            // C8: замовлення з'являється на мапі адміністратора, фільтр за містом і способом оплати працює
            var admin = await _fx.NewPageAsync(E2EFixture.AdminEmail);
            await admin.GotoAsync("/Orders/AdminMap");
            await Assertions.Expect(admin.Locator(".order-card-item", new() { HasText = "Хрещатик 1" })).ToBeVisibleAsync();
            await admin.SelectOptionAsync("#cityFilter", "Київ");
            await admin.SelectOptionAsync("#paymentFilter", "Готівкою кур'єру");
            await Assertions.Expect(admin.Locator(".order-card-item", new() { HasText = "Хрещатик 1" })).ToBeVisibleAsync();
            await Assertions.Expect(admin.Locator(".leaflet-marker-icon").First).ToBeVisibleAsync();
        }

        [Fact]
        public async Task LiveStock_WebSocket_ReceivesUpdateAfterSale()
        {
            var title = "Limited Vinyl " + Guid.NewGuid().ToString("N")[..6];
            var id = await CreateMerchViaApiAsync(title, stock: 10);

            var page = await _fx.NewPageAsync(E2EFixture.AdminEmail);
            await page.GotoAsync("/Merch/Live");
            var row = page.Locator($"tr[data-id='{id}']");
            await Assertions.Expect(row).ToContainTextAsync("10 ШТ.");

            // Зміна залишку через API має прийти по WebSocket без перезавантаження
            using var http = new HttpClient { BaseAddress = new Uri(_fx.BaseUrl) };
            http.DefaultRequestHeaders.Add("X-Api-Key", ApiKey);
            var put = await http.PutAsJsonAsync($"/api/merch/{id}", new { id, title, description = "E2E test item", price = 30, stockQuantity = 9, categoryId = 1 });
            put.EnsureSuccessStatusCode();

            await Assertions.Expect(row).ToContainTextAsync("9 ШТ.");
            await Assertions.Expect(page.Locator("#mUpdates")).ToHaveTextAsync("1");
        }

        [Fact]
        public async Task PostEditor_RestoresDraftAfterReload_AndSortsBlocks()
        {
            var page = await _fx.NewPageAsync(E2EFixture.AdminEmail);
            await page.GotoAsync("/Posts/Create");
            await page.EvaluateAsync("localStorage.clear()");
            await page.ReloadAsync();

            await page.Locator(".block-item .CodeMirror").First.ClickAsync();
            await page.Keyboard.TypeAsync("Перший блок");
            await page.ClickAsync("button:has-text('+ Текст')");
            await page.Locator(".block-item .CodeMirror").Nth(1).ClickAsync();
            await page.Keyboard.TypeAsync("Другий блок");

            // F3: перетягуємо другий блок на місце першого
            await page.Locator(".drag-handle").Nth(1).DragToAsync(page.Locator(".drag-handle").First);
            await page.ReloadAsync();

            // C12: чернетка відновлена з localStorage у новому порядку
            var blocks = page.Locator(".block-item .CodeMirror");
            await Assertions.Expect(blocks).ToHaveCountAsync(2);
            await Assertions.Expect(blocks.First).ToContainTextAsync("Другий блок");
            await Assertions.Expect(blocks.Nth(1)).ToContainTextAsync("Перший блок");
        }

        [Fact]
        public async Task PostEditor_SyncsChangesAndPresenceBetweenTwoDevices()
        {
            var deviceA = await _fx.NewPageAsync(E2EFixture.AdminEmail);
            var deviceB = await _fx.NewPageAsync(E2EFixture.AdminEmail);

            // Окремий документ (редагування існуючого поста), щоб не перетинатися з іншими тестами
            await deviceA.GotoAsync("/Posts/Create");
            await deviceA.FillAsync("#Title", "Sync test");
            await deviceA.Locator(".block-item .CodeMirror").First.ClickAsync();
            await deviceA.Keyboard.TypeAsync("Початковий текст");
            await deviceA.ClickAsync("button:has-text('ОПУБЛІКУВАТИ')");
            await deviceA.WaitForURLAsync("**/Posts");
            await deviceA.ClickAsync("a:has-text('Редагувати')");
            await deviceA.WaitForURLAsync("**/Posts/Edit/**");
            var editUrl = deviceA.Url;

            await deviceB.GotoAsync(editUrl);
            await Assertions.Expect(deviceA.Locator("#collabStatus")).ToContainTextAsync("РАЗОМ З ВАМИ");

            // C13: зміна на пристрої A з'являється на пристрої B
            await deviceA.Locator(".block-item .CodeMirror").First.ClickAsync();
            await deviceA.Keyboard.PressAsync("End");
            await deviceA.Keyboard.TypeAsync(" + додано з пристрою A");
            await Assertions.Expect(deviceB.Locator(".block-item .CodeMirror").First).ToContainTextAsync("додано з пристрою A");

            // C14: блок, який редагує A, підсвічений на B
            await Assertions.Expect(deviceB.Locator(".block-item.peer-editing")).ToHaveCountAsync(1);
            await Assertions.Expect(deviceB.Locator(".peer-label").First).ToContainTextAsync("редагує");
        }
    }
}
