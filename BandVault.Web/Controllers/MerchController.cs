using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using BandVault.Web.Data;
using BandVault.Web.Models;

namespace BandVault.Web.Controllers
{
    public class MerchController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _env;

        public MerchController(ApplicationDbContext context, IWebHostEnvironment env)
        {
            _context = context;
            _env = env;
        }

        // GET: /Merch/
        public async Task<IActionResult> Index()
        {
            var merch = await _context.MerchItems.Include(m => m.Category).ToListAsync();
            return View(merch);
        }


        // CREATE
       
        [Authorize]
        public IActionResult Create()
        {
            if (!User.HasClaim("IsAdmin", "true")) return Forbid();

          
            ViewBag.Categories = new SelectList(_context.MerchCategories, "Id", "Name");
            return View();
        }

        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(MerchItem merchItem, IFormFile? imageFile)
        {
            if (!User.HasClaim("IsAdmin", "true")) return Forbid();

            if (ModelState.IsValid)
            {
            
                if (imageFile != null && imageFile.Length > 0)
                {
                    string fileName = Guid.NewGuid().ToString() + Path.GetExtension(imageFile.FileName);
                    string uploadPath = Path.Combine(_env.WebRootPath, "images", "merch");
                    Directory.CreateDirectory(uploadPath);

                    using (var stream = new FileStream(Path.Combine(uploadPath, fileName), FileMode.Create))
                    {
                        await imageFile.CopyToAsync(stream);
                    }
                    merchItem.ImageUrl = "/images/merch/" + fileName;
                }

                _context.MerchItems.Add(merchItem);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            ViewBag.Categories = new SelectList(_context.MerchCategories, "Id", "Name", merchItem.CategoryId);
            return View(merchItem);
        }


        // EDIT
    
        [Authorize]
        public async Task<IActionResult> Edit(int? id)
        {
            if (!User.HasClaim("IsAdmin", "true")) return Forbid();

            if (id == null) return NotFound();

            var merchItem = await _context.MerchItems.FindAsync(id);
            if (merchItem == null) return NotFound();

            ViewBag.Categories = new SelectList(_context.MerchCategories, "Id", "Name", merchItem.CategoryId);
            return View(merchItem);
        }

        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, MerchItem merchItem, IFormFile? imageFile)
        {
            if (!User.HasClaim("IsAdmin", "true")) return Forbid();

            if (id != merchItem.Id) return NotFound();

            if (ModelState.IsValid)
            {
                try
                {
                    var existingItem = await _context.MerchItems.FindAsync(id);
                    if (existingItem == null) return NotFound();

                    existingItem.Title = merchItem.Title;
                    existingItem.Description = merchItem.Description;
                    existingItem.Price = merchItem.Price;
                    existingItem.FanPrice = merchItem.FanPrice;
                    existingItem.StockQuantity = merchItem.StockQuantity;
                    existingItem.CategoryId = merchItem.CategoryId;

                 
                    if (imageFile != null && imageFile.Length > 0)
                    {
                        string fileName = Guid.NewGuid().ToString() + Path.GetExtension(imageFile.FileName);
                        string uploadPath = Path.Combine(_env.WebRootPath, "images", "merch");
                        Directory.CreateDirectory(uploadPath);

                        using (var stream = new FileStream(Path.Combine(uploadPath, fileName), FileMode.Create))
                        {
                            await imageFile.CopyToAsync(stream);
                        }
                        existingItem.ImageUrl = "/images/merch/" + fileName;
                    }

                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!_context.MerchItems.Any(e => e.Id == merchItem.Id)) return NotFound();
                    else throw;
                }
                return RedirectToAction(nameof(Index));
            }
            ViewBag.Categories = new SelectList(_context.MerchCategories, "Id", "Name", merchItem.CategoryId);
            return View(merchItem);
        }

        
        // DELETE
   
        [Authorize]
        public async Task<IActionResult> Delete(int? id)
        {
            if (!User.HasClaim("IsAdmin", "true")) return Forbid();

            if (id == null) return NotFound();

            var merchItem = await _context.MerchItems
                .Include(m => m.Category)
                .FirstOrDefaultAsync(m => m.Id == id);

            if (merchItem == null) return NotFound();

            return View(merchItem);
        }

        [HttpPost, ActionName("Delete")]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            if (!User.HasClaim("IsAdmin", "true")) return Forbid();

            var merchItem = await _context.MerchItems.FindAsync(id);
            if (merchItem != null)
            {
                // Опціонально: можна видаляти і сам файл картинки з диска
                _context.MerchItems.Remove(merchItem);
                await _context.SaveChangesAsync();
            }
            return RedirectToAction(nameof(Index));
        }

        // GET: /Merch/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null) return NotFound();

            var merchItem = await _context.MerchItems
                .Include(m => m.Category)
                .FirstOrDefaultAsync(m => m.Id == id);

            if (merchItem == null) return NotFound();

            return View(merchItem);
        }
    }
}