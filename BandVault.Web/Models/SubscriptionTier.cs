namespace BandVault.Web.Models
{
    public class SubscriptionTier
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public int Level { get; set; }
        public bool IsRecommended { get; set; }
        
        public ICollection<TierFeature> Features { get; set; } = new List<TierFeature>();
        public ICollection<User> Users { get; set; } = new List<User>();
    }
}