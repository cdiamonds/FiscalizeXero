using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FiscalizeXero.Api.Data
{
    public class AppUserDeviceConfig
    {
        [Key]
        public int Id { get; set; }

        [ForeignKey("AppUserCompanyId")]
        public AppUserCompany AppUserCompany { get; set; } = null!;
        public int AppUserCompanyId { get; set; }

        public long DeviceId { get; set; }
        public string? Platform { get; set; }
        public int DeviceNumber { get; set; }

        public long? ApiKeyId { get; set; }
        public string? ApiKey { get; set; }
        public string? ApiSecret { get; set; }
        public string? ApiKeyDescription { get; set; }

        public bool IsDefault { get; set; } = false;

        public DateTime DateConfigured { get; set; } = DateTime.UtcNow;
        public DateTime? DateUpdated { get; set; }
    }
}
