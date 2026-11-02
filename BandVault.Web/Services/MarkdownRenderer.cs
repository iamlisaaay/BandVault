using Ganss.Xss;
using Markdig;
using Microsoft.AspNetCore.Html;

namespace BandVault.Web.Services
{
    // C12: Markdown постів -> безпечний HTML.
    // Редактор вставляє медіа як сирий HTML (<video>/<audio>), тому HTML не вимикаємо,
    // а пропускаємо результат через білий список тегів (скрипти, обробники подій, iframe вирізаються).
    public static class MarkdownRenderer
    {
        // Роздільник блоків редактора - HTML-коментар, у відрендереному пості він невидимий
        public const string BlockSeparator = "<!-- block -->";

        private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();
        private static readonly HtmlSanitizer Sanitizer = CreateSanitizer();

        public static IHtmlContent ToHtml(string? markdown)
        {
            var html = Markdown.ToHtml(markdown ?? "", Pipeline);
            return new HtmlString(Sanitizer.Sanitize(html));
        }

        private static HtmlSanitizer CreateSanitizer()
        {
            var sanitizer = new HtmlSanitizer();
            foreach (var tag in new[] { "video", "audio", "source" }) sanitizer.AllowedTags.Add(tag);
            foreach (var attr in new[] { "controls", "src", "type", "class", "style", "preload", "poster" }) sanitizer.AllowedAttributes.Add(attr);
            sanitizer.AllowedSchemes.Add("https");
            sanitizer.AllowedSchemes.Add("http");
            return sanitizer;
        }
    }
}
