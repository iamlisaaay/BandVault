using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BandVault.Web.Data;
using BandVault.Web.Models;
using BandVault.Web.Services;
using BandVault.Web.Services.Messaging;
using System.Globalization;
using System.Text.Json;

namespace BandVault.Web.Controllers
{
    [Authorize]
    public class OrdersController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly OrderQueue _orderQueue;
        private readonly CatalogChangeNotifier _catalog;

        public OrdersController(ApplicationDbContext context, OrderQueue orderQueue, CatalogChangeNotifier catalog)
        {
            _context = context;
            _orderQueue = orderQueue;
            _catalog = catalog;
        }

        // B8: сторінка успіху опитує, чи вже згенерований інвойс фоновим обробником
        [HttpGet]
        public async Task<IActionResult> InvoiceStatus(int id)
        {
            var order = await _context.Orders.Include(o => o.User).FirstOrDefaultAsync(o => o.Id == id);
            if (order == null) return NotFound();
            if (order.User?.Email != User.Identity?.Name && !User.HasClaim("IsAdmin", "true")) return Forbid();
            return Json(new { ready = !string.IsNullOrEmpty(order.InvoiceUrl), url = order.InvoiceUrl });
        }

        public class UpdateOrderStatusDto
        {
            public int OrderId { get; set; }
            public string Status { get; set; } = "New";
        }

        // 1. СТОРІНКА ОФОРМЛЕННЯ ЗАМОВЛЕННЯ
        public async Task<IActionResult> Checkout()
        {
            var cartJson = HttpContext.Session.GetString("Cart");
            if (string.IsNullOrEmpty(cartJson) || cartJson == "[]") return RedirectToAction("Index", "Cart");

            var cart = JsonSerializer.Deserialize<List<CartController.CartItemDto>>(
                cartJson,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

            if (cart == null || !cart.Any()) return RedirectToAction("Index", "Cart");

            var userEmail = User.Identity?.Name;
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == userEmail);

            ViewBag.DefaultName = user?.DisplayName ?? "";
            ViewBag.DefaultEmail = user?.Email ?? userEmail ?? "";
            ViewBag.DefaultPhone = user?.PhoneNum ?? "";
            ViewBag.Total = cart.Sum(i => i.Price * i.Quantity);

            return View(cart);
        }

        // 2. ЗБЕРЕЖЕННЯ ЗАМОВЛЕННЯ В БД
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> PlaceOrder(
            string address,
            string? lat,
            string? lng,
            string customerName,
            string customerEmail,
            string customerPhone,
            string paymentMethod)
        {
            var cartJson = HttpContext.Session.GetString("Cart");
            if (string.IsNullOrEmpty(cartJson) || cartJson == "[]") return RedirectToAction("Index", "Cart");

            var cart = JsonSerializer.Deserialize<List<CartController.CartItemDto>>(
                cartJson,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

            var userEmail = User.Identity?.Name;
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == userEmail);
            if (user == null || cart == null || !cart.Any()) return Forbid();

            // ФІКС: Надійний парсинг координат незалежно від локалі ОС (крапка чи кома)
            // Без координат замовлення не потрапляє в «Київ за замовчуванням» - на мапі воно буде позначене окремо
            double parsedLat = 0;
            double parsedLng = 0;
            if (!string.IsNullOrWhiteSpace(lat) &&
                double.TryParse(lat.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var l1))
            {
                parsedLat = l1;
            }
            if (!string.IsNullOrWhiteSpace(lng) &&
                double.TryParse(lng.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var l2))
            {
                parsedLng = l2;
            }

            // Перевіряємо залишки всіх товарів перед оформленням
            foreach (var item in cart)
            {
                var merch = await _context.MerchItems.FindAsync(item.MerchItemId);
                if (merch == null || (merch.StockQuantity ?? 0) < item.Quantity)
                {
                    TempData["CartError"] = $"Товару «{item.Title}» недостатньо на складі.";
                    return RedirectToAction("Index", "Cart");
                }
            }

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
                DeliveryAddress = string.IsNullOrWhiteSpace(address) ? "Адресу не вказано" : address.Trim(),
                DeliveryLat = parsedLat,
                DeliveryLng = parsedLng,
                CustomerName = string.IsNullOrWhiteSpace(customerName) ? user.DisplayName : customerName.Trim(),
                CustomerEmail = string.IsNullOrWhiteSpace(customerEmail) ? user.Email : customerEmail.Trim(),
                CustomerPhone = string.IsNullOrWhiteSpace(customerPhone) ? "-" : customerPhone.Trim(),
                PaymentMethod = string.IsNullOrWhiteSpace(paymentMethod) ? "Готівкою кур'єру" : paymentMethod,
                StatusId = status.Id
            };

