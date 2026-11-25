namespace FiscalizeXero.Shared.Models.Users
{
    public class UserCompanyModel
    {
        public int Id { get; set; }
        public long CompanyId { get; set; }
        public string CompanyName { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public bool IsSetupComplete { get; set; }
        /// <summary>The connected Xero organisation, also while its tokens are lost and it needs reconnecting.</summary>
        public Guid? XeroTenantId { get; set; }
        public string? XeroTenantName { get; set; }
        /// <summary>Where "Open in Xero" goes.</summary>
        public string? XeroUrl { get; set; }
        public bool NeedsReauth { get; set; }
        /// <summary>Why the connection needs attention, when <see cref="NeedsReauth"/> is true.</summary>
        public string? ConnectionProblem { get; set; }
        public bool IsUsingSharedConnection { get; set; }
    }
}
