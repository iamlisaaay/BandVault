using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BandVault.Web.Data;
using BandVault.Web.Models;

namespace BandVault.Web.Controllers
{
    // TODO: Пізніше розкоментуєш атрибут авторизації, щоб сюди міг зайти тільки адмін
    // [Authorize] 
    public class SiteSettingsController : Controller
    {
        private readonly ApplicationDbContext _context;

        public SiteSettingsController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: Сторінка зі списком усіх налаштувань
        public async Task<IActionResult> Index()
        {
            // Виводимо всі налаштування, відсортовані за алфавітом
            return View(await _context.SiteSettings.OrderBy(s => s.Key).ToListAsync());
        }

        // GET: Відкриваємо форму редагування
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();

            var siteSetting = await _context.SiteSettings.FindAsync(id);
            if (siteSetting == null) return NotFound();

            return View(siteSetting);
        }

        // POST: Зберігаємо нове значення в базу
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("Id,Key,Value")] SiteSetting siteSetting)
        {
            if (id != siteSetting.Id) return NotFound();

            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(siteSetting);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!SiteSettingExists(siteSetting.Id)) return NotFound();
                    else throw;
                }
                // Після успішного збереження повертаємося до таблиці
                return RedirectToAction(nameof(Index));
            }
            return View(siteSetting);
        }

        private bool SiteSettingExists(int id)
        {
            return _context.SiteSettings.Any(e => e.Id == id);
        }
    }
}