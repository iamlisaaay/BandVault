namespace BandVault.Web.Models
{
    public class TierFeature
    {
        public int Id { get; set; }
        public string Description { get; set; } = string.Empty;

        public int TierId { get; set; }
        public SubscriptionTier? Tier { get; set; }
    }
}