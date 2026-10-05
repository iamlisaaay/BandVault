namespace BandVault.Web.Models
{
    public class MerchItem
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public int StockQuantity { get; set; }
        public string? ImageUrl { get; set; }

        public int CategoryId { get; set; }
        public MerchCategory? Category { get; set; }
    }
}