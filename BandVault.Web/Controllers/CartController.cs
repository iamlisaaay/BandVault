using Microsoft.AspNetCore.Mvc;
using BandVault.Web.Data;
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
            if (quantity < 1) quantity = 1;

            var merch = await _context.MerchItems.FindAsync(merchId);
            if (merch == null) return NotFound();

            var cart = GetCart();
            var existingItem = cart.FirstOrDefault(i => i.MerchItemId == merchId);

            int currentQty = existingItem?.Quantity ?? 0;
            int availableStock = merch.StockQuantity ?? 0;

            if (availableStock <= 0 || currentQty + quantity > availableStock)
            {
                TempData["CartError"] = $"На складі доступно лише {availableStock} шт. товару «{merch.Title}» (у кошику вже {currentQty} шт.).";
                return RedirectToAction(nameof(Index));
            }

            decimal actualPrice = merch.Price;
            if (User.Identity != null && User.Identity.IsAuthenticated && merch.FanPrice.HasValue)
            {
                actualPrice = merch.FanPrice.Value;
            }

            if (existingItem != null)
            {
                existingItem.Quantity += quantity;
                existingItem.Price = actualPrice;
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

        [HttpPost]
        public async Task<IActionResult> UpdateQuantity(int merchId, int delta)
        {
            var cart = GetCart();
            var item = cart.FirstOrDefault(i => i.MerchItemId == merchId);
            if (item == null) return RedirectToAction(nameof(Index));

            int newQty = item.Quantity + delta;
            if (newQty <= 0)
            {
                cart.Remove(item);
            }
            else
            {
                var merch = await _context.MerchItems.FindAsync(merchId);
                int availableStock = merch?.StockQuantity ?? 0;
                if (newQty > availableStock)
                {
                    TempData["CartError"] = $"Більше немає на складі (доступно {availableStock} шт.).";
                    item.Quantity = Math.Max(1, availableStock);
                }
                else
                {
                    item.Quantity = newQty;
                }
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