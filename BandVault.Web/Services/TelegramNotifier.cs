using System.Net;

namespace BandVault.Web.Services
{
    // B2: Надсилання повідомлень у Telegram-канал через Bot API
    public class TelegramNotifier
    {
        private readonly HttpClient _http;
        private readonly IConfiguration _config;
        private readonly ILogger<TelegramNotifier> _logger;

        public TelegramNotifier(HttpClient http, IConfiguration config, ILogger<TelegramNotifier> logger)
        {
            _http = http;
            _config = config;
            _logger = logger;
        }

        public async Task SendHtmlAsync(string html)
        {
            string token = _config["TelegramBot:Token"] ?? "";
            string chatId = _config["TelegramBot:ChatId"] ?? "";
            if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(chatId)) return;

            try
            {
                var response = await _http.PostAsync($"https://api.telegram.org/bot{token}/sendMessage",
                    new FormUrlEncodedContent(new Dictionary<string, string>
                    {
                        ["chat_id"] = chatId,
                        ["text"] = html,
                        ["parse_mode"] = "HTML"
                    }));
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Telegram відповів {Status}", response.StatusCode);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Не вдалося надіслати повідомлення в Telegram");
            }
        }

        public static string Encode(string? text) => WebUtility.HtmlEncode(text ?? "");
    }
}
