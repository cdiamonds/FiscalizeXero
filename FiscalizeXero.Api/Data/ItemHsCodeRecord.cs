using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FiscalizeXero.Api.Data
{
    public class ItemHsCodeRecord
    {
        [Key]
        public long Id { get; set; }

        [ForeignKey("AppUserCompanyId")]
        public AppUserCompany AppUserCompany { get; set; } = null!;
        public int AppUserCompanyId { get; set; }

        /// <summary>The Xero item code (the code users see on a line, up to 30 characters), as typed in Xero.</summary>
        [MaxLength(60)]
        public string ItemCode { get; set; } = string.Empty;
        public string? HsCode { get; set; }

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
