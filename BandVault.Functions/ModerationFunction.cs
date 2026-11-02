using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace BandVault.Functions
{
    public record FanMessage(string? Author, string? Text);

    public record ModerationResult(bool Approved, string[] Reasons, string CleanedText);

    // B7: Перевірка повідомлень фанатів (стіна, коментарі) перед публікацією
    public class ModerationFunction
    {
        private static readonly string[] BannedWords = { "спам", "казино", "scam", "casino", "free money", "заробіток без вкладень" };
        private static readonly Regex LinkRegex = new(@"https?://\S+|www\.\S+", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private readonly ILogger<ModerationFunction> _logger;

        public ModerationFunction(ILogger<ModerationFunction> logger)
        {
            _logger = logger;
        }

        [Function("ModerateFanMessage")]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "moderation/check")] HttpRequest req)
        {
            FanMessage? message;
            try
            {
                message = await req.ReadFromJsonAsync<FanMessage>();
            }
            catch
            {
                return new BadRequestObjectResult(new { error = "Очікується JSON { author, text }" });
            }
            if (message == null || string.IsNullOrWhiteSpace(message.Text))
                return new BadRequestObjectResult(new { error = "Поле text обов'язкове" });

            var result = Moderate(message.Text);
            _logger.LogInformation("Модерація повідомлення від {Author}: {Approved}", message.Author ?? "anon", result.Approved);
            return new OkObjectResult(result);
        }

        public static ModerationResult Moderate(string text)
        {
            var reasons = new List<string>();
            var cleaned = text.Trim();

            if (cleaned.Length > 1000) reasons.Add("Повідомлення довше за 1000 символів");

            foreach (var word in BannedWords)
            {
                if (cleaned.Contains(word, StringComparison.OrdinalIgnoreCase))
                {
                    reasons.Add($"Заборонене слово: {word}");
                    cleaned = Regex.Replace(cleaned, Regex.Escape(word), new string('*', word.Length), RegexOptions.IgnoreCase);
                }
            }

            var links = LinkRegex.Matches(cleaned).Count;
            if (links > 1) reasons.Add("Забагато посилань (можливий спам)");

            var letters = cleaned.Where(char.IsLetter).ToArray();
            if (letters.Length >= 15 && letters.Count(char.IsUpper) > letters.Length * 0.7)
                reasons.Add("Текст написаний капсом");

            if (Regex.IsMatch(cleaned, @"(.)\1{9,}")) reasons.Add("Повторювані символи (флуд)");

            return new ModerationResult(reasons.Count == 0, reasons.ToArray(), cleaned);
        }
    }
}
