using BandVault.Web.Data;
using BandVault.Web.Hubs;
using BandVault.Web.Models;
using BandVault.Web.Services; // Додано підключення наших сервісів
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.IO;
using System.Net.Http;
using System.Web;

namespace BandVault.Web.Controllers
{
    [Authorize]
    public class PostsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _env;
        private readonly IConfiguration _config;
        private readonly IHubContext<FeedHub> _hubContext;
        private readonly IFileService _fileService; // НОВЕ: Сервіс для Azure
        private readonly TelegramNotifier _telegram;

        public PostsController(ApplicationDbContext context, IWebHostEnvironment env, IConfiguration config, IHubContext<FeedHub> hubContext, IFileService fileService, TelegramNotifier telegram)
        {
            _context = context;
            _env = env;
            _config = config;
            _hubContext = hubContext;
            _fileService = fileService;
            _telegram = telegram;
        }

        private const long MaxMediaSize = 20 * 1024 * 1024;

        [HttpPost]
        [RequestSizeLimit(MaxMediaSize + 1024 * 1024)]
        public async Task<IActionResult> UploadMedia(IFormFile file)
        {
            if (!User.HasClaim("IsAdmin", "true")) return Forbid();

            if (file == null || file.Length == 0) return BadRequest("Файл порожній.");
            if (file.Length > MaxMediaSize) return BadRequest("Файл більший за 20 МБ.");

            string ext = Path.GetExtension(file.FileName).ToLower();

            // Лише медіа з білого списку: .html/.svg тощо зі скриптами у публічне сховище не потрапляють
            string? type = ext switch
            {
                ".jpg" or ".jpeg" or ".png" or ".gif" or ".webp" => "image",
                ".mp4" or ".webm" or ".mov" => "video",
                ".mp3" or ".wav" or ".m4a" or ".ogg" or ".flac" => "audio",
                _ => null
            };
            if (type == null) return BadRequest("Дозволені лише зображення, відео та аудіо.");

            // C19: Завантажуємо файл у хмару Azure Blob Storage замість локальної папки
            string fileUrl = await _fileService.UploadFileAsync(file, "posts");

            return Json(new { url = fileUrl, type = type, name = file.FileName });
        }

        public async Task<IActionResult> Index()
        {
            var currentUserEmail = User.Identity?.Name;
            var user = await _context.Users.Include(u => u.SubscriptionTier).FirstOrDefaultAsync(u => u.Email == currentUserEmail);

            bool isAdmin = user?.IsAdmin ?? false;
            ViewBag.IsAdmin = isAdmin;
            ViewBag.UserTier = user?.SubscriptionTier?.Level ?? 0;
            ViewBag.CurrentUserId = user?.Id ?? 0;

            var posts = await _context.Posts
                .Include(p => p.Author)
                .Where(p => isAdmin || p.IsPublished || (user != null && p.AuthorId == user.Id))
                .OrderByDescending(p => p.CreatedAt)
                .ToListAsync();

            return View(posts);
        }

        public async Task<IActionResult> Details(int? id)
        {
            if (id == null) return NotFound();

            var post = await _context.Posts
                .Include(p => p.Author)
                .FirstOrDefaultAsync(m => m.Id == id);

            if (post == null) return NotFound();

            var currentUserEmail = User.Identity?.Name;
            var user = await _context.Users.Include(u => u.SubscriptionTier).FirstOrDefaultAsync(u => u.Email == currentUserEmail);

            bool isAdmin = user?.IsAdmin ?? false;
            int userTier = user?.SubscriptionTier?.Level ?? 0;

            // Чернетку бачать лише адмін та автор, а не будь-хто, хто знає id
            if (!post.IsPublished && !isAdmin && post.AuthorId != user?.Id) return NotFound();

            bool hasAccess = isAdmin || post.RequiredTierLevel == 0 || userTier >= post.RequiredTierLevel;

            ViewBag.HasAccess = hasAccess;
            ViewBag.RequiredTier = post.RequiredTierLevel;
            ViewBag.UserTier = userTier;
            ViewBag.IsAdmin = isAdmin;

            return View(post);
        }

        public async Task<IActionResult> Create()
        {
            var currentUserEmail = User.Identity?.Name;
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == currentUserEmail);

            if (user == null || !user.IsAdmin)
                return Forbid();

