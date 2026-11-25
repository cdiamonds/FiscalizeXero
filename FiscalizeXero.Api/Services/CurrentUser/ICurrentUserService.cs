using FiscalizeXero.Api.Data;

namespace FiscalizeXero.Api.Services.CurrentUser
{
    public interface ICurrentUserService
    {
        Task<AppUser?> GetCurrentUserAsync();
        Task<ClientConfig?> GetCurrentClientConfigAsync();

        /// <summary>
        /// The AppUserCompany id that the active Fiscalize company's data (devices, fiscal records, saved buyer details and HS codes) is
        /// stored under: the one holding the company's Xero connection, else the one already holding its devices or
        /// fiscal records, else the user's own.
        /// </summary>
        Task<int?> GetCompanyAnchorIdAsync();
        long? GetCurrentUserId();
    }
}
