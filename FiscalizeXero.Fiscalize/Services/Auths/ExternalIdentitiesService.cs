using FiscalizeXero.Fiscalize.Models.Auths;
using FiscalizeXero.Shared.Models.Auths;

namespace FiscalizeXero.Fiscalize.Services.Auths
{
    /// <summary>
    /// Thin client for the Fiscalyze platform's /api/ExternalIdentities CRUD. Used
    /// during the OAuth callback when the user arrived via the launch flow (i.e.
    /// already has a Fiscalyze JWT on their AppUserCompany) to create or refresh
    /// the pairing row for their Xero organisation.
    /// </summary>
    public class ExternalIdentitiesService
    {
        private const string ListEndpoint = "api/ExternalIdentities";

        public static async Task<ExternalIdentityResponse> CreateAsync(
            FiscalizeAuthModel auth,
            CreateExternalIdentityRequest request)
        {
            return await RestClient.PostAsync<CreateExternalIdentityRequest, ExternalIdentityResponse>(
                auth, ListEndpoint, request);
        }
    }

    public class CreateExternalIdentityRequest
    {
        public long CompanyId { get; set; }
        public string Provider { get; set; } = ProviderNames.Xero;
        public string ExternalIdentifier { get; set; } = string.Empty;
        public string? ExternalDisplayName { get; set; }
        public string? Notes { get; set; }
    }

    public static class ProviderNames
    {
        /// <summary>The provider name this app pairs under on the Fiscalize platform. The external identifier is the Xero tenant id.</summary>
        public const string Xero = "Xero";
    }

    public class ExternalIdentityResponse
    {
        public long Id { get; set; }
        public long CompanyId { get; set; }
        public string? CompanyName { get; set; }
        public string Provider { get; set; } = string.Empty;
        public string ExternalIdentifier { get; set; } = string.Empty;
        public string? ExternalDisplayName { get; set; }
        public int Status { get; set; }
    }
}
