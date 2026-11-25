using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace FiscalizeXero.Api.Data
{
    public class AppUserCompany
    {
        [Key]
        public int Id { get; set; }

        [ForeignKey("AppUserId")]
        public AppUser AppUser { get; set; } = null!;
        public int AppUserId { get; set; }

        [Required(ErrorMessage = "{0} is required")]
        public long CompanyId { get; set; }

        [StringLength(100, ErrorMessage = "The property {0} should have {1} maximum characters")]
        public string? CompanyName { get; set; }

        [JsonIgnore] // the member's Fiscalize platform session: never sent to the browser
        public string? FiscalizeToken { get; set; }
        public DateTime? FiscalizeTokenExpiresAt { get; set; }
        [JsonIgnore]
        public string? FiscalizeRefreshToken { get; set; }

        public ClientConfig? ClientConfig { get; set; }

        public ICollection<AppUserDeviceConfig> DeviceConfigs { get; set; } = new List<AppUserDeviceConfig>();
    }
}
