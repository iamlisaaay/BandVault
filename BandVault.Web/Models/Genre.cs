namespace BandVault.Web.Models
{
    public class Genre
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;

        public ICollection<Release> Releases { get; set; } = new List<Release>();
    }
}