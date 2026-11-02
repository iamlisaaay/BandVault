using BandVault.Web.Data;
using BandVault.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BandVault.Web.Controllers.Api
{
    // ============================================
    // C10: ЗАЛИШКИ ЛІМІТОВАНОГО МЕРЧУ - Polls, Adaptive Polls, Long Poll
    // (WebSocket - /ws/stock у Program.cs, SignalR - /stockHub)
    // ============================================
    [Route("api/stock")]
    [ApiController]
    public class StockApiController : ControllerBase
    {
        private static readonly TimeSpan LongPollTimeout = TimeSpan.FromSeconds(25);

        private readonly StockNotifier _stock;
        private readonly ApplicationDbContext _context;
        private readonly CatalogChangeNotifier _catalog;

        public StockApiController(StockNotifier stock, ApplicationDbContext context, CatalogChangeNotifier catalog)
        {
            _stock = stock;
            _context = context;
            _catalog = catalog;
        }

        // FREQUENT / ADAPTIVE POLLS: знімок з ETag. Якщо клієнт надіслав If-None-Match з тією ж версією - 304 без тіла
        [HttpGet]
        public async Task<IActionResult> GetSnapshot()
        {
            var snapshot = await _stock.GetAsync();
            var etag = $"\"{snapshot.Version}\"";
            Response.Headers.ETag = etag;
            Response.Headers.CacheControl = "no-cache";

            if (Request.Headers.IfNoneMatch.ToString() == etag)
            {
                return StatusCode(StatusCodes.Status304NotModified);
            }
            return Ok(snapshot);
        }

        // LONG POLL: запит висить до появи нової версії (200) або до тайм-ауту (204 - клієнт одразу перепідключається)
        [HttpGet("longpoll")]
        public async Task<IActionResult> LongPoll([FromQuery] long version = 0)
        {
            var snapshot = await _stock.WaitForChangeAsync(version, LongPollTimeout, HttpContext.RequestAborted);
            if (snapshot == null) return NoContent();
            return Ok(snapshot);
        }

        // Демо-генератор змін для порівняння способів: "продає" 1 шт. випадкового товару (або поповнює склад)
        [HttpPost("simulate")]
        public async Task<IActionResult> Simulate()
        {
            if (!User.HasClaim("IsAdmin", "true")) return Forbid();

            var items = await _context.MerchItems.ToListAsync();
            if (items.Count == 0) return NotFound("Немає товарів для симуляції");

            var inStock = items.Where(i => (i.StockQuantity ?? 0) > 0).ToList();
            if (inStock.Count == 0)
            {
                foreach (var item in items) item.StockQuantity = 10;
            }
            else
            {
                var item = inStock[Random.Shared.Next(inStock.Count)];
                item.StockQuantity = (item.StockQuantity ?? 0) - 1;
            }

            await _context.SaveChangesAsync();
            await _catalog.MerchChangedAsync();
            return Ok(await _stock.GetAsync());
        }
    }
}
