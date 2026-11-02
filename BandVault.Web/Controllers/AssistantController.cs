using System.Text.Json;
using Anthropic;
using Anthropic.Models.Beta;
using Anthropic.Models.Beta.Messages;
using BandVault.Web.Services.Catalog;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BandVault.Web.Controllers
{
    // ============================================
    // C21: ШІ-АСИСТЕНТ ФАН-КЛУБУ
    // Відповідь стрімиться на front-end через Server-Sent Events (text/event-stream):
    //   thinking   - короткі "думки" моделі
    //   tool_call  - виклик інструменту каталогу (ті самі, що й у MCP-сервері C22)
    //   tool_result- результат інструменту
    //   text       - фрагменти відповіді
    //   done/error
    // ============================================
    [Authorize]
    public class AssistantController : Controller
    {
        private const string Model = "claude-opus-5-5";
        private const int MaxToolRounds = 6;

        private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

        private readonly CatalogTools _tools;
        private readonly IConfiguration _config;
        private readonly ILogger<AssistantController> _logger;

        public AssistantController(CatalogTools tools, IConfiguration config, ILogger<AssistantController> logger)
        {
            _tools = tools;
            _config = config;
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
            ViewBag.Configured = !string.IsNullOrWhiteSpace(ApiKey);
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

            if (string.IsNullOrWhiteSpace(ApiKey))
            {
                await SendEventAsync("error", new { message = "Асистент не налаштований: задайте Anthropic:ApiKey (user-secrets) або змінну ANTHROPIC_API_KEY." }, ct);
                return;
            }

            var history = request.History
                .Where(t => !string.IsNullOrWhiteSpace(t.Text))
                .TakeLast(20)
                .ToList();
            if (history.Count == 0 || history[^1].Role != "user")
            {
                await SendEventAsync("error", new { message = "Порожнє повідомлення" }, ct);
                return;
            }

            var client = new AnthropicClient { ApiKey = ApiKey };
            var messages = history.Select(t =>
            {
                var text = t.Text.Length > 4000 ? t.Text[..4000] : t.Text;
                return t.Role == "assistant"
                    ? (BetaMessageParam)new BetaAssistantMessageParam { Content = text }
                    : new BetaUserMessageParam { Content = text };
            }).ToList();

            try
            {
                for (int round = 0; round < MaxToolRounds; round++)
                {
                    var turn = await StreamTurnAsync(client, messages, ct);

                    if (turn.StopReason == "refusal")
                    {
                        await SendEventAsync("error", new { message = "Асистент не може відповісти на цей запит." }, ct);
                        return;
                    }

                    messages.Add(new BetaAssistantMessageParam { Content = turn.AssistantContent });
                    if (turn.StopReason != "tool_use" || turn.ToolCalls.Count == 0) break;

                    // Виконуємо всі виклики інструментів і повертаємо результати одним повідомленням
                    var results = new List<BetaContentBlockParam>();
                    foreach (var call in turn.ToolCalls)
                    {
                        string result = await ExecuteToolAsync(call.Name, call.InputJson);
                        bool isError = result.StartsWith("{\"error\"");
                        await SendEventAsync("tool_result", new { id = call.Id, name = call.Name, result, isError }, ct);
                        results.Add(new BetaToolResultBlockParam { ToolUseID = call.Id, Content = result, IsError = isError });
                    }
                    messages.Add(new BetaUserMessageParam { Content = results });
                }

                await SendEventAsync("done", new { }, ct);
            }
            catch (OperationCanceledException)
            {
                // Користувач закрив сторінку
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Помилка ШІ-асистента");
                await SendEventAsync("error", new { message = "Сервіс ШІ тимчасово недоступний. Спробуйте пізніше." }, CancellationToken.None);
            }
        }

        private record ToolCall(string Id, string Name, string InputJson);

        private record TurnResult(string? StopReason, List<BetaContentBlockParam> AssistantContent, List<ToolCall> ToolCalls);

        private class BlockAccumulator
        {
            public string Type = "";
            public string Id = "";
            public string Name = "";
            public System.Text.StringBuilder Text = new();
            public string Signature = "";
        }

        // Один запит до моделі зі стрімінгом: фрагменти тексту/думок одразу пересилаємо в браузер,
        // паралельно збираємо повні блоки, щоб повернути їх у наступному запиті
        private async Task<TurnResult> StreamTurnAsync(AnthropicClient client, List<BetaMessageParam> messages, CancellationToken ct)
        {
            var parameters = new MessageCreateParams
            {
                Model = Model,
                MaxTokens = 16000,
                System = SystemPrompt,
                Thinking = new BetaThinkingConfigAdaptive { Display = Display.Summarized },
                OutputConfig = new BetaOutputConfig { Effort = Effort.Medium },
                // Якщо класифікатори безпеки відхилять запит, сервер сам перенаправить його на резервну модель
                Betas = [AnthropicBeta.ServerSideFallback2026_07_01],
                Fallbacks = new Default(),
                Tools = ToolDefinitions,
                Messages = messages,
            };

            var blocks = new SortedDictionary<long, BlockAccumulator>();
            string? stopReason = null;

            await foreach (var ev in client.Beta.Messages.CreateStreaming(parameters, ct))
            {
                if (ev.TryPickContentBlockStart(out var start))
                {
                    var acc = new BlockAccumulator();
                    if (start.ContentBlock.TryPickBetaText(out _)) acc.Type = "text";
                    else if (start.ContentBlock.TryPickBetaThinking(out _)) acc.Type = "thinking";
                    else if (start.ContentBlock.TryPickBetaRedactedThinking(out var redacted)) { acc.Type = "redacted"; acc.Signature = redacted.Data; }
                    else if (start.ContentBlock.TryPickBetaToolUse(out var toolUse))
                    {
                        acc.Type = "tool_use";
                        acc.Id = toolUse.ID;
                        acc.Name = toolUse.Name;
                    }
                    blocks[start.Index] = acc;
                }
                else if (ev.TryPickContentBlockDelta(out var delta) && blocks.TryGetValue(delta.Index, out var block))
                {
                    if (delta.Delta.TryPickText(out var text))
                    {
                        block.Text.Append(text.Text);
                        await SendEventAsync("text", new { text = text.Text }, ct);
                    }
                    else if (delta.Delta.TryPickThinking(out var thinking))
                    {
                        block.Text.Append(thinking.Thinking);
                        await SendEventAsync("thinking", new { text = thinking.Thinking }, ct);
                    }
                    else if (delta.Delta.TryPickSignature(out var signature))
                    {
                        block.Signature = signature.Signature;
                    }
                    else if (delta.Delta.TryPickInputJson(out var inputJson))
                    {
                        block.Text.Append(inputJson.PartialJson);
                    }
                }
                else if (ev.TryPickContentBlockStop(out var stop) && blocks.TryGetValue(stop.Index, out var finished) && finished.Type == "tool_use")
                {
                    await SendEventAsync("tool_call", new { id = finished.Id, name = finished.Name, input = ParseInput(finished.Text.ToString()) }, ct);
                }
                else if (ev.TryPickDelta(out var messageDelta) && messageDelta.Delta.StopReason != null)
                {
                    stopReason = messageDelta.Delta.StopReason.Raw();
                }
            }

            var content = new List<BetaContentBlockParam>();
            var toolCalls = new List<ToolCall>();
            foreach (var b in blocks.Values)
            {
                switch (b.Type)
                {
                    case "text" when b.Text.Length > 0:
                        content.Add(new BetaTextBlockParam { Text = b.Text.ToString() });
                        break;
                    case "thinking":
                        // Підпис обов'язково повертаємо без змін, інакше API відхилить наступний запит
                        content.Add(new BetaThinkingBlockParam { Thinking = b.Text.ToString(), Signature = b.Signature });
                        break;
                    case "redacted":
                        content.Add(new BetaRedactedThinkingBlockParam { Data = b.Signature });
                        break;
                    case "tool_use":
                        var inputJson = b.Text.Length == 0 ? "{}" : b.Text.ToString();
                        content.Add(new BetaToolUseBlockParam { ID = b.Id, Name = b.Name, Input = ParseInput(inputJson) });
                        toolCalls.Add(new ToolCall(b.Id, b.Name, inputJson));
                        break;
                }
            }

            return new TurnResult(NormalizeStopReason(stopReason), content, toolCalls);
        }

        private static string? NormalizeStopReason(string? raw) => raw switch
        {
            null => null,
            _ when raw.Equals("ToolUse", StringComparison.OrdinalIgnoreCase) || raw == "tool_use" => "tool_use",
            _ when raw.Equals("Refusal", StringComparison.OrdinalIgnoreCase) => "refusal",
            _ => raw
        };

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

        // Валідуємо аргументи перед виконанням - модель могла згенерувати неповний JSON
        private async Task<string> ExecuteToolAsync(string name, string inputJson)
        {
            var input = ParseInput(inputJson);
            string? Str(string key) => input.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            int? Int(string key) => input.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : null;
            decimal? Dec(string key) => input.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var d) ? d : null;

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

        private string? ApiKey => _config["Anthropic:ApiKey"] ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");

        private string SystemPrompt =>
            "Ти - асистент офіційного фан-клубу музичного гурту на платформі BandVault. " +
            "Відповідай українською, коротко і дружньо. Для питань про мерч, ціни, наявність, релізи та треки " +
            "користуйся інструментами каталогу, а не вигадуй дані. Посилання на сторінки сайту давай у форматі markdown. " +
            (User.HasClaim("IsAdmin", "true")
                ? "Користувач - адміністратор: він може просити додати товар (create_merch_item); перед створенням коротко підсумуй, що саме додаєш."
                : "Користувач - фанат, додавати товари він не може.");

        private static BetaToolUnion[] ToolDefinitions =>
        [
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
        ];

        private static BetaToolUnion Tool(string name, string description, object properties, params string[] required)
        {
            var props = JsonSerializer.SerializeToElement(properties)
                .EnumerateObject()
                .ToDictionary(p => p.Name, p => p.Value);
            return new BetaTool
            {
                Name = name,
                Description = description,
                EagerInputStreaming = true,
                InputSchema = new() { Properties = props, Required = required },
            };
        }
    }
}
