namespace BandVault.Web.Models
{
    public class Release
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public DateTime ReleaseDate { get; set; }
        public string? CoverImageUrl { get; set; }

        public int GenreId { get; set; }
        public Genre? Genre { get; set; }

        public ICollection<Track> Tracks { get; set; } = new List<Track>();
    }
}