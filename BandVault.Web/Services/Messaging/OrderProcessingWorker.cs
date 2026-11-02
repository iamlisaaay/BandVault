using System.Globalization;
using BandVault.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace BandVault.Web.Services.Messaging
{
    // ============================================
    // B8: ФОНОВИЙ СПОЖИВАЧ ЧЕРГИ ЗАМОВЛЕНЬ
    // 1) генерує PDF-інвойс і кладе його у сховище (Blob) -> Order.InvoiceUrl
    // 2) надсилає сповіщення про нове замовлення в Telegram (B2)
    // Запит користувача на оформлення не чекає на ці повільні операції
    // ============================================
    public class OrderProcessingWorker : BackgroundService
    {
        private readonly OrderQueue _queue;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly TelegramNotifier _telegram;
        private readonly ILogger<OrderProcessingWorker> _logger;

        public OrderProcessingWorker(OrderQueue queue, IServiceScopeFactory scopeFactory, TelegramNotifier telegram, ILogger<OrderProcessingWorker> logger)
        {
            _queue = queue;
            _scopeFactory = scopeFactory;
            _telegram = telegram;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                await _queue.ConsumeAsync(HandleAsync, stoppingToken);
            }
            catch (OperationCanceledException) { }
        }

        private async Task HandleAsync(OrderPlacedMessage message)
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var files = scope.ServiceProvider.GetRequiredService<IFileService>();

            var order = await context.Orders
                .Include(o => o.OrderItems).ThenInclude(oi => oi.MerchItem)
                .FirstOrDefaultAsync(o => o.Id == message.OrderId);
            if (order == null)
            {
                _logger.LogWarning("Замовлення #{OrderId} з черги не знайдено", message.OrderId);
                return;
            }

            // Ідемпотентність: повторна доставка повідомлення не створює другий інвойс
            if (string.IsNullOrEmpty(order.InvoiceUrl))
            {
                var bandName = await context.SiteSettings.Where(s => s.Key == "BandName").Select(s => s.Value).FirstOrDefaultAsync() ?? "BandVault";
                var pdf = InvoicePdfGenerator.Generate(order, bandName);
                order.InvoiceUrl = await files.UploadBytesAsync(pdf, "invoices", ".pdf", "application/pdf");
                await context.SaveChangesAsync();
                _logger.LogInformation("Інвойс для замовлення #{OrderId} згенеровано: {Url}", order.Id, order.InvoiceUrl);

                var items = string.Join("\n", order.OrderItems.Select(i => $"• {i.Quantity} x {TelegramNotifier.Encode(i.MerchItem?.Title)}"));
                await _telegram.SendHtmlAsync(
                    $"🛒 <b>Нове замовлення #{order.Id}</b>\n\n{items}\n\n" +
                    // Персональні дані покупця (адреса, телефон) у канал не відправляємо
                    $"💰 <b>{order.TotalAmount.ToString("0.00", CultureInfo.InvariantCulture)} $</b> · {TelegramNotifier.Encode(order.PaymentMethod)}");
            }
        }
    }
}
