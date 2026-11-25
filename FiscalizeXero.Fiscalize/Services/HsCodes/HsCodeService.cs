using FiscalizeXero.Fiscalize.Models.HsCodes;
using FiscalizeXero.Shared.Models.Auths;

namespace FiscalizeXero.Fiscalize.Services.HsCodes
{
    public class HsCodeService
    {
        private const string Endpoint = "api/HsCodes";

        public static async Task<HsCodeSearchResponse> SearchAsync(
            FiscalizeAuthModel auth,
            string? search,
            int page,
            int pageSize)
        {
            var query = $"{Endpoint}?Page={page}&PageSize={pageSize}";
            if (!string.IsNullOrWhiteSpace(search))
                query += $"&Search={Uri.EscapeDataString(search)}";

            var response = await RestClient.GetAsync<HsCodeSearchResponse>(auth, query);
            return response ?? new HsCodeSearchResponse();
        }
    }
}
