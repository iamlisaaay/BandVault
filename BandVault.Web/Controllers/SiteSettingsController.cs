using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BandVault.Web.Data;
using BandVault.Web.Models;

namespace BandVault.Web.Controllers
{
   
    [Authorize]
    public class SiteSettingsController : Controller
    {
        private readonly ApplicationDbContext _context;

        public SiteSettingsController(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            
            return View(await _context.SiteSettings.OrderBy(s => s.Key).ToListAsync());
        }

       
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();

            var siteSetting = await _context.SiteSettings.FindAsync(id);
            if (siteSetting == null) return NotFound();

            return View(siteSetting);
        }

       
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