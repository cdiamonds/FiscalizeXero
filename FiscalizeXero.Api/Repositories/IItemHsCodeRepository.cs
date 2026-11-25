using FiscalizeXero.Api.Data;

namespace FiscalizeXero.Api.Repositories
{
    /// <summary>HS codes kept in Fiscalize, one per Xero item code per company.</summary>
    public interface IItemHsCodeRepository
    {
        Task<ItemHsCodeRecord?> GetAsync(int appUserCompanyId, string itemCode);
        Task<List<ItemHsCodeRecord>> GetManyAsync(int appUserCompanyId, IEnumerable<string> itemCodes);
        Task<List<ItemHsCodeRecord>> GetAllForCompanyAsync(int appUserCompanyId);
        Task UpsertAsync(int appUserCompanyId, string itemCode, string? hsCode);
    }
}
