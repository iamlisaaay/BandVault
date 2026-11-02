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

    public record ToolCallRequest(string Id, string Name, string ArgumentsJson);

    public record ChatTurnResult(string Text, IReadOnlyList<ToolCallRequest> ToolCalls, string? FinishReason);

    public class AssistantOptions
    {
        // За замовчуванням - локальна Ollama (безкоштовно, без ключа). Інші безкоштовні варіанти - у docs/IMPLEMENTATION.md
        public string BaseUrl { get; set; } = "http://localhost:11434/v1";
        public string Model { get; set; } = "qwen3:4b";
        public string? ApiKey { get; set; }
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
                ["stream"] = true,
                ["temperature"] = 0.4
            };

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
            var toolCalls = new SortedDictionary<int, (string Id, string Name, StringBuilder Args)>();
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
                        int index = call?["index"]?.GetValue<int>() ?? toolCalls.Count;
                        if (!toolCalls.TryGetValue(index, out var acc)) acc = ("", "", new StringBuilder());
                        var id = call?["id"]?.GetValue<string>();
                        var name = call?["function"]?["name"]?.GetValue<string>();
                        var args = call?["function"]?["arguments"]?.GetValue<string>();
                        acc = (string.IsNullOrEmpty(id) ? acc.Id : id, string.IsNullOrEmpty(name) ? acc.Name : name, acc.Args.Append(args));
                        toolCalls[index] = acc;
                    }
                }
            }

            var requests = toolCalls.Values
                .Where(c => !string.IsNullOrEmpty(c.Name))
                .Select((c, i) => new ToolCallRequest(string.IsNullOrEmpty(c.Id) ? $"call_{i}" : c.Id, c.Name, c.Args.Length == 0 ? "{}" : c.Args.ToString()))
                .ToList();
            return new ChatTurnResult(text.ToString(), requests, finishReason);
        }

        private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";

        // Розбирає потік тексту на звичайний текст і блоки <think>...</think> (qwen3, deepseek-r1 тощо)
        private class ThinkTagParser
        {
            private bool _inThink;
            private string _pending = "";

            public IEnumerable<(bool IsThinking, string Text)> Feed(string chunk)
            {
                var buffer = _pending + chunk;
                _pending = "";
                var result = new List<(bool, string)>();
                while (buffer.Length > 0)
                {
                    var tag = _inThink ? "</think>" : "<think>";
                    int idx = buffer.IndexOf(tag, StringComparison.Ordinal);
                    if (idx >= 0)
                    {
                        if (idx > 0) result.Add((_inThink, buffer[..idx]));
                        buffer = buffer[(idx + tag.Length)..];
                        _inThink = !_inThink;
                        continue;
                    }
                    // Тег міг розірватися між фрагментами - залишаємо можливий початок тегу на потім
                    int keep = PartialTagSuffix(buffer, tag);
                    if (buffer.Length - keep > 0) result.Add((_inThink, buffer[..^keep]));
                    _pending = buffer[^keep..];
                    break;
                }
                return result.Where(r => r.Item2.Length > 0);
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
