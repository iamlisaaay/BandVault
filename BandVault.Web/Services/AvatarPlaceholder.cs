using System.Net;

namespace BandVault.Web.Services
{
    // Заглушка аватара: SVG з першою літерою, вбудований як data URI.
    // Замінює via.placeholder.com, який більше не працює.
    public static class AvatarPlaceholder
    {
        public static string For(string? name, int size = 150)
        {
            var letter = string.IsNullOrWhiteSpace(name) ? "?" : name.Trim()[..1].ToUpperInvariant();
            var svg = $"<svg xmlns='http://www.w3.org/2000/svg' width='{size}' height='{size}' viewBox='0 0 100 100'>" +
                      "<rect width='100' height='100' fill='#1a1a1a'/>" +
                      $"<text x='50' y='50' dy='.35em' text-anchor='middle' font-family='Arial,sans-serif' font-size='46' fill='#ffffff'>{WebUtility.HtmlEncode(letter)}</text></svg>";
            return "data:image/svg+xml;charset=utf-8," + Uri.EscapeDataString(svg);
        }
    }
}