            // Списання залишків атомарне: UPDATE ... WHERE Stock >= qty в одній транзакції із замовленням.
            // Якщо інший покупець встиг забрати товар, рядок не оновиться - і все замовлення відкотиться.
            await using (var transaction = await _context.Database.BeginTransactionAsync())
            {
                foreach (var item in cart)
                {
                    order.OrderItems.Add(new OrderItem
                    {
                        MerchItemId = item.MerchItemId,
                        Quantity = item.Quantity,
                        UnitPrice = item.Price
                    });

                    int updated = await _context.MerchItems
                        .Where(m => m.Id == item.MerchItemId && m.StockQuantity >= item.Quantity)
                        .ExecuteUpdateAsync(s => s.SetProperty(m => m.StockQuantity, m => m.StockQuantity - item.Quantity));
                    if (updated == 0)
                    {
                        await transaction.RollbackAsync();
                        TempData["CartError"] = $"Товару «{item.Title}» вже недостатньо на складі.";
                        return RedirectToAction("Index", "Cart");
                    }
                }

                _context.Orders.Add(order);
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
            }

            HttpContext.Session.Remove("Cart");

            // B8: інвойс + Telegram - асинхронно через чергу; C10/B9: залишки змінились - розсилаємо клієнтам і скидаємо кеш
            await _orderQueue.PublishAsync(new OrderPlacedMessage(order.Id, order.OrderDate));
            await _catalog.MerchChangedAsync();

            return View("Success", order.Id);
        }

        // 3. ІНТЕРАКТИВНА МАПА ЗАМОВЛЕНЬ З ФІЛЬТРАЦІЄЮ ТА ДИНАМІКОЮ (ЗАДАЧА C8)
        public async Task<IActionResult> AdminMap()
        {
            if (!User.HasClaim("IsAdmin", "true")) return Forbid();

            var rawOrders = await _context.Orders
                .Include(o => o.Status)
                .Include(o => o.OrderItems)
                    .ThenInclude(oi => oi.MerchItem)
                .OrderByDescending(o => o.OrderDate)
                .ToListAsync();

            var orders = rawOrders.Select(o => new
            {
                id = o.Id,
                lat = o.DeliveryLat,
                lng = o.DeliveryLng,
                address = o.DeliveryAddress,
                total = o.TotalAmount,
                status = o.Status?.Name ?? "New",
                paymentMethod = o.PaymentMethod,
                user = o.CustomerName,
                phone = o.CustomerPhone,
                email = o.CustomerEmail,
                date = o.OrderDate.ToLocalTime().ToString("dd.MM.yyyy HH:mm"),
                items = string.Join(", ", o.OrderItems.Select(oi => $"{oi.Quantity}x {(oi.MerchItem?.Title ?? "Мерч")}")),
                invoiceUrl = o.InvoiceUrl
            }).ToList();

            ViewBag.OrdersJson = JsonSerializer.Serialize(
                orders,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

            return View();
        }

        // 4. ДИНАМІЧНА ЗМІНА СТАТУСУ ПРЯМО З МАПИ (AJAX)
        [HttpPost]
        [ValidateAntiForgeryToken] // токен передається заголовком RequestVerificationToken з мапи
        public async Task<IActionResult> UpdateStatus([FromBody] UpdateOrderStatusDto dto)
        {
            if (!User.HasClaim("IsAdmin", "true")) return Forbid();

            var allowed = new[] { "New", "Shipped", "Completed" };
            if (!allowed.Contains(dto.Status)) return BadRequest("Невідомий статус");

            var order = await _context.Orders.FindAsync(dto.OrderId);
            if (order == null) return NotFound();

            var status = await _context.OrderStatuses.FirstOrDefaultAsync(s => s.Name == dto.Status);
            if (status == null)
            {
                status = new OrderStatus { Name = dto.Status };
                _context.OrderStatuses.Add(status);
                await _context.SaveChangesAsync();
            }

            order.StatusId = status.Id;
            await _context.SaveChangesAsync();

            return Json(new { success = true, status = status.Name });
        }
    }
}