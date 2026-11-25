using FiscalizeXero.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace FiscalizeXero.Api.Services.Fiscalization
{
    /// <summary>Reads and writes <see cref="FiscalSuppression"/> rows.</summary>
    public static class FiscalSuppressions
    {
        public static Task<bool> IsSuppressedAsync(DataContext db, int companyId, string documentId, CancellationToken ct = default) =>
            db.FiscalSuppressions.AsNoTracking().AnyAsync(s => s.AppUserCompanyId == companyId && s.DocumentId == documentId, ct);

        public static Task<List<string>> ForAsync(DataContext db, int companyId, IReadOnlyCollection<string> documentIds, CancellationToken ct = default) =>
            db.FiscalSuppressions.AsNoTracking()
                .Where(s => s.AppUserCompanyId == companyId && documentIds.Contains(s.DocumentId))
                .Select(s => s.DocumentId).ToListAsync(ct);

        public static async Task AddAsync(DataContext db, int companyId, string documentType, string documentId, string reason)
        {
            if (await IsSuppressedAsync(db, companyId, documentId)) return;
            db.FiscalSuppressions.Add(new FiscalSuppression
            {
                AppUserCompanyId = companyId, DocumentType = documentType, DocumentId = documentId,
                Reason = reason.Length > 200 ? reason[..200] : reason,
            });
            await db.SaveChangesAsync();
        }

        public static Task RemoveAsync(DataContext db, int companyId, string documentId) =>
            db.FiscalSuppressions.Where(s => s.AppUserCompanyId == companyId && s.DocumentId == documentId).ExecuteDeleteAsync();
    }
}
