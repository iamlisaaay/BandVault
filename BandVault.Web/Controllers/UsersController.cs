using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BandVault.Web.Data;
using BandVault.Web.Models;

namespace BandVault.Web.Controllers
{
    [Authorize]
    public class UsersController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _env;

        public UsersController(ApplicationDbContext context, IWebHostEnvironment env)
        {
            _context = context;
            _env = env;
        }

        // GET: /Users/Details або /Users/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            User? targetUser;
            bool isOwnProfile = false;

            var currentEmail = User.Identity?.Name;
            var currentUser = await _context.Users
                .Include(u => u.SubscriptionTier)
                .FirstOrDefaultAsync(u => u.Email == currentEmail);

            if (currentUser == null) return NotFound();

            if (id == null || id == currentUser.Id)
            {
                targetUser = currentUser;
                isOwnProfile = true;
            }
            else
            {

                targetUser = await _context.Users
                    .Include(u => u.SubscriptionTier)
                    .FirstOrDefaultAsync(u => u.Id == id);

                if (targetUser == null) return NotFound();
            }


            if (string.IsNullOrEmpty(targetUser.AvatarUrl))
            {
                targetUser.AvatarUrl = $"https://via.placeholder.com/150/1a1a1a/ffffff?text={targetUser.DisplayName.Substring(0, 1).ToUpper()}";
            }


            if (targetUser.IsAdmin)
            {
                targetUser.SubscriptionTier = new SubscriptionTier
                {
                    Level = 999,
                    Name = "ALL ACCESS"
                };
            }

            ViewBag.IsOwnProfile = isOwnProfile;
            return View(targetUser);
        }

        // POST: /Users/EditProfile
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditProfile(string displayName, string? bio, IFormFile? avatarFile)
        {
            var currentEmail = User.Identity?.Name;
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == currentEmail);

            if (user == null) return NotFound();

            user.DisplayName = displayName;
            user.Bio = bio;


            if (avatarFile != null && avatarFile.Length > 0)
            {
                string fileName = Guid.NewGuid().ToString() + Path.GetExtension(avatarFile.FileName);
                string uploadPath = Path.Combine(_env.WebRootPath, "images", "avatars");
                Directory.CreateDirectory(uploadPath);

                using (var stream = new FileStream(Path.Combine(uploadPath, fileName), FileMode.Create))
                {
                    await avatarFile.CopyToAsync(stream);
                }
                user.AvatarUrl = "/images/avatars/" + fileName;
            }

            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Details));
        }

       
        [HttpPost]
        public async Task<IActionResult> ChangeTier(int tierId)
        {
            var currentEmail = User.Identity?.Name;
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == currentEmail);

            if (user == null) return RedirectToAction("Login", "Auth");

        
            var tier = await _context.SubscriptionTiers.FindAsync(tierId);
            if (tier != null)
            {
                user.SubscriptionTierId = tier.Id;
                await _context.SaveChangesAsync();
            }

          
            return RedirectToAction("Index", "Home");
        }
    }
}