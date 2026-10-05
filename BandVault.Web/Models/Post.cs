namespace BandVault.Web.Models
{
    public class Post
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string ContentMarkdown { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public bool IsPublished { get; set; }

        public int AuthorId { get; set; }
        public User? Author { get; set; }
    }
}