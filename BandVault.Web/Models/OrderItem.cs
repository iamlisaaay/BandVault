namespace BandVault.Web.Models
{
    public class OrderItem
    {
        public int Id { get; set; }
        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; }

        public int OrderId { get; set; }
        public Order? Order { get; set; }

        public int MerchItemId { get; set; }
        public MerchItem? MerchItem { get; set; }
    }
}