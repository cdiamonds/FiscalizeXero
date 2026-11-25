namespace FiscalizeXero.Fiscalize.Models.Auths
{
    public class LoginResponseModel
    {
        public long UserId { get; set; }
        public string FullName { get; set; }
        public string? Email { get; set; }
        public bool EmailConfirmed { get; set; }
        public string Token { get; set; }
        public DateTime TokeExpiry { get; set; }
        public string? RefreshToken { get; set; }
        public LoginResponseActiveCompanyModel? ActiveCompany { get; set; }
        /// <summary>The Xero organisation the active company is connected to, when it has one.</summary>
        public LoginResponseXeroOrgModel? XeroOrganisation { get; set; }
        /// <summary>Where "Open in Xero" goes.</summary>
        public string? XeroUrl { get; set; }
    }

    public class LoginResponseActiveCompanyModel
    {
        public long Id { get; set; }
        public string Name { get; set; }
    }

    public class LoginResponseXeroOrgModel
    {
        /// <summary>The Xero tenant id.</summary>
        public Guid Id { get; set; }
        public string? Name { get; set; }
        public bool IsSetupComplete { get; set; }
    }
}
