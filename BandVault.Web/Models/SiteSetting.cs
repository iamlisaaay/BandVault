using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BandVault.Web.Models

{
    [Table("SiteSetting")]
    public class SiteSetting
    {
        public int Id { get; set; }

        [Required]
        public string Key { get; set; } = string.Empty;
        [DisplayFormat(ConvertEmptyStringToNull = false)]
        public string Value { get; set; } = string.Empty;
    }
}