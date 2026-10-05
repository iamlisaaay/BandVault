namespace BandVault.Web.Models
{
    public class User
    {
        public int Id { get; set; }
        public string Email { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string? PhoneNum { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public bool IsAdmin { get; set; }
        public int SubscriptionTierId { get; set; }
        public SubscriptionTier? SubscriptionTier { get; set; }

        public ICollection<Order> Orders { get; set; } = new List<Order>();
        public ICollection<Post> Posts { get; set; } = new List<Post>();
    }
}