using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BandVault.Web.Data;
using BandVault.Web.Models;
using System.Text.Json;

namespace BandVault.Web.Controllers
{
    [Authorize]
    public class OrdersController : Controller
    {
        private readonly ApplicationDbContext _context;

        public OrdersController(ApplicationDbContext context)
        {
            _context = context;
        }

        // 1. СТОРІНКА ОФОРМЛЕННЯ ЗАМОВЛЕННЯ
        public IActionResult Checkout()
        {
            var cartJson = HttpContext.Session.GetString("Cart");
            if (string.IsNullOrEmpty(cartJson) || cartJson == "[]") return RedirectToAction("Index", "Cart");

            var cart = JsonSerializer.Deserialize<List<CartController.CartItemDto>>(cartJson, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            ViewBag.Total = cart?.Sum(i => i.Price * i.Quantity) ?? 0;

            return View(cart);
        }

        // 2. ЗБЕРЕЖЕННЯ ЗАМОВЛЕННЯ В БД
        [HttpPost]
        public async Task<IActionResult> PlaceOrder(string address, double lat, double lng, string customerName, string customerEmail, string customerPhone, string paymentMethod)
        {
            var cartJson = HttpContext.Session.GetString("Cart");
            if (string.IsNullOrEmpty(cartJson) || cartJson == "[]") return RedirectToAction("Index", "Cart");
            var cart = JsonSerializer.Deserialize<List<CartController.CartItemDto>>(cartJson, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

            var userEmail = User.Identity?.Name;
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == userEmail);
            if (user == null || cart == null) return Forbid();

            // Перевіряємо чи є такий статус в БД
            var status = await _context.OrderStatuses.FirstOrDefaultAsync(s => s.Name == "New");
            if (status == null)
            {
                status = new OrderStatus { Name = "New" };
                _context.OrderStatuses.Add(status);
                await _context.SaveChangesAsync();
            }

            var order = new Order
            {
                UserId = user.Id,
                OrderDate = DateTime.UtcNow,
                TotalAmount = cart.Sum(i => i.Price * i.Quantity),
                DeliveryAddress = address ?? "Адресу не вказано",
                DeliveryLat = lat,
                DeliveryLng = lng,
                CustomerName = customerName ?? user.DisplayName,
                CustomerEmail = customerEmail ?? user.Email,
                CustomerPhone = customerPhone ?? "-",
                PaymentMethod = paymentMethod ?? "Готівкою кур'єру",
                StatusId = status.Id
            };

            foreach (var item in cart)
            {
                order.OrderItems.Add(new OrderItem
                {
                    MerchItemId = item.MerchItemId,
                    Quantity = item.Quantity,
                    UnitPrice = item.Price
                });

                // Списуємо зі складу
                var merch = await _context.MerchItems.FindAsync(item.MerchItemId);
                if (merch != null) merch.StockQuantity -= item.Quantity;
            }

            _context.Orders.Add(order);
            await _context.SaveChangesAsync();

            HttpContext.Session.Remove("Cart"); // Очищаємо кошик

            return View("Success", order.Id);
        }

        // 3. ІНТЕРАКТИВНА МАПА ЗАМОВЛЕНЬ (ЗАДАЧА C8)
        public async Task<IActionResult> AdminMap()
        {
            if (!User.HasClaim("IsAdmin", "true")) return Forbid();

            var orders = await _context.Orders
                .Include(o => o.User)
                .Include(o => o.Status)
                .OrderByDescending(o => o.OrderDate)
                .Select(o => new
                {
                    id = o.Id,
                    lat = o.DeliveryLat,
                    lng = o.DeliveryLng,
                    address = o.DeliveryAddress,
                    total = o.TotalAmount,
                    status = o.Status!.Name,
                    user = o.CustomerName,
                    date = o.OrderDate.ToString("dd.MM.yyyy")
                })
                .ToListAsync();

            ViewBag.OrdersJson = JsonSerializer.Serialize(orders, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            return View();
        }
    }
}