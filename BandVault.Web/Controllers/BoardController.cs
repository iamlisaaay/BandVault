using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BandVault.Web.Controllers
{
    // C18: Спільна дошка автографів (логіка синхронізації - Hubs/BoardHub.cs)
    [Authorize]
    public class BoardController : Controller
    {
        public IActionResult Index()
        {
            ViewBag.IsAdmin = User.HasClaim("IsAdmin", "true");
            return View();
        }
    }
}
