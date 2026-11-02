using System.ComponentModel.DataAnnotations;

namespace BandVault.Web.Models
{
    // C6: підписка на розсилку з лендінгу (раніше форма лише показувала alert)
    public class NewsletterSubscriber
    {
        public int Id { get; set; }

        [Required, EmailAddress, MaxLength(256)]
        public string Email { get; set; } = string.Empty;

        public DateTime SubscribedAt { get; set; } = DateTime.UtcNow;
    }
}
