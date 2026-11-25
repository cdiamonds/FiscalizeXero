using FiscalizeXero.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace FiscalizeXero.Api.Repositories
{
    public class ContactVatRepository : IContactVatRepository
    {
        private readonly DataContext _context;

        public ContactVatRepository(DataContext context) => _context = context;

        public Task<ContactVatRecord?> GetAsync(int appUserCompanyId, string contactId)
            => _context.ContactVatRecords
                .FirstOrDefaultAsync(r => r.AppUserCompanyId == appUserCompanyId && r.ContactId == contactId);

        public Task<List<ContactVatRecord>> GetManyAsync(int appUserCompanyId, IEnumerable<string> contactIds)
        {
            var ids = contactIds.ToList();
            return _context.ContactVatRecords
                .Where(r => r.AppUserCompanyId == appUserCompanyId && ids.Contains(r.ContactId))
                .ToListAsync();
        }

        public Task<List<ContactVatRecord>> GetAllForCompanyAsync(int appUserCompanyId)
            => _context.ContactVatRecords
                .Where(r => r.AppUserCompanyId == appUserCompanyId &&
                    (r.VatNumber != null ||
                     r.Tin != null ||
                     r.RegisteredName != null ||
                     r.TradeName != null ||
                     r.Phone != null ||
                     r.Email != null ||
                     r.HouseNo != null ||
                     r.Street != null ||
                     r.City != null ||
                     r.Province != null))
                .ToListAsync();

        public async Task UpsertAsync(int appUserCompanyId, string contactId, ContactVatRecord values)
        {
            var existing = await _context.ContactVatRecords
                .FirstOrDefaultAsync(r => r.AppUserCompanyId == appUserCompanyId && r.ContactId == contactId);

            if (existing == null)
            {
                existing = new ContactVatRecord { AppUserCompanyId = appUserCompanyId, ContactId = contactId };
                _context.ContactVatRecords.Add(existing);
            }

            existing.CopyBuyerValuesFrom(values);
            existing.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }
    }
}
