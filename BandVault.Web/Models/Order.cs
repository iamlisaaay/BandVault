namespace BandVault.Web.Models
{
    public class Order
    {
        public int Id { get; set; }
        public DateTime OrderDate { get; set; } = DateTime.UtcNow;
        public decimal TotalAmount { get; set; }
        public string DeliveryAddress { get; set; } = string.Empty;
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