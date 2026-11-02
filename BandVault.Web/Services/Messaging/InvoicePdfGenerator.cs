using System.Globalization;
using BandVault.Web.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace BandVault.Web.Services.Messaging
{
    // B8: PDF-інвойс замовлення (генерується у фоні споживачем черги)
    public static class InvoicePdfGenerator
    {
        public static byte[] Generate(Order order, string bandName)
        {
            var culture = CultureInfo.InvariantCulture;

            return Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(40);
                    page.DefaultTextStyle(x => x.FontSize(10).FontColor("#111111"));

                    page.Header().Column(col =>
                    {
                        col.Item().Text(bandName.ToUpperInvariant()).FontSize(20).Bold().LetterSpacing(0.1f);
                        col.Item().Text($"РАХУНОК-ФАКТУРА № {order.Id}").FontSize(12).SemiBold();
                        col.Item().Text($"Дата: {order.OrderDate:dd.MM.yyyy HH:mm} UTC").FontColor("#666666");
                        col.Item().PaddingTop(10).LineHorizontal(1).LineColor("#000000");
                    });

                    page.Content().PaddingVertical(15).Column(col =>
                    {
                        col.Spacing(4);
                        col.Item().Text("ОТРИМУВАЧ").FontColor("#666666").FontSize(8).LetterSpacing(0.1f);
                        col.Item().Text(order.CustomerName).SemiBold();
                        col.Item().Text($"{order.CustomerEmail} · {order.CustomerPhone}");
                        col.Item().Text($"Адреса доставки: {order.DeliveryAddress}");
                        col.Item().Text($"Спосіб оплати: {order.PaymentMethod}");

                        col.Item().PaddingTop(15).Table(table =>
                        {
                            table.ColumnsDefinition(c =>
                            {
                                c.RelativeColumn(6);
                                c.RelativeColumn(1);
                                c.RelativeColumn(2);
                                c.RelativeColumn(2);
                            });

                            table.Header(h =>
                            {
                                h.Cell().BorderBottom(1).PaddingBottom(4).Text("ТОВАР").Bold();
                                h.Cell().BorderBottom(1).PaddingBottom(4).AlignRight().Text("К-СТЬ").Bold();
                                h.Cell().BorderBottom(1).PaddingBottom(4).AlignRight().Text("ЦІНА").Bold();
                                h.Cell().BorderBottom(1).PaddingBottom(4).AlignRight().Text("СУМА").Bold();
                            });

                            foreach (var item in order.OrderItems)
                            {
                                table.Cell().PaddingVertical(4).Text(item.MerchItem?.Title ?? $"Товар #{item.MerchItemId}");
                                table.Cell().PaddingVertical(4).AlignRight().Text(item.Quantity.ToString());
                                table.Cell().PaddingVertical(4).AlignRight().Text(item.UnitPrice.ToString("0.00", culture) + " $");
                                table.Cell().PaddingVertical(4).AlignRight().Text((item.UnitPrice * item.Quantity).ToString("0.00", culture) + " $");
                            }
                        });

                        col.Item().PaddingTop(10).AlignRight()
                            .Text($"ДО СПЛАТИ: {order.TotalAmount.ToString("0.00", culture)} $").FontSize(14).Bold();
                    });

                    page.Footer().AlignCenter().Text(t =>
                    {
                        t.Span("BandVault · офіційний мерч фан-клубу · сторінка ").FontColor("#888888").FontSize(8);
                        t.CurrentPageNumber().FontSize(8);
                    });
                });
            }).GeneratePdf();
        }
    }
}
