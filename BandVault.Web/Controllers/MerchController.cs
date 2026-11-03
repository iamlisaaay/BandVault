using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using BandVault.Web.Data;
using BandVault.Web.Models;
using BandVault.Web.Services;

namespace BandVault.Web.Controllers
{
    public class MerchController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _env;
        private readonly IFileService _fileService;
        private readonly CatalogChangeNotifier _catalog;
        private readonly FeatureFlags _features;

        public MerchController(ApplicationDbContext context, IWebHostEnvironment env, IFileService fileService, CatalogChangeNotifier catalog, FeatureFlags features)
        {
            _features = features;
            _context = context;
            _env = env;
            _fileService = fileService;
            _catalog = catalog;
        }

        // C10: Моніторинг залишків лімітованого мерчу в реальному часі
        public IActionResult Live()
        {
            return View();
        }

        private async Task OnMerchChangedAsync()
        {
            await _catalog.MerchChangedAsync();

        }

        public async Task<IActionResult> Index()
        {
            var merch = await _context.MerchItems.Include(m => m.Category).ToListAsync();

            // B6: знижені ціни бачать лише плани з прапорцем fan_prices
            ViewBag.FanPrices = await _features.IsEnabledAsync(Features.FanPrices);

            return View(merch);
        }

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
                // C19: Використання Azure Blob Storage
                if (imageFile != null && imageFile.Length > 0)
                {
                    merchItem.ImageUrl = await _fileService.UploadFileAsync(imageFile, "merch");
                }

                _context.MerchItems.Add(merchItem);
                await _context.SaveChangesAsync();
                await OnMerchChangedAsync();
                return RedirectToAction(nameof(Index));
            }

            // При помилці валідації повертаємо список
            ViewBag.Categories = new SelectList(_context.MerchCategories, "Id", "Name", merchItem.CategoryId);
            // 422: Turbo показує форму з помилками без перезавантаження сторінки
            Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
            return View(merchItem);
        }

        [Authorize]
        public async Task<IActionResult> Edit(int? id)
        {
            if (!User.HasClaim("IsAdmin", "true")) return Forbid();

            if (id == null) return NotFound();

            var merchItem = await _context.MerchItems
                .Include(m => m.Category)
                .FirstOrDefaultAsync(m => m.Id == id);

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

                    // C19: Використання Azure Blob Storage
                    if (imageFile != null && imageFile.Length > 0)
                    {
                        // Якщо є старий файл, його можна видалити
                        if (!string.IsNullOrEmpty(existingItem.ImageUrl))
                        {
                            await _fileService.DeleteFileAsync(existingItem.ImageUrl);
                        }
                        existingItem.ImageUrl = await _fileService.UploadFileAsync(imageFile, "merch");
                    }

                    await _context.SaveChangesAsync();
                    await OnMerchChangedAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!_context.MerchItems.Any(e => e.Id == merchItem.Id)) return NotFound();
                    else throw;
                }
                return RedirectToAction(nameof(Index));
            }

            // ФІКС: Підвантажуємо категорію, якщо була помилка валідації, щоб Select2 не був порожнім
            merchItem.Category = await _context.MerchCategories.FindAsync(merchItem.CategoryId);
            ViewBag.Categories = new SelectList(_context.MerchCategories, "Id", "Name", merchItem.CategoryId);
            Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
            return View(merchItem);
        }

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
            // Товар з історії замовлень не видаляємо (інакше зникнуть рядки старих замовлень) - знімаємо з продажу
            if (merchItem != null && await _context.OrderItems.AnyAsync(oi => oi.MerchItemId == id))
            {
                merchItem.StockQuantity = 0;
                await _context.SaveChangesAsync();
                await OnMerchChangedAsync();
                TempData["MerchMessage"] = $"«{merchItem.Title}» є в замовленнях, тому його знято з продажу (залишок 0), а не видалено.";
                return RedirectToAction(nameof(Index));
            }
            if (merchItem != null)
            {
                // Опціонально: видаляємо фото з Azure при видаленні товару
                if (!string.IsNullOrEmpty(merchItem.ImageUrl))
                {
                    await _fileService.DeleteFileAsync(merchItem.ImageUrl);
                }

                _context.MerchItems.Remove(merchItem);
                await _context.SaveChangesAsync();
                await OnMerchChangedAsync();
            }
            return RedirectToAction(nameof(Index));
        }

        public async Task<IActionResult> Details(int? id)
        {
            if (id == null) return NotFound();

            var merchItem = await _context.MerchItems
                .Include(m => m.Category)
                .FirstOrDefaultAsync(m => m.Id == id);

            if (merchItem == null) return NotFound();

            // B6: знижені ціни бачать лише плани з прапорцем fan_prices
            ViewBag.FanPrices = await _features.IsEnabledAsync(Features.FanPrices);

            // C6: блок "Схожі товари" на лендінгу товару
            ViewBag.Related = await _context.MerchItems
                .Where(m => m.Id != merchItem.Id && m.CategoryId == merchItem.CategoryId)
                .OrderByDescending(m => m.StockQuantity)
                .Take(3)
                .ToListAsync();

            return View(merchItem);
        }
    }
}