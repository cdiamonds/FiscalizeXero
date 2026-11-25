using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FiscalizeXero.Api.Data
{
    public class AppUser
    {
        [Key]
        public int Id { get; set; }
        public string? Email { get; set; }
        [Required(ErrorMessage = "{0} is required")]
        public long UserId { get; set; }
        [ForeignKey("ActiveCompanyId")]
        public AppUserCompany? ActiveCompany { get; set; }
        public int? ActiveCompanyId { get; set; }

        public ICollection<AppUserCompany> Companies { get; set; } = new List<AppUserCompany>();

        public string? Theme { get; set; }

        /// <summary>
        /// Bumped by "Sign out of all devices": every sign-in token carries the version it was issued with, and a token
        /// with an older version is refused, so a lost or stolen token stops working at once.
        /// </summary>
        public int TokenVersion { get; set; }

        /// <summary>
        /// The user's own Fiscalize platform session from their last direct sign-in (email and password), which may
        /// have no company chosen yet. It lists the user's companies and opens a session on a chosen company, and it
        /// renews a company session that can no longer be refreshed on its own. Encrypted at rest.
        /// </summary>
        public string? FiscalizeToken { get; set; }
        public DateTime? FiscalizeTokenExpiresAt { get; set; }
        /// <summary>Renews <see cref="FiscalizeToken"/>. Encrypted at rest.</summary>
        public string? FiscalizeRefreshToken { get; set; }
    }
}
