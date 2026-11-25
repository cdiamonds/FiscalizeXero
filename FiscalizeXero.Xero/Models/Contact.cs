using FiscalizeXero.Xero.Converters;
using System.Text.Json.Serialization;

namespace FiscalizeXero.Xero.Models
{
    public class Contact
    {
        public Guid ContactID { get; set; }
        public string? ContactNumber { get; set; }
        public string? AccountNumber { get; set; }
        /// <summary>"ACTIVE", "ARCHIVED" or "GDPRREQUEST".</summary>
        public string? ContactStatus { get; set; }
        public string? Name { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? EmailAddress { get; set; }
        /// <summary>The contact's tax number (shown in Xero under the organisation's own label for it).</summary>
        public string? TaxNumber { get; set; }
        /// <summary>The contact's company registration number.</summary>
        public string? CompanyNumber { get; set; }
        public bool? IsCustomer { get; set; }
        public bool? IsSupplier { get; set; }
        public string? DefaultCurrency { get; set; }
        public List<ContactAddress>? Addresses { get; set; }
        public List<ContactPhone>? Phones { get; set; }
        public string? Website { get; set; }
        /// <summary>Only on a single-contact read.</summary>
        public List<ContactGroupRef>? ContactGroups { get; set; }
        /// <summary>Only on a single-contact read: what the customer owes.</summary>
        public ContactBalances? Balances { get; set; }
        /// <summary>The contact's own payment terms, when set (else the organisation's apply).</summary>
        public ContactPaymentTerms? PaymentTerms { get; set; }
        [JsonConverter(typeof(XeroDateConverter))]
        public DateTime? UpdatedDateUTC { get; set; }

        /// <summary>The address of the given type ("STREET" is the delivery address, "POBOX" the billing one) when it has any content.</summary>
        public ContactAddress? Address(string type) =>
            Addresses?.FirstOrDefault(a => string.Equals(a.AddressType, type, StringComparison.OrdinalIgnoreCase) && !a.IsEmpty);

        /// <summary>The number of the given type ("DEFAULT", "MOBILE", "DDI", "FAX") when one is set.</summary>
        public ContactPhone? Phone(string type) =>
            Phones?.FirstOrDefault(p => string.Equals(p.PhoneType, type, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(p.PhoneNumber));
    }

    public class ContactGroupRef
    {
        public string? Name { get; set; }
        public string? Status { get; set; }
    }

    public class ContactBalances
    {
        public ContactBalance? AccountsReceivable { get; set; }
    }

    public class ContactBalance
    {
        public decimal? Outstanding { get; set; }
        public decimal? Overdue { get; set; }
    }

    public class ContactPaymentTerms
    {
        public PaymentTerm? Sales { get; set; }
    }

    /// <summary>"Day" with a "Type": DAYSAFTERBILLDATE, DAYSAFTERBILLMONTH, OFCURRENTMONTH or OFFOLLOWINGMONTH.</summary>
    public class PaymentTerm
    {
        public int? Day { get; set; }
        public string? Type { get; set; }
    }

    public class ContactAddress
    {
        /// <summary>"POBOX" (billing/postal) or "STREET" (delivery/physical).</summary>
        public string? AddressType { get; set; }
        public string? AddressLine1 { get; set; }
        public string? AddressLine2 { get; set; }
        public string? AddressLine3 { get; set; }
        public string? AddressLine4 { get; set; }
        public string? City { get; set; }
        public string? Region { get; set; }
        public string? PostalCode { get; set; }
        public string? Country { get; set; }
        public string? AttentionTo { get; set; }

        [JsonIgnore]
        public bool IsEmpty =>
            string.IsNullOrWhiteSpace(AddressLine1) && string.IsNullOrWhiteSpace(AddressLine2) &&
            string.IsNullOrWhiteSpace(AddressLine3) && string.IsNullOrWhiteSpace(AddressLine4) &&
            string.IsNullOrWhiteSpace(City) && string.IsNullOrWhiteSpace(Region) &&
            string.IsNullOrWhiteSpace(PostalCode) && string.IsNullOrWhiteSpace(Country);
    }

    public class ContactPhone
    {
        /// <summary>"DEFAULT", "DDI", "MOBILE" or "FAX".</summary>
        public string? PhoneType { get; set; }
        public string? PhoneNumber { get; set; }
        public string? PhoneAreaCode { get; set; }
        public string? PhoneCountryCode { get; set; }

        /// <summary>Country code, area code and number run together, as the user would dial it.</summary>
        [JsonIgnore]
        public string Full => string.Concat(PhoneCountryCode?.Trim(), PhoneAreaCode?.Trim(), PhoneNumber?.Trim());
    }

    public class ContactsEnvelope
    {
        public List<Contact>? Contacts { get; set; }
        [JsonPropertyName("pagination")] public XeroPagination? Pagination { get; set; }
    }
}
