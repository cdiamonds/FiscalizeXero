using FiscalizeXero.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace FiscalizeXero.Api.Repositories
{
    public class ItemHsCodeRepository : IItemHsCodeRepository
    {
        private readonly DataContext _context;

        public ItemHsCodeRepository(DataContext context) => _context = context;

        public Task<ItemHsCodeRecord?> GetAsync(int appUserCompanyId, string itemCode)
            => _context.ItemHsCodeRecords
                .FirstOrDefaultAsync(r => r.AppUserCompanyId == appUserCompanyId && r.ItemCode == itemCode);

        public Task<List<ItemHsCodeRecord>> GetManyAsync(int appUserCompanyId, IEnumerable<string> itemCodes)
        {
            var codes = itemCodes.ToList();
            return _context.ItemHsCodeRecords
                .Where(r => r.AppUserCompanyId == appUserCompanyId && codes.Contains(r.ItemCode))
                .ToListAsync();
        }

        public Task<List<ItemHsCodeRecord>> GetAllForCompanyAsync(int appUserCompanyId)
            => _context.ItemHsCodeRecords
                .Where(r => r.AppUserCompanyId == appUserCompanyId && r.HsCode != null && r.HsCode != "")
                .ToListAsync();

        public async Task UpsertAsync(int appUserCompanyId, string itemCode, string? hsCode)
        {
            var existing = await _context.ItemHsCodeRecords
                .FirstOrDefaultAsync(r => r.AppUserCompanyId == appUserCompanyId && r.ItemCode == itemCode);

            if (existing == null)
            {
                _context.ItemHsCodeRecords.Add(new ItemHsCodeRecord
                {
                    AppUserCompanyId = appUserCompanyId,
                    ItemCode = itemCode,
                    HsCode = hsCode,
                    UpdatedAt = DateTime.UtcNow
                });
            }
            else
            {
                existing.HsCode = hsCode;
                existing.UpdatedAt = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync();
        }
    }
}
