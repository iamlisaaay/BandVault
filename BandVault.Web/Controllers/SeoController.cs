using System.Text;
using System.Xml.Linq;
using BandVault.Web.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace BandVault.Web.Controllers
{
    // ============================================
    // C7: SEO - динамічний sitemap.xml та OpenGraph-зображення
    // ============================================
    public class SeoController : Controller
    {
        private static readonly XNamespace Ns = "http://www.sitemaps.org/schemas/sitemap/0.9";

        private readonly ApplicationDbContext _context;
        private readonly IMemoryCache _cache;

        public SeoController(ApplicationDbContext context, IMemoryCache cache)
        {
            _context = context;
            _cache = cache;
        }

        // robots.txt генерується, бо рядок Sitemap має містити абсолютну адресу поточного домену
        [HttpGet("/robots.txt")]
        [ResponseCache(Duration = 86400)]
        public IActionResult Robots()
        {
            var lines = new[]
            {
                "User-agent: *",
                "Disallow: /Auth/",
                "Disallow: /SiteSettings/",
                "Disallow: /Orders/",
                "Disallow: /Cart/",
                "Disallow: /api/",
                "Disallow: /mcp",
                "Allow: /",
                "",
                $"Sitemap: {Request.Scheme}://{Request.Host}/sitemap.xml"
            };
            return Content(string.Join("\n", lines), "text/plain", Encoding.UTF8);
        }

        // Публічні сторінки: головна, каталог мерчу, лендінги товарів, опубліковані відкриті пости
        [HttpGet("/sitemap.xml")]
        [ResponseCache(Duration = 3600)]
        public async Task<IActionResult> Sitemap()
        {
            var baseUrl = $"{Request.Scheme}://{Request.Host}";
            var urls = new List<XElement>
            {
                SitemapUrl(baseUrl + "/", null, "daily", "1.0"),
                SitemapUrl(baseUrl + "/Merch", null, "daily", "0.9"),
            };

            foreach (var id in await _context.MerchItems.OrderBy(m => m.Id).Select(m => m.Id).ToListAsync())
            {
                urls.Add(SitemapUrl($"{baseUrl}/Merch/Details/{id}", null, "weekly", "0.8"));
            }

            var posts = await _context.Posts
                .Where(p => p.IsPublished && p.RequiredTierLevel == 0)
                .Select(p => new { p.Id, p.CreatedAt })
                .ToListAsync();
            foreach (var post in posts)
            {
                urls.Add(SitemapUrl($"{baseUrl}/Posts/Details/{post.Id}", post.CreatedAt, "monthly", "0.6"));
            }

            var xml = new XDocument(new XDeclaration("1.0", "utf-8", null), new XElement(Ns + "urlset", urls));
            return Content(xml.Declaration + Environment.NewLine + xml.Root, "application/xml", Encoding.UTF8);
        }

        // 1200x630 - рекомендований розмір прев'ю для Facebook/Telegram/X (ICO соцмережі не показують)
        [HttpGet("/og-image.png")]
        [ResponseCache(Duration = 86400)]
        public async Task<IActionResult> OgImage()
        {
            var bandName = await _context.SiteSettings.Where(s => s.Key == "BandName").Select(s => s.Value).FirstOrDefaultAsync() ?? "BandVault";
            var png = await _cache.GetOrCreateAsync($"og_image_{bandName}", entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(6);
                return Task.FromResult(RenderOgImage(bandName));
            });
            return File(png!, "image/png");
        }

        private static byte[] RenderOgImage(string bandName)
        {
            return Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(1200, 630, Unit.Point);
                    page.PageColor("#000000");
                    page.Content().Padding(40).Border(1).BorderColor("#333333").AlignMiddle().AlignCenter().Column(col =>
                    {
                        col.Spacing(20);
                        col.Item().AlignCenter().Text(bandName.ToUpperInvariant()).FontSize(88).Bold().FontColor(Colors.White).LetterSpacing(0.08f);
                        col.Item().AlignCenter().Text("ОФІЦІЙНИЙ ФАН-КЛУБ · РЕЛІЗИ · МЕРЧ").FontSize(26).FontColor("#888888").LetterSpacing(0.15f);
                    });
                });
            }).GenerateImages(new ImageGenerationSettings { ImageFormat = ImageFormat.Png, RasterDpi = 72 }).First();
        }

        private static XElement SitemapUrl(string loc, DateTime? lastModified, string changeFreq, string priority)
        {
            var element = new XElement(Ns + "url", new XElement(Ns + "loc", loc));
            if (lastModified.HasValue) element.Add(new XElement(Ns + "lastmod", lastModified.Value.ToString("yyyy-MM-dd")));
            element.Add(new XElement(Ns + "changefreq", changeFreq), new XElement(Ns + "priority", priority));
            return element;
        }
    }
}