            var tiers = await _context.SubscriptionTiers.OrderBy(t => t.Level).ToListAsync();
            var tierOptions = new List<SelectListItem>
            {
                new SelectListItem { Value = "0", Text = "Усім підписникам (Tier 0+)" }
            };
            tierOptions.AddRange(tiers.Select(t => new SelectListItem { Value = t.Level.ToString(), Text = $"Лише Tier {t.Level} ({t.Name}) і вище" }));

            ViewBag.Tiers = tierOptions;

            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Post post)
        {
            var currentUserEmail = User.Identity?.Name;
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == currentUserEmail);

            if (user != null && user.IsAdmin)
            {
                post.AuthorId = user.Id;
                post.CreatedAt = DateTime.UtcNow;

                _context.Posts.Add(post);
                await _context.SaveChangesAsync();

                if (post.IsPublished)
                {
                    await AnnouncePostAsync(post);
                }

                return RedirectToAction(nameof(Index));
            }

            return Forbid();
        }

        // Повідомлення про публікацію: Telegram (B2) + SignalR (C11)
        private async Task AnnouncePostAsync(Post post)
        {
            string postUrl = Url.Action("Details", "Posts", new { id = post.Id }, Request.Scheme) ?? "";
            bool isExclusive = post.RequiredTierLevel > 0;

            // Відправка в Telegram не затримує відповідь адміну
            string title = isExclusive ? $"Ексклюзив для Tier {post.RequiredTierLevel}+" : TelegramNotifier.Encode(post.Title);
            _ = _telegram.SendHtmlAsync($"🔥 <b>Новий пост у фан-клубі!</b>\n\n<b>{title}</b>\n\n👉 <a href=\"{postUrl}\">Читати повністю</a>");

            // Назви закритих постів не розсилаємо всім підключеним (включно з анонімами)
            await _hubContext.Clients.All.SendAsync("ReceiveNewPost", new
            {
                id = post.Id,
                title = isExclusive ? "Новий ексклюзивний матеріал" : post.Title,
                tier = post.RequiredTierLevel
            });
        }

        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();

            var post = await _context.Posts.FindAsync(id);
            if (post == null) return NotFound();

            var currentUserEmail = User.Identity?.Name;
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == currentUserEmail);

            if (user == null || post.AuthorId != user.Id)
            {
                return Forbid();
            }

            var tiers = await _context.SubscriptionTiers.OrderBy(t => t.Level).ToListAsync();
            var tierOptions = new List<SelectListItem>
            {
                new SelectListItem { Value = "0", Text = "Усім підписникам (Tier 0+)" }
            };
            tierOptions.AddRange(tiers.Select(t => new SelectListItem { Value = t.Level.ToString(), Text = $"Лише Tier {t.Level} ({t.Name}) і вище" }));

            ViewBag.Tiers = tierOptions;

            return View(post);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, Post post)
        {
            if (id != post.Id) return NotFound();

            var currentUserEmail = User.Identity?.Name;
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == currentUserEmail);

            try
            {
                var existingPost = await _context.Posts.FindAsync(id);
                if (existingPost == null) return NotFound();

                if (user == null || existingPost.AuthorId != user.Id)
                {
                    return Forbid();
                }

                bool becamePublished = !existingPost.IsPublished && post.IsPublished;

                existingPost.Title = post.Title;
                existingPost.ContentMarkdown = post.ContentMarkdown;
                existingPost.IsPublished = post.IsPublished;
                existingPost.RequiredTierLevel = post.RequiredTierLevel;

                await _context.SaveChangesAsync();

                // Чернетку опублікували через редагування - теж сповіщаємо
                if (becamePublished) await AnnouncePostAsync(existingPost);
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!_context.Posts.Any(e => e.Id == post.Id)) return NotFound();
                else throw;
            }

            return RedirectToAction(nameof(Index));
        }

        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null) return NotFound();

            var post = await _context.Posts.FirstOrDefaultAsync(m => m.Id == id);
            if (post == null) return NotFound();

            var currentUserEmail = User.Identity?.Name;
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == currentUserEmail);

            if (user == null || post.AuthorId != user.Id)
            {
                return Forbid();
            }

            return View(post);
        }

        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var existingPost = await _context.Posts.FindAsync(id);
            if (existingPost == null) return NotFound();

            var currentUserEmail = User.Identity?.Name;
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == currentUserEmail);

            if (user == null || existingPost.AuthorId != user.Id)
            {
                return Forbid();
            }

            _context.Posts.Remove(existingPost);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }
    }
}