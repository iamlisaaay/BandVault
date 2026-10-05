namespace BandVault.Web.Models
{
    public class Track
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public int DurationSeconds { get; set; }
        public string AudioFileUrl { get; set; } = string.Empty;
        public int PlayCount { get; set; }

        // Дозволяє ховати закриті демки для вищих рівнів фан-клубу
        public int RequiredTierLevel { get; set; }

        public int ReleaseId { get; set; }
        public Release? Release { get; set; }
    }
}