using FiscalizeXero.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace FiscalizeXero.Api.Repositories
{
    public class AppUserCompanyRepository : IAppUserCompanyRepository
    {
        private readonly DataContext _context;

        public AppUserCompanyRepository(DataContext context)
        {
            _context = context;
        }

        public async Task<IEnumerable<AppUserCompany>> GetAllAsync()
        {
            return await _context.AppUserCompanies
                .Include(c => c.AppUser)
                .Include(c => c.ClientConfig)
                .ToListAsync();
        }

        public async Task<AppUserCompany?> GetByIdAsync(int id)
        {
            return await _context.AppUserCompanies
                .Include(c => c.AppUser)
                .Include(c => c.ClientConfig)
                .FirstOrDefaultAsync(c => c.Id == id);
        }

        public async Task<IEnumerable<AppUserCompany>> GetByAppUserIdAsync(int appUserId)
        {
            return await _context.AppUserCompanies
                .Include(c => c.AppUser)
                .Include(c => c.ClientConfig)
                .Where(c => c.AppUserId == appUserId)
                .ToListAsync();
        }

        public async Task<AppUserCompany?> GetByAppUserIdAndCompanyIdAsync(int appUserId, long companyId)
        {
            return await _context.AppUserCompanies
                .Include(c => c.AppUser)
                .Include(c => c.ClientConfig)
                .FirstOrDefaultAsync(c => c.AppUserId == appUserId && c.CompanyId == companyId);
        }

        public Task<int> CountMembersAsync(long companyId) =>
            _context.AppUserCompanies.CountAsync(c => c.CompanyId == companyId);

        public async Task<int?> GetDataHolderIdAsync(long companyId) =>
            await _context.AppUserCompanies
                .Where(c => c.CompanyId == companyId
                    && (c.DeviceConfigs.Any() || _context.FiscalRecords.Any(r => r.AppUserCompanyId == c.Id)))
                // Ranking: the row with devices, then the most fiscal records, then the oldest.
                .OrderByDescending(c => c.DeviceConfigs.Any())
                .ThenByDescending(c => _context.FiscalRecords.Count(r => r.AppUserCompanyId == c.Id))
                .ThenBy(c => c.Id)
                .Select(c => (int?)c.Id)
                .FirstOrDefaultAsync();

        public async Task<AppUserCompany> CreateAsync(AppUserCompany appUserCompany)
        {
            _context.AppUserCompanies.Add(appUserCompany);
            await _context.SaveChangesAsync();
            return appUserCompany;
        }

        public async Task<AppUserCompany> UpdateAsync(AppUserCompany appUserCompany)
        {
            _context.AppUserCompanies.Update(appUserCompany);
            await _context.SaveChangesAsync();
            return appUserCompany;
        }

        public async Task<bool> DeleteAsync(int id)
        {
            var appUserCompany = await _context.AppUserCompanies.FindAsync(id);
            if (appUserCompany == null)
                return false;

            _context.AppUserCompanies.Remove(appUserCompany);
            await _context.SaveChangesAsync();
            return true;
        }
    }
}
