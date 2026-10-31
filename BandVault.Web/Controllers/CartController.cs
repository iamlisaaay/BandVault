using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BandVault.Web.Data;
using BandVault.Web.Models;
using System.Text.Json;

namespace BandVault.Web.Controllers
{
    public class CartController : Controller
    {
        private readonly ApplicationDbContext _context;

        public CartController(ApplicationDbContext context)
        {
            _context = context;
        }

        public class CartItemDto
        {
            public int MerchItemId { get; set; }
            public string Title { get; set; } = string.Empty;
            public decimal Price { get; set; }
            public string? ImageUrl { get; set; }
            public int Quantity { get; set; }
        }

        private List<CartItemDto> GetCart()
        {
            var cartJson = HttpContext.Session.GetString("Cart");
            if (string.IsNullOrEmpty(cartJson)) return new List<CartItemDto>();

            // ФІКС: Додали налаштування для правильного читання
            var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            return JsonSerializer.Deserialize<List<CartItemDto>>(cartJson, jsonOptions) ?? new List<CartItemDto>();
        }

        private void SaveCart(List<CartItemDto> cart)
        {
            var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            HttpContext.Session.SetString("Cart", JsonSerializer.Serialize(cart, jsonOptions));
        }

        public IActionResult Index()
        {
            var cart = GetCart();
            ViewBag.Total = cart.Sum(i => i.Price * i.Quantity);
            return View(cart);
        }

        [HttpPost]
        public async Task<IActionResult> AddToCart(int merchId, int quantity = 1)
        {
            // ФІКС: Захист, щоб не можна було додати менше 1 товару
            if (quantity < 1) quantity = 1;

            var merch = await _context.MerchItems.FindAsync(merchId);
            if (merch == null || merch.StockQuantity < quantity) return NotFound();

            var cart = GetCart();
            var existingItem = cart.FirstOrDefault(i => i.MerchItemId == merchId);

            decimal actualPrice = merch.Price;
            if (User.Identity != null && User.Identity.IsAuthenticated && merch.FanPrice.HasValue)
            {
                actualPrice = merch.FanPrice.Value;
            }

            if (existingItem != null)
            {
                existingItem.Quantity += quantity;
            }
            else
            {
                cart.Add(new CartItemDto
                {
                    MerchItemId = merch.Id,
                    Title = merch.Title,
                    Price = actualPrice,
                    ImageUrl = merch.ImageUrl,
                    Quantity = quantity
                });
            }

            SaveCart(cart);
            return RedirectToAction(nameof(Index));
        }

        public IActionResult RemoveFromCart(int merchId)
        {
            var cart = GetCart();
            var item = cart.FirstOrDefault(i => i.MerchItemId == merchId);
            if (item != null)
            {
                cart.Remove(item);
                SaveCart(cart);
            }
            return RedirectToAction(nameof(Index));
        }
    }
}