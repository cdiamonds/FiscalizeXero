using FiscalizeXero.Xero.Models;
using System.Globalization;

namespace FiscalizeXero.Xero.Services
{
    /// <summary>Calls that are about the app's access, not an organisation's books. They take no tenant id.</summary>
    public static class ConnectionService
    {
        public const string ConnectionsUrl = "https://api.xero.com/connections";

        /// <summary>Every organisation the token's user has connected to this app.</summary>
        public static async Task<List<XeroConnection>> ListAsync(string accessToken)
        {
            var auth = new XeroAuth { Token = accessToken };
            return await RestClient.GetAsync<List<XeroConnection>>(auth, ConnectionsUrl) ?? new List<XeroConnection>();
        }

        /// <summary>Removes one organisation's connection to this app. The other organisations on the same sign-in stay connected.</summary>
        public static Task DisconnectAsync(string accessToken, Guid connectionId) =>
            RestClient.DeleteAsync(new XeroAuth { Token = accessToken }, $"{ConnectionsUrl}/{connectionId}");
    }

    public static class OrganisationService
    {
        public static async Task<Organisation?> GetAsync(XeroAuth auth)
        {
            var envelope = await RestClient.GetAsync<OrganisationsEnvelope>(auth, "Organisation");
            return envelope?.Organisations?.FirstOrDefault();
        }
    }

    public static class TrackingCategoryService
    {
        /// <summary>The organisation's active tracking categories (at most two in Xero).</summary>
        public static async Task<List<TrackingCategory>> ListActiveAsync(XeroAuth auth)
        {
            var envelope = await RestClient.GetAsync<TrackingCategoriesEnvelope>(auth, "TrackingCategories");
            return (envelope?.TrackingCategories ?? new List<TrackingCategory>())
                .Where(c => !string.IsNullOrWhiteSpace(c.Name) && !string.Equals(c.Status, "DELETED", StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
    }

    public static class TaxRateService
    {
        /// <summary>Every tax rate the organisation has, including archived ones (old documents still use them).</summary>
        public static async Task<List<TaxRate>> ListAsync(XeroAuth auth)
        {
            var envelope = await RestClient.GetAsync<TaxRatesEnvelope>(auth, "TaxRates");
            return envelope?.TaxRates ?? new List<TaxRate>();
        }
    }

    public static class ItemService
    {
        /// <summary>Every product and service. Xero does not page this list.</summary>
        public static async Task<List<Item>> ListAsync(XeroAuth auth, DateTime? modifiedSince = null)
        {
            var envelope = await RestClient.GetAsync<ItemsEnvelope>(auth, "Items", modifiedSince);
            return envelope?.Items ?? new List<Item>();
        }

        public static async Task<Item?> GetByIdAsync(XeroAuth auth, Guid id)
        {
            var envelope = await RestClient.GetAsync<ItemsEnvelope>(auth, $"Items/{id}");
            return envelope?.Items?.FirstOrDefault();
        }
    }

    public static class ContactService
    {
        public static async Task<Contact?> GetByIdAsync(XeroAuth auth, Guid id)
        {
            var envelope = await RestClient.GetAsync<ContactsEnvelope>(auth, $"Contacts/{id}");
            return envelope?.Contacts?.FirstOrDefault();
        }

        /// <summary>
        /// A page of active customers, addresses and phones included. <paramref name="search"/> uses Xero's own
        /// search over name, first and last name, contact number, company number and email.
        /// </summary>
        public static async Task<XeroPage<Contact>> ListCustomersAsync(XeroAuth auth, int page, int pageSize, string? search = null)
        {
            var size = Math.Clamp(pageSize, 1, 1000);
            var url = "Contacts?where=" + Uri.EscapeDataString("IsCustomer==true&&ContactStatus==\"ACTIVE\"") +
                      "&order=" + Uri.EscapeDataString("Name ASC") +
                      "&page=" + Math.Max(1, page).ToString(CultureInfo.InvariantCulture) +
                      "&pageSize=" + size.ToString(CultureInfo.InvariantCulture);
            if (!string.IsNullOrWhiteSpace(search))
                url += "&searchTerm=" + Uri.EscapeDataString(search.Trim());

            var envelope = await RestClient.GetAsync<ContactsEnvelope>(auth, url);
            return Paging.ToPage(envelope?.Contacts, envelope?.Pagination, new DocumentQuery { Page = page, PageSize = size });
        }

        /// <summary>Several contacts by id in one call.</summary>
        public static async Task<List<Contact>> GetByIdsAsync(XeroAuth auth, IReadOnlyCollection<Guid> ids)
        {
            var result = new List<Contact>();
            foreach (var chunk in ids.Distinct().Chunk(40))
            {
                var envelope = await RestClient.GetAsync<ContactsEnvelope>(auth, $"Contacts?IDs={string.Join(",", chunk)}");
                if (envelope?.Contacts != null) result.AddRange(envelope.Contacts);
            }
            return result;
        }

        /// <summary>
        /// Writes the contact's tax number and company number, leaving everything else as it is. A null argument
        /// leaves that field alone; an empty string clears it.
        /// </summary>
        public static async Task<Contact?> UpdateTaxDetailsAsync(XeroAuth auth, Guid id, string? taxNumber, string? companyNumber)
        {
            var contact = new Dictionary<string, object?> { ["ContactID"] = id };
            if (taxNumber != null) contact["TaxNumber"] = taxNumber;
            if (companyNumber != null) contact["CompanyNumber"] = companyNumber;
            if (contact.Count == 1) return await GetByIdAsync(auth, id);

            var body = new Dictionary<string, object?> { ["Contacts"] = new[] { contact } };
            var envelope = await RestClient.PostAsync<Dictionary<string, object?>, ContactsEnvelope>(auth, $"Contacts/{id}", body);
            return envelope?.Contacts?.FirstOrDefault();
        }
    }
}
