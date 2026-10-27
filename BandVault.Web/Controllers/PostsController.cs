using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering; // Додано для SelectList
using Microsoft.EntityFrameworkCore;
using BandVault.Web.Data;
using BandVault.Web.Models;

namespace BandVault.Web.Controllers
{
    [Authorize]
    public class PostsController : Controller
    {
        private readonly ApplicationDbContext _context;

        public PostsController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: Posts
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
                // Адмін бачить усі пости (навіть чернетки інших). Звичайні юзери бачать тільки опубліковані, або свої чернетки.
                .Where(p => isAdmin || p.IsPublished || (user != null && p.AuthorId == user.Id))
                .OrderByDescending(p => p.CreatedAt)
                .ToListAsync();

            return View(posts);
        }

        // GET: Posts/Details/5
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

            // ГАРАНТІЯ ДОСТУПУ: Адміни завжди мають доступ. Інакше - перевіряємо рівень.
            bool hasAccess = isAdmin || post.RequiredTierLevel == 0 || userTier >= post.RequiredTierLevel;

            ViewBag.HasAccess = hasAccess;
            ViewBag.RequiredTier = post.RequiredTierLevel;
            ViewBag.UserTier = userTier;
            ViewBag.IsAdmin = isAdmin;

            return View(post);
        }

        // GET: Posts/Create
        public async Task<IActionResult> Create()
        {
            var currentUserEmail = User.Identity?.Name;
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == currentUserEmail);

            if (user == null || !user.IsAdmin)
                return Forbid();

            // ДІСТАЄМО ТІРИ З БАЗИ ДЛЯ ВИТЯЖНОГО СПИСКУ
            var tiers = await _context.SubscriptionTiers.OrderBy(t => t.Level).ToListAsync();
            // Створюємо список, додаючи базовий варіант "Усім підписникам (Tier 0+)"
            var tierOptions = new List<SelectListItem>
            {
                new SelectListItem { Value = "0", Text = "Усім підписникам (Tier 0+)" }
            };
            tierOptions.AddRange(tiers.Select(t => new SelectListItem { Value = t.Level.ToString(), Text = $"Лише Tier {t.Level} ({t.Name}) і вище" }));

            ViewBag.Tiers = tierOptions;

            return View();
        }

        // POST: Posts/Create
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
                return RedirectToAction(nameof(Index));
            }

            return Forbid();
        }

        // GET: Posts/Edit/5
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

            // ДІСТАЄМО ТІРИ З БАЗИ ДЛЯ ВИТЯЖНОГО СПИСКУ
            var tiers = await _context.SubscriptionTiers.OrderBy(t => t.Level).ToListAsync();
            var tierOptions = new List<SelectListItem>
            {
                new SelectListItem { Value = "0", Text = "Усім підписникам (Tier 0+)" }
            };
            tierOptions.AddRange(tiers.Select(t => new SelectListItem { Value = t.Level.ToString(), Text = $"Лише Tier {t.Level} ({t.Name}) і вище" }));

            ViewBag.Tiers = tierOptions;

            return View(post);
        }

        // POST: Posts/Edit/5
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

                existingPost.Title = post.Title;
                existingPost.ContentMarkdown = post.ContentMarkdown;
                existingPost.IsPublished = post.IsPublished;
                existingPost.RequiredTierLevel = post.RequiredTierLevel;

                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!_context.Posts.Any(e => e.Id == post.Id)) return NotFound();
                else throw;
            }

            return RedirectToAction(nameof(Index));
        }

        // GET: Posts/Delete/5
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

        // POST: Posts/Delete/5
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