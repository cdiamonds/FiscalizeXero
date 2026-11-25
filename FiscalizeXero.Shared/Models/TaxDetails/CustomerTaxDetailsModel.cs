namespace FiscalizeXero.Shared.Models.TaxDetails
{
    /// <summary>
    /// A customer's buyer details as fiscalisation will use them, and whether they satisfy ZIMRA.
    /// <see cref="Source"/> says whether any value was saved in Fiscalize or all are read from Xero.
    /// </summary>
    public class CustomerTaxDetailsModel
    {
        /// <summary>The Xero ContactID (a GUID, lower case).</summary>
        public string CustomerId { get; set; } = string.Empty;
        /// <summary>The contact's name in Xero.</summary>
        public string? DisplayName { get; set; }
        public string? RegisteredName { get; set; }
        public string? TradeName { get; set; }
        public string? VatNumber { get; set; }
        public string? Tin { get; set; }

        /// <summary>"Override" if any value was saved in Fiscalize, otherwise "Xero".</summary>
        public string Source { get; set; } = "Xero";

        /// <summary>The email that will be sent (the first valid one in the Xero field).</summary>
        public string? Email { get; set; }
        /// <summary>The phone number that will be sent: Phone, else Mobile, else Direct dial, cleaned up.</summary>
        public string? Phone { get; set; }
        /// <summary>Where <see cref="Phone"/> came from: "Saved in Fiscalize", or the Xero phone "Phone", "Mobile" or "Direct dial".</summary>
        public string? PhoneSource { get; set; }
        /// <summary>The raw Xero numbers (country code, area code and number run together), for display.</summary>
        public string? DefaultPhone { get; set; }
        public string? Mobile { get; set; }
        public string? DirectDial { get; set; }

        /// <summary>The Xero address the parts were read from on one line, for display: the billing (POBOX) address, else the delivery (STREET) one.</summary>
        public string? AddressText { get; set; }
        /// <summary>Address parts as they will be sent to ZIMRA (parsed from the Xero address unless saved in Fiscalize).</summary>
        public string? HouseNo { get; set; }
        public string? Street { get; set; }
        public string? City { get; set; }
        public string? Province { get; set; }

        /// <summary>NoTin, Compliant, MissingDetails or Invalid.</summary>
        public string ComplianceStatus { get; set; } = "NoTin";
        /// <summary>Values typed in Fiscalize for this customer (each wins over Xero). Null means "use Xero".</summary>
        public BuyerDetailsValuesModel Saved { get; set; } = new();
        /// <summary>What the Xero contact alone provides, so the page can show it as the fallback.</summary>
        public BuyerDetailsValuesModel Xero { get; set; } = new();

        /// <summary>The contact's Tax Number box in Xero, exactly as it is. A value of exactly 10 digits is read as the TIN, exactly 9 digits as the VAT number.</summary>
        public string? XeroTaxNumber { get; set; }
        /// <summary>The contact's Company Number box in Xero, exactly as it is. Read the same way as <see cref="XeroTaxNumber"/>.</summary>
        public string? XeroCompanyNumber { get; set; }

        /// <summary>Set on a save: true when the Xero contact was updated too, false when it was not, null when it was not asked for.</summary>
        public bool? XeroUpdated { get; set; }
        /// <summary>Set on a save when Xero didn't take (all of) the update; the values are still saved in Fiscalize.</summary>
        public string? XeroWarning { get; set; }

        /// <summary>Buyer details ZIMRA needs with a TIN that could not be found.</summary>
        public List<string> Missing { get; set; } = new();
        /// <summary>Values present but wrong (bad TIN/VAT format, VAT without TIN).</summary>
        public List<string> Problems { get; set; } = new();
        /// <summary>The company's buyer validation ("Strict" or "Lenient"): whether missing details block fiscalising or get placeholders. Set on a single-customer read.</summary>
        public string? ValidationMode { get; set; }
    }

    /// <summary>The ten buyer values ZIMRA uses.</summary>
    public class BuyerDetailsValuesModel
    {
        public string? RegisteredName { get; set; }
        public string? TradeName { get; set; }
        public string? VatNumber { get; set; }
        public string? Tin { get; set; }
        public string? Phone { get; set; }
        public string? Email { get; set; }
        public string? HouseNo { get; set; }
        public string? Street { get; set; }
        public string? City { get; set; }
        public string? Province { get; set; }
    }

    /// <summary>Buyer values to save in Fiscalize. Null leaves a value to Xero; "" clears it, so nothing is sent for it.</summary>
    public class SaveCustomerTaxDetailsModel : BuyerDetailsValuesModel
    {
        /// <summary>
        /// Also copy the TIN to the Xero contact's Tax Number and the VAT number to its Company Number. Values Xero
        /// then holds exactly are no longer kept in Fiscalize. Xero has no home for the other values.
        /// </summary>
        public bool UpdateXero { get; set; }
    }

    /// <summary>A page of customers with compliance counts across all of them.</summary>
    public class CustomerOverviewModel
    {
        public List<CustomerTaxDetailsModel> Items { get; set; } = new();
        /// <summary>Customers matching the search and status filter.</summary>
        public int Total { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        /// <summary>Counts per status across every customer matching the search (ignoring the status filter).</summary>
        public CustomerComplianceCounts Counts { get; set; } = new();
        /// <summary>The company's buyer validation mode, so the page can say what happens to incomplete customers.</summary>
        public string ValidationMode { get; set; } = "Strict";
        /// <summary>True when the organisation has more active customers than one overview reads; search to narrow it.</summary>
        public bool Truncated { get; set; }
    }

    public class CustomerComplianceCounts
    {
        public int All { get; set; }
        public int Compliant { get; set; }
        public int MissingDetails { get; set; }
        public int Invalid { get; set; }
        public int NoTin { get; set; }
    }
}
