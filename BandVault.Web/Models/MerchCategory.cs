namespace BandVault.Web.Models
{
    public class MerchCategory
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;

        public ICollection<MerchItem> MerchItems { get; set; } = new List<MerchItem>();
    }
}