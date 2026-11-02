using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using BandVault.Web.Services.Assistant;
using BandVault.Web.Services.Catalog;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BandVault.Web.Controllers
{
    // ============================================
    // C21: ШІ-АСИСТЕНТ ФАН-КЛУБУ (безкоштовний провайдер: Ollama локально / Gemini / Groq)
    // Відповідь стрімиться на front-end через Server-Sent Events (text/event-stream):
    //   thinking    - "думки" моделі (якщо модель їх віддає)
    //   tool_call   - виклик інструменту каталогу (ті самі, що й у MCP-сервері C22)
    //   tool_result - результат інструменту
    //   text        - фрагменти відповіді
    //   done/error
    // ============================================
    [Authorize]
    public class AssistantController : Controller
    {
        private const int MaxToolRounds = 6;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        private readonly CatalogTools _tools;
        private readonly ChatCompletionsClient _chat;
        private readonly AssistantOptions _options;
        private readonly ILogger<AssistantController> _logger;

        public AssistantController(CatalogTools tools, ChatCompletionsClient chat, AssistantOptions options, ILogger<AssistantController> logger)
        {
            _tools = tools;
            _chat = chat;
            _options = options;
            _logger = logger;
        }

        public class ChatTurn
        {
            public string Role { get; set; } = "user";
            public string Text { get; set; } = string.Empty;
        }

        public class ChatRequest
        {
            public List<ChatTurn> History { get; set; } = new();
        }

        public IActionResult Index()
        {
            ViewBag.Model = _options.Model;
            ViewBag.Provider = _options.BaseUrl;
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task Stream([FromBody] ChatRequest request)
        {
            Response.ContentType = "text/event-stream";
            Response.Headers.CacheControl = "no-cache";
            Response.Headers["X-Accel-Buffering"] = "no"; // nginx/проксі не повинні буферизувати потік

            var ct = HttpContext.RequestAborted;

            var history = request.History
                .Where(t => !string.IsNullOrWhiteSpace(t.Text))
                .TakeLast(20)
                .ToList();
            if (history.Count == 0 || history[^1].Role != "user")
            {
                await SendEventAsync("error", new { message = "Порожнє повідомлення" }, ct);
                return;
            }

            var messages = new JsonArray { new JsonObject { ["role"] = "system", ["content"] = SystemPrompt } };
            foreach (var turn in history)
            {
                messages.Add(new JsonObject
                {
                    ["role"] = turn.Role == "assistant" ? "assistant" : "user",
                    ["content"] = turn.Text.Length > 4000 ? turn.Text[..4000] : turn.Text
                });
            }

            try
            {
                for (int round = 0; round < MaxToolRounds; round++)
                {
                    var result = await _chat.StreamTurnAsync(messages, ToolDefinitions, async ev =>
                    {
                        switch (ev)
                        {
                            case TextDelta t: await SendEventAsync("text", new { text = t.Text }, ct); break;
                            case ThinkingDelta th: await SendEventAsync("thinking", new { text = th.Text }, ct); break;
                        }
                    }, ct);

                    if (result.ToolCalls.Count == 0) break;

                    // Повертаємо моделі її виклики та результати інструментів у форматі Chat Completions
                    messages.Add(new JsonObject
                    {
                        ["role"] = "assistant",
                        ["content"] = result.Text,
                        ["tool_calls"] = new JsonArray(result.ToolCalls.Select(c => (JsonNode)new JsonObject
                        {
                            ["id"] = c.Id,
                            ["type"] = "function",
                            ["function"] = new JsonObject { ["name"] = c.Name, ["arguments"] = c.ArgumentsJson }
                        }).ToArray())
                    });

                    foreach (var call in result.ToolCalls)
                    {
                        await SendEventAsync("tool_call", new { id = call.Id, name = call.Name, input = ParseInput(call.ArgumentsJson) }, ct);
                        string output = await ExecuteToolAsync(call.Name, call.ArgumentsJson);
                        bool isError = output.StartsWith("{\"error\"");
                        await SendEventAsync("tool_result", new { id = call.Id, name = call.Name, result = output, isError }, ct);
                        messages.Add(new JsonObject { ["role"] = "tool", ["tool_call_id"] = call.Id, ["content"] = output });
                    }
                }

                await SendEventAsync("done", new { }, ct);
            }
            catch (OperationCanceledException)
            {
                // Користувач закрив сторінку
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "Провайдер ШІ недоступний");
                await SendEventAsync("error", new { message = $"Не вдалося під'єднатися до моделі ({_options.BaseUrl}). Якщо це Ollama - запустіть її та завантажте модель: ollama pull {_options.Model}" }, CancellationToken.None);
            }
            catch (ChatProviderException ex)
            {
                _logger.LogWarning("Помилка провайдера ШІ: {Message}", ex.Message);
                await SendEventAsync("error", new { message = ex.Message }, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Помилка ШІ-асистента");
                await SendEventAsync("error", new { message = "Сервіс ШІ тимчасово недоступний. Спробуйте пізніше." }, CancellationToken.None);
            }
        }

        private static Dictionary<string, JsonElement> ParseInput(string json)
        {
            try
            {
                return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json) ?? new();
            }
            catch (JsonException)
            {
                return new();
            }
        }

        // Валідуємо аргументи перед виконанням: невеликі моделі інколи передають числа рядками або неповний JSON
        private async Task<string> ExecuteToolAsync(string name, string inputJson)
        {
            var input = ParseInput(inputJson);
            string? Str(string key) => input.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            decimal? Dec(string key)
            {
                if (!input.TryGetValue(key, out var v)) return null;
                if (v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var d)) return d;
                if (v.ValueKind == JsonValueKind.String && decimal.TryParse(v.GetString()?.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var s)) return s;
                return null;
            }
            int? Int(string key) => Dec(key) is decimal d && d == Math.Floor(d) ? (int)d : null;

            object result = name switch
            {
                "list_merch" => await _tools.ListMerchAsync(Str("query"), Int("limit") ?? 20),
                "get_merch_item" => Int("id") is int id ? await _tools.GetMerchItemAsync(id) : new { error = "Потрібен параметр id" },
                "list_releases" => await _tools.ListReleasesAsync(),
                "search_catalog" => Str("query") is string q ? await _tools.SearchCatalogAsync(q, Str("type")) : new { error = "Потрібен параметр query" },
                "create_merch_item" => !User.HasClaim("IsAdmin", "true")
                    ? new { error = "Додавати товари може лише адміністратор фан-клубу" }
                    : (Str("title"), Str("description"), Dec("price"), Int("stock"), Str("category")) is (string t, string d, decimal p, int s, string c)
                        ? await _tools.CreateMerchItemAsync(t, d, p, s, c, Dec("fanPrice"))
                        : new { error = "Потрібні параметри title, description, price, stock, category" },
                _ => new { error = $"Невідомий інструмент {name}" }
            };
            return JsonSerializer.Serialize(result, JsonOptions);
        }

        private async Task SendEventAsync(string type, object data, CancellationToken ct)
        {
            await Response.WriteAsync($"event: {type}\ndata: {JsonSerializer.Serialize(data, JsonOptions)}\n\n", ct);
            await Response.Body.FlushAsync(ct);
        }

        private string SystemPrompt =>
            "Ти - асистент офіційного фан-клубу музичного гурту на платформі BandVault. " +
            "Відповідай українською, коротко і дружньо. Для питань про мерч, ціни, наявність, релізи та треки " +
            "обов'язково викликай інструменти каталогу, а не вигадуй дані. Посилання на сторінки сайту давай у форматі markdown, " +
            "наприклад [Худі](/Merch/Details/1). " +
            (User.HasClaim("IsAdmin", "true")
                ? "Користувач - адміністратор: він може просити додати товар (create_merch_item); перед створенням коротко підсумуй, що саме додаєш."
                : "Користувач - фанат, додавати товари він не може.");

        // Опис інструментів у форматі Chat Completions (JSON Schema параметрів)
        private static JsonArray ToolDefinitions => new()
        {
            Tool("list_merch", "Список товарів мерчу з цінами та залишками. Можна фільтрувати за частиною назви.",
                new { query = new { type = "string", description = "Частина назви товару" }, limit = new { type = "integer", description = "1-50" } }),
            Tool("get_merch_item", "Повна інформація про товар мерчу за id.",
                new { id = new { type = "integer", description = "Ідентифікатор товару" } }, "id"),
            Tool("list_releases", "Дискографія гурту: релізи з жанром, датою та треклістом.", new { }),
            Tool("search_catalog", "Повнотекстовий пошук по мерчу, релізах і треках.",
                new { query = new { type = "string" }, type = new { type = "string", @enum = new[] { "merch", "release", "track" } } }, "query"),
            Tool("create_merch_item", "Додає новий товар у каталог мерчу (лише для адміністратора). Категорія - назва, напр. 'Одяг' чи 'Аксесуари'.",
                new
                {
                    title = new { type = "string" },
                    description = new { type = "string" },
                    price = new { type = "number" },
                    stock = new { type = "integer" },
                    category = new { type = "string" },
                    fanPrice = new { type = "number" }
                }, "title", "description", "price", "stock", "category"),
        };

        private static JsonObject Tool(string name, string description, object properties, params string[] required) => new()
        {
            ["type"] = "function",
            ["function"] = new JsonObject
            {
                ["name"] = name,
                ["description"] = description,
                ["parameters"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = JsonSerializer.SerializeToNode(properties),
                    ["required"] = new JsonArray(required.Select(r => (JsonNode)JsonValue.Create(r)!).ToArray())
                }
            }
        };
    }
}
