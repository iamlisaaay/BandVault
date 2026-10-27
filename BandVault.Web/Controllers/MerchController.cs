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

        // ==========================================
        // CREATE
        // ==========================================
        public IActionResult Create()
        {
            // Передаємо список категорій у View для випадаючого списку
            ViewBag.Categories = new SelectList(_context.MerchCategories, "Id", "Name");
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(MerchItem merchItem, IFormFile? imageFile)
        {
            if (ModelState.IsValid)
            {
                // Завантаження фото
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

        // ==========================================
        // EDIT
        // ==========================================
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();

            var merchItem = await _context.MerchItems.FindAsync(id);
            if (merchItem == null) return NotFound();

            ViewBag.Categories = new SelectList(_context.MerchCategories, "Id", "Name", merchItem.CategoryId);
            return View(merchItem);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, MerchItem merchItem, IFormFile? imageFile)
        {
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
                    existingItem.StockQuantity = merchItem.StockQuantity;
                    existingItem.CategoryId = merchItem.CategoryId;

                    // Якщо завантажили нове фото
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

        // ==========================================
        // DELETE
        // ==========================================
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null) return NotFound();

            var merchItem = await _context.MerchItems
                .Include(m => m.Category)
                .FirstOrDefaultAsync(m => m.Id == id);

            if (merchItem == null) return NotFound();

            return View(merchItem);
        }

        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
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