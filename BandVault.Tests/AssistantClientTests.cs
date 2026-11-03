using System.Text.Json.Nodes;
using BandVault.Web.Services.Assistant;
using Xunit;
using Xunit.Abstractions;

namespace BandVault.Tests
{
    // C21: клієнт OpenAI-сумісного API асистента
    public class AssistantClientTests
    {
        private readonly ITestOutputHelper _output;

        public AssistantClientTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void ThinkTags_SplitAcrossChunks_AreSeparatedFromAnswer()
        {
            var parser = new ChatCompletionsClient.ThinkTagParser();
            var pieces = new[] { "<tho", "ught>Шукаю в каталозі</thou", "ght>Є худі. <think>ще</think>Кінець" }
                .SelectMany(parser.Feed)
                .ToList();

            Assert.Equal("Шукаю в каталозіще", string.Concat(pieces.Where(p => p.IsThinking).Select(p => p.Text)));
            Assert.Equal("Є худі. Кінець", string.Concat(pieces.Where(p => !p.IsThinking).Select(p => p.Text)));
        }

        // Живий тест з Google AI Studio. Запуск: $env:GEMINI_API_KEY="..."; dotnet test --filter LiveGemini
        [Fact]
        public async Task LiveGemini_ToolCallRoundTrip_ReturnsAnswer()
        {
            var key = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
            if (string.IsNullOrEmpty(key))
            {
                _output.WriteLine("GEMINI_API_KEY не задано - живий тест пропущено");
                return;
            }

            var options = new AssistantOptions
            {
                BaseUrl = "https://generativelanguage.googleapis.com/v1beta/openai",
                Model = Environment.GetEnvironmentVariable("GEMINI_MODEL") ?? "gemini-3.1-flash-lite-preview",
                ApiKey = key
            };
            var client = new ChatCompletionsClient(new HttpClient { Timeout = TimeSpan.FromMinutes(2) }, options);

            var tools = JsonNode.Parse("""
                [{"type":"function","function":{"name":"list_merch","description":"Список товарів мерчу гурту з цінами та залишками",
                  "parameters":{"type":"object","properties":{"query":{"type":"string"}},"required":[]}}}]
                """)!.AsArray();
            var messages = new JsonArray
            {
                new JsonObject { ["role"] = "system", ["content"] = "Ти асистент фан-клубу. Для питань про мерч обов'язково викликай list_merch." },
                new JsonObject { ["role"] = "user", ["content"] = "Які є худі?" }
            };

            int thinking = 0;
            var turn1 = await client.StreamTurnAsync(messages, tools, ev => { if (ev is ThinkingDelta) thinking++; return Task.CompletedTask; }, CancellationToken.None);
            Assert.NotEmpty(turn1.ToolCalls);
            var call = turn1.ToolCalls[0];
            _output.WriteLine($"Модель: {options.Model}; виклик: {call.Name}({call.ArgumentsJson}); підпис: {(call.Extra != null ? "є" : "немає")}");

            // Другий крок - так само, як в AssistantController (з поверненням extra_content)
            var assistantCall = new JsonObject
            {
                ["id"] = call.Id,
                ["type"] = "function",
                ["function"] = new JsonObject { ["name"] = call.Name, ["arguments"] = call.ArgumentsJson }
            };
            if (call.Extra != null) assistantCall["extra_content"] = call.Extra.DeepClone();
            messages.Add(new JsonObject { ["role"] = "assistant", ["content"] = turn1.Text, ["tool_calls"] = new JsonArray(assistantCall) });
            messages.Add(new JsonObject
            {
                ["role"] = "tool",
                ["tool_call_id"] = call.Id,
                ["content"] = """{"items":[{"id":1,"title":"Dandelion Tour Hoodie","price":55,"stock":12}]}"""
            });

            var answer = new System.Text.StringBuilder();
            var turn2 = await client.StreamTurnAsync(messages, tools, ev =>
            {
                if (ev is TextDelta t) answer.Append(t.Text);
                if (ev is ThinkingDelta) thinking++;
                return Task.CompletedTask;
            }, CancellationToken.None);

            _output.WriteLine($"Фрагментів думок: {thinking}; відповідь: {answer}");
            Assert.Contains("Dandelion", answer.ToString());
        }
    }
}
