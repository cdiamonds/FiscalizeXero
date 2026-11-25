using FiscalizeXero.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace FiscalizeXero.Api.Repositories
{
    public class FiscalRecordRepository : IFiscalRecordRepository
    {
        private readonly DataContext _context;

        public FiscalRecordRepository(DataContext context) => _context = context;

        public Task<FiscalRecord?> GetAsync(int appUserCompanyId, string documentId)
            => _context.FiscalRecords
                .FirstOrDefaultAsync(r => r.AppUserCompanyId == appUserCompanyId && r.DocumentId == documentId);

        public Task<List<FiscalRecord>> GetManyAsync(int appUserCompanyId, IEnumerable<string> documentIds)
        {
            var ids = documentIds.ToList();
            return _context.FiscalRecords
                .Where(r => r.AppUserCompanyId == appUserCompanyId && ids.Contains(r.DocumentId))
                .ToListAsync();
        }

        public async Task UpsertAsync(FiscalRecord record)
        {
            var existing = await _context.FiscalRecords
                .FirstOrDefaultAsync(r => r.AppUserCompanyId == record.AppUserCompanyId && r.DocumentId == record.DocumentId);

            if (existing == null)
            {
                _context.FiscalRecords.Add(record);
            }
            else
            {
                existing.VerificationCode = record.VerificationCode;
                existing.QrCodeUrl = record.QrCodeUrl;
                existing.ReceiptNumber = record.ReceiptNumber;
                existing.GlobalNumber = record.GlobalNumber;
                existing.DeviceId = record.DeviceId;
                existing.FiscalDayNumber = record.FiscalDayNumber;
                existing.ValidationError = record.ValidationError;
                existing.DocumentNumber = record.DocumentNumber;
                existing.FiscalizedAt = record.FiscalizedAt;
            }

            await _context.SaveChangesAsync();
        }

        public Task<FiscalRecord?> GetByDocumentNumberAsync(int appUserCompanyId, string documentType, string documentNumber)
            => _context.FiscalRecords
                .Where(r => r.AppUserCompanyId == appUserCompanyId && r.DocumentType == documentType && r.DocumentNumber == documentNumber && r.ReceiptNumber != null)
                .OrderByDescending(r => r.FiscalizedAt).FirstOrDefaultAsync();

        public Task<List<FiscalRecord>> GetRecentAsync(int appUserCompanyId, string documentType, int take)
            => _context.FiscalRecords
                .Where(r => r.AppUserCompanyId == appUserCompanyId && r.DocumentType == documentType && r.ReceiptNumber != null)
                .OrderByDescending(r => r.FiscalizedAt).Take(take).ToListAsync();

        public async Task DeleteAsync(int appUserCompanyId, string documentId)
        {
            var existing = await _context.FiscalRecords
                .FirstOrDefaultAsync(r => r.AppUserCompanyId == appUserCompanyId && r.DocumentId == documentId);
            if (existing == null) return;
            _context.FiscalRecords.Remove(existing);
            await _context.SaveChangesAsync();
        }
    }
}
