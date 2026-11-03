using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BandVault.Web.Services.Assistant
{
    // Події одного кроку моделі, що стрімляться в браузер
    public abstract record ChatStreamEvent;
    public record TextDelta(string Text) : ChatStreamEvent;
    public record ThinkingDelta(string Text) : ChatStreamEvent;

    // Extra - службові дані провайдера, які треба повернути разом із викликом
    // (Gemini 3: extra_content.google.thought_signature, без нього наступний запит отримує 400)
    public record ToolCallRequest(string Id, string Name, string ArgumentsJson, JsonNode? Extra = null);

    public record ChatTurnResult(string Text, IReadOnlyList<ToolCallRequest> ToolCalls, string? FinishReason);

    public class AssistantOptions
    {
        // За замовчуванням - Google Gemini (безкоштовний ключ з AI Studio). Інші варіанти (Ollama, Groq) - у docs/IMPLEMENTATION.md
        public string BaseUrl { get; set; } = "https://generativelanguage.googleapis.com/v1beta/openai";
        public string Model { get; set; } = "gemini-3.1-flash-lite-preview";
        public string? ApiKey { get; set; }
        // Просити модель показувати хід міркувань (для Gemini - thinking_config.include_thoughts)
        public bool IncludeThoughts { get; set; } = true;

        public bool IsGemini => BaseUrl.Contains("generativelanguage.googleapis.com", StringComparison.OrdinalIgnoreCase);
    }

    // ============================================
    // C21: КЛІЄНТ OpenAI-СУМІСНОГО Chat Completions API зі стрімінгом і викликом інструментів.
    // Працює з Ollama (локально), Google Gemini, Groq, OpenRouter - змінюється лише Assistant:BaseUrl / Model / ApiKey.
    // ============================================
    public class ChatCompletionsClient
    {
        private readonly HttpClient _http;
        private readonly AssistantOptions _options;

        public ChatCompletionsClient(HttpClient http, AssistantOptions options)
        {
            _http = http;
            _options = options;
        }

        public string Model => _options.Model;

        // Один запит до моделі: фрагменти тексту й "думок" віддаються через onEvent одразу,
        // а виклики інструментів збираються з фрагментів і повертаються в кінці
        public async Task<ChatTurnResult> StreamTurnAsync(JsonArray messages, JsonArray tools, Func<ChatStreamEvent, Task> onEvent, CancellationToken ct)
        {
            var body = new JsonObject
            {
                ["model"] = _options.Model,
                ["messages"] = messages.DeepClone(),
                ["tools"] = tools.DeepClone(),
                ["stream"] = true
                // temperature не задаємо: Gemini 3 рекомендує значення за замовчуванням (нижче - ризик зациклення)
            };
            if (_options.IncludeThoughts && _options.IsGemini)
            {
                // Gemini повертає підсумок міркувань у тексті між тегами <thought>...</thought>
                body["extra_body"] = new JsonObject
                {
                    ["google"] = new JsonObject { ["thinking_config"] = new JsonObject { ["include_thoughts"] = true } }
                };
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, _options.BaseUrl.TrimEnd('/') + "/chat/completions")
            {
                Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
            };
            if (!string.IsNullOrWhiteSpace(_options.ApiKey))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
            }

            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(ct);
                throw new ChatProviderException($"Провайдер ШІ відповів {(int)response.StatusCode}: {Truncate(error, 300)}");
            }

            var text = new StringBuilder();
            var toolCalls = new SortedDictionary<int, (string Id, string Name, StringBuilder Args, JsonNode? Extra)>();
            var thinkParser = new ThinkTagParser();
            string? finishReason = null;

            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var reader = new StreamReader(stream);
            while (await reader.ReadLineAsync(ct) is { } line)
            {
                if (!line.StartsWith("data:")) continue;
                var data = line[5..].Trim();
                if (data == "[DONE]") break;
                if (data.Length == 0) continue;

                var chunk = JsonNode.Parse(data);
                var choice = chunk?["choices"]?[0];
                if (choice == null) continue;
                finishReason = choice["finish_reason"]?.GetValue<string>() ?? finishReason;

                var delta = choice["delta"];
                if (delta == null) continue;

                // Різні провайдери передають міркування по-різному: окреме поле або теги <think> у тексті
                var reasoning = delta["reasoning_content"]?.GetValue<string>() ?? delta["reasoning"]?.GetValue<string>();
                if (!string.IsNullOrEmpty(reasoning)) await onEvent(new ThinkingDelta(reasoning));

                var content = delta["content"]?.GetValue<string>();
                if (!string.IsNullOrEmpty(content))
                {
                    foreach (var piece in thinkParser.Feed(content))
                    {
                        if (piece.IsThinking) await onEvent(new ThinkingDelta(piece.Text));
                        else
                        {
                            text.Append(piece.Text);
                            await onEvent(new TextDelta(piece.Text));
                        }
                    }
                }

                if (delta["tool_calls"] is JsonArray calls)
                {
                    foreach (var call in calls)
                    {
                        // OpenAI/Ollama надсилають index і дробити аргументи на фрагменти;
                        // Gemini надсилає кожен виклик цілком і без index
                        int index = call?["index"]?.GetValue<int>() ?? toolCalls.Count;
                        if (!toolCalls.TryGetValue(index, out var acc)) acc = ("", "", new StringBuilder(), null);
                        var id = call?["id"]?.GetValue<string>();
                        var name = call?["function"]?["name"]?.GetValue<string>();
                        var args = call?["function"]?["arguments"]?.GetValue<string>();
                        var extra = call?["extra_content"]?.DeepClone() ?? acc.Extra;
                        acc = (string.IsNullOrEmpty(id) ? acc.Id : id, string.IsNullOrEmpty(name) ? acc.Name : name, acc.Args.Append(args), extra);
                        toolCalls[index] = acc;
                    }
                }
            }

            var requests = toolCalls.Values
                .Where(c => !string.IsNullOrEmpty(c.Name))
                .Select((c, i) => new ToolCallRequest(string.IsNullOrEmpty(c.Id) ? $"call_{i}" : c.Id, c.Name, c.Args.Length == 0 ? "{}" : c.Args.ToString(), c.Extra))
                .ToList();
            return new ChatTurnResult(text.ToString(), requests, finishReason);
        }

        private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";

        // Розбирає потік тексту на звичайний текст і блоки міркувань:
        // <think>...</think> (qwen3, deepseek-r1 в Ollama) та <thought>...</thought> (Gemini з include_thoughts)
        internal class ThinkTagParser
        {
            private static readonly string[] OpenTags = { "<think>", "<thought>" };

            private string? _closeTag; // не null - зараз усередині блоку міркувань
            private string _pending = "";

            public IEnumerable<(bool IsThinking, string Text)> Feed(string chunk)
            {
                var buffer = _pending + chunk;
                _pending = "";
                var result = new List<(bool, string)>();
                while (buffer.Length > 0)
                {
                    var (idx, tag) = _closeTag != null
                        ? (buffer.IndexOf(_closeTag, StringComparison.Ordinal), _closeTag)
                        : FindFirstOpenTag(buffer);
                    if (idx >= 0)
                    {
                        if (idx > 0) result.Add((_closeTag != null, buffer[..idx]));
                        buffer = buffer[(idx + tag.Length)..];
                        _closeTag = _closeTag != null ? null : "</" + tag[1..];
                        continue;
                    }
                    // Тег міг розірватися між фрагментами - залишаємо можливий початок тегу на потім
                    int keep = _closeTag != null ? PartialTagSuffix(buffer, _closeTag) : OpenTags.Max(t => PartialTagSuffix(buffer, t));
                    if (buffer.Length - keep > 0) result.Add((_closeTag != null, buffer[..^keep]));
                    _pending = buffer[^keep..];
                    break;
                }
                return result.Where(r => r.Item2.Length > 0);
            }

            private static (int Index, string Tag) FindFirstOpenTag(string buffer)
            {
                var found = OpenTags
                    .Select(t => (Index: buffer.IndexOf(t, StringComparison.Ordinal), Tag: t))
                    .Where(x => x.Index >= 0)
                    .OrderBy(x => x.Index)
                    .FirstOrDefault();
                return found.Tag == null ? (-1, "") : found;
            }

            private static int PartialTagSuffix(string s, string tag)
            {
                for (int len = Math.Min(tag.Length - 1, s.Length); len > 0; len--)
                {
                    if (s.EndsWith(tag[..len], StringComparison.Ordinal)) return len;
                }
                return 0;
            }
        }
    }

    public class ChatProviderException : Exception
    {
        public ChatProviderException(string message) : base(message) { }
    }
}
