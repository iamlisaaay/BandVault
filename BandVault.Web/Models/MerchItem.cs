using System.ComponentModel.DataAnnotations;
namespace BandVault.Web.Models
{
    public class MerchItem
    {
        public int Id { get; set; }
        [Required(ErrorMessage = "Назва обов'язкова")]
        public string Title { get; set; } = string.Empty;
        [Required(ErrorMessage = "Опис обов'язковий")]
        public string Description { get; set; } = string.Empty;

        [Range(0.01, 100000, ErrorMessage = "Ціна має бути більше 0")]
        public decimal Price { get; set; }
        public int? StockQuantity { get; set; }
        public string? ImageUrl { get; set; }

        [Required(ErrorMessage = "Категорія обов'язкова")]
        public int CategoryId { get; set; }
        public MerchCategory? Category { get; set; }

    }
}