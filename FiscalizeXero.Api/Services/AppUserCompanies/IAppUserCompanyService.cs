using FiscalizeXero.Api.Data;

namespace FiscalizeXero.Api.Services.AppUserCompanies
{
    public interface IAppUserCompanyService
    {
        Task<IEnumerable<AppUserCompany>> GetAllAsync();
        Task<AppUserCompany?> GetByIdAsync(int id);
        Task<IEnumerable<AppUserCompany>> GetByAppUserIdAsync(int appUserId);
        Task<AppUserCompany?> GetByAppUserIdAndCompanyIdAsync(int appUserId, long companyId);
        /// <summary>How many users belong to this Fiscalize company (rows for the same CompanyId).</summary>
        Task<int> CountMembersAsync(long companyId);
        /// <summary>
        /// The member row that already holds this company's devices or fiscal records (lowest id first), or null. Used to
        /// put a new connection where the company's data already lives, whoever reconnects.
        /// </summary>
        Task<int?> GetDataHolderIdAsync(long companyId);
        Task<AppUserCompany> GetOrCreateAsync(int appUserId, long companyId, string? companyName = null);
        Task<AppUserCompany> CreateAsync(AppUserCompany appUserCompany);
        Task<AppUserCompany> UpdateAsync(AppUserCompany appUserCompany);
        Task<bool> DeleteAsync(int id);
    }
}
