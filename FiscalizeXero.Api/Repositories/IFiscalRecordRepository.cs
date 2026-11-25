using FiscalizeXero.Api.Data;

namespace FiscalizeXero.Api.Repositories
{
    public interface IFiscalRecordRepository
    {
        /// <summary>The record for a Xero document id given in its stored form (<see cref="FiscalDocumentType.Key(Guid)"/>).</summary>
        Task<FiscalRecord?> GetAsync(int appUserCompanyId, string documentId);
        Task<List<FiscalRecord>> GetManyAsync(int appUserCompanyId, IEnumerable<string> documentIds);
        Task UpsertAsync(FiscalRecord record);
        Task DeleteAsync(int appUserCompanyId, string documentId);
        Task<FiscalRecord?> GetByDocumentNumberAsync(int appUserCompanyId, string documentType, string documentNumber);
        Task<List<FiscalRecord>> GetRecentAsync(int appUserCompanyId, string documentType, int take);
    }
}
