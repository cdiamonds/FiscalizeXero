namespace FiscalizeXero.Fiscalize.Models.AppLaunchers
{
    public class ValidateLaunchTokenRequest
    {
        public string Token { get; set; } = null!;
    }

    public class LaunchTokenValidationResult
    {
        public long AppUserId { get; set; }
        public string UserId { get; set; } = null!;
        public string Email { get; set; } = null!;
        public string FullName { get; set; } = null!;
        public long CompanyId { get; set; }
        public string CompanyName { get; set; } = null!;
        public string? FiscalizeJwt { get; set; }
        public DateTime? FiscalizeJwtExpiry { get; set; }
        public string? FiscalizeRefreshToken { get; set; }
    }

    public class AppLaunchModel
    {
        public string Token { get; set; } = null!;
        public string LaunchUrl { get; set; } = null!;
        public DateTime ExpiresAt { get; set; }
    }
}
