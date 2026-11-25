using FiscalizeXero.Api.Data;

namespace FiscalizeXero.Api.Repositories
{
    public interface IContactVatRepository
    {
        Task<ContactVatRecord?> GetAsync(int appUserCompanyId, string contactId);
        Task<List<ContactVatRecord>> GetManyAsync(int appUserCompanyId, IEnumerable<string> contactIds);
        Task<List<ContactVatRecord>> GetAllForCompanyAsync(int appUserCompanyId);
        /// <summary>Saves every buyer value in <paramref name="values"/> for the customer; null clears that value.</summary>
        Task UpsertAsync(int appUserCompanyId, string contactId, ContactVatRecord values);
    }
}
