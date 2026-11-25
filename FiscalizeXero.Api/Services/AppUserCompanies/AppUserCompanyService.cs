using FiscalizeXero.Api.Data;
using FiscalizeXero.Api.Repositories;

namespace FiscalizeXero.Api.Services.AppUserCompanies
{
    public class AppUserCompanyService : IAppUserCompanyService
    {
        private readonly IAppUserCompanyRepository _appUserCompanyRepository;

        public AppUserCompanyService(IAppUserCompanyRepository appUserCompanyRepository)
        {
            _appUserCompanyRepository = appUserCompanyRepository;
        }

        public async Task<IEnumerable<AppUserCompany>> GetAllAsync()
        {
            return await _appUserCompanyRepository.GetAllAsync();
        }

        public async Task<AppUserCompany?> GetByIdAsync(int id)
        {
            return await _appUserCompanyRepository.GetByIdAsync(id);
        }

        public async Task<IEnumerable<AppUserCompany>> GetByAppUserIdAsync(int appUserId)
        {
            return await _appUserCompanyRepository.GetByAppUserIdAsync(appUserId);
        }

        public async Task<AppUserCompany?> GetByAppUserIdAndCompanyIdAsync(int appUserId, long companyId)
        {
            return await _appUserCompanyRepository.GetByAppUserIdAndCompanyIdAsync(appUserId, companyId);
        }

        public Task<int> CountMembersAsync(long companyId) => _appUserCompanyRepository.CountMembersAsync(companyId);

        public Task<int?> GetDataHolderIdAsync(long companyId) => _appUserCompanyRepository.GetDataHolderIdAsync(companyId);

        public async Task<AppUserCompany> GetOrCreateAsync(int appUserId, long companyId, string? companyName = null)
        {
            var existing = await _appUserCompanyRepository.GetByAppUserIdAndCompanyIdAsync(appUserId, companyId);
            if (existing != null)
            {
                return existing;
            }

            var newAppUserCompany = new AppUserCompany
            {
                AppUserId = appUserId,
                CompanyId = companyId,
                CompanyName = companyName
            };

            return await _appUserCompanyRepository.CreateAsync(newAppUserCompany);
        }

        public async Task<AppUserCompany> CreateAsync(AppUserCompany appUserCompany)
        {
            return await _appUserCompanyRepository.CreateAsync(appUserCompany);
        }

        public async Task<AppUserCompany> UpdateAsync(AppUserCompany appUserCompany)
        {
            return await _appUserCompanyRepository.UpdateAsync(appUserCompany);
        }

        public async Task<bool> DeleteAsync(int id)
        {
            return await _appUserCompanyRepository.DeleteAsync(id);
        }
    }
}
