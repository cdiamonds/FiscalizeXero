using System.Text.Json.Serialization;
using FiscalizeXero.Shared.Models.Auths;

namespace FiscalizeXero.Fiscalize.Services.Auths
{
    /// <summary>The companies a signed-in user may use on the Fiscalize platform.</summary>
    public static class CompanyUsersService
    {
        /// <summary>One page of the user's companies (<c>api/CompanyUsers/MyCompanies</c>), with the user's own token.</summary>
        public static async Task<List<PlatformCompanyModel>> MyCompaniesAsync(FiscalizeAuthModel auth, int page = 1, int pageSize = 10)
        {
            var result = await RestClient.GetAsync<PlatformCompanyPage>(auth, $"api/CompanyUsers/MyCompanies?Search=&Page={page}&PageSize={pageSize}");
            return result?.Value ?? new List<PlatformCompanyModel>();
        }

        private sealed class PlatformCompanyPage
        {
            [JsonPropertyName("value")] public List<PlatformCompanyModel>? Value { get; set; }
        }
    }

    public sealed class PlatformCompanyModel
    {
        [JsonPropertyName("id")] public long Id { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
    }
}
