using System.ComponentModel.DataAnnotations;

namespace BandVault.Web.Models
{
    public class Order
    {
        public int Id { get; set; }
        public DateTime OrderDate { get; set; } = DateTime.UtcNow;
        public decimal TotalAmount { get; set; }

        // Нові поля для оформлення
        [Required] public string CustomerName { get; set; } = string.Empty;
        [Required] public string CustomerEmail { get; set; } = string.Empty;
        [Required] public string CustomerPhone { get; set; } = string.Empty;
        [Required] public string PaymentMethod { get; set; } = "Готівкою кур'єру";

        [Required] public string DeliveryAddress { get; set; } = string.Empty;
        public double DeliveryLat { get; set; }
        public double DeliveryLng { get; set; }
        public string? InvoiceUrl { get; set; }

        public int UserId { get; set; }
        public User? User { get; set; }

        public int StatusId { get; set; }
        public OrderStatus? Status { get; set; }

        public ICollection<OrderItem> OrderItems { get; set; } = new List<OrderItem>();
    }
}