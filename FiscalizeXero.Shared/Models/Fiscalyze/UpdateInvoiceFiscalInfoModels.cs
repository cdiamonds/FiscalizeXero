using System.Text.Json.Serialization;

namespace FiscalizeXero.Shared.Models.Fiscalize
{
    /// <summary>
    /// Corrected buyer details for an already-fiscalized invoice. Applying them re-fiscalizes the invoice
    /// (a credit note against the original FDMS record, then a fresh submission).
    /// </summary>
    public class UpdateInvoiceFiscalInfoRequest
    {
        /// <summary>Updated buyer information. When TIN is supplied, the related fields are required.</summary>
        [JsonPropertyName("buyerData")]
        public UpdateInvoiceFiscalInfoBuyerData? BuyerData { get; set; }

        /// <summary>When true, clears all buyer data (converts the invoice to a B2C / cash sale). Takes precedence over BuyerData.</summary>
        [JsonPropertyName("removeBuyer")]
        public bool RemoveBuyer { get; set; }
    }

    public class UpdateInvoiceFiscalInfoBuyerData
    {
        [JsonPropertyName("registeredName")] public string? RegisteredName { get; set; }
        [JsonPropertyName("tradeName")] public string? TradeName { get; set; }
        [JsonPropertyName("vatNumber")] public string? VatNumber { get; set; }
        [JsonPropertyName("tin")] public string? Tin { get; set; }
        [JsonPropertyName("contacts")] public UpdateInvoiceFiscalInfoBuyerContacts? Contacts { get; set; }
        [JsonPropertyName("address")] public UpdateInvoiceFiscalInfoBuyerAddress? Address { get; set; }
    }

    public class UpdateInvoiceFiscalInfoBuyerContacts
    {
        [JsonPropertyName("phoneNumber")] public string? PhoneNumber { get; set; }
        [JsonPropertyName("email")] public string? Email { get; set; }
    }

    public class UpdateInvoiceFiscalInfoBuyerAddress
    {
        [JsonPropertyName("province")] public string? Province { get; set; }
        [JsonPropertyName("street")] public string? Street { get; set; }
        [JsonPropertyName("houseNo")] public string? HouseNo { get; set; }
        [JsonPropertyName("city")] public string? City { get; set; }
    }

    /// <summary>Outcome of a fiscal cancellation: the FDMS record is reversed and the local fiscal record removed.</summary>
    public class FiscalCancelResultModel
    {
        /// <summary>The Xero document id (a GUID, lower case).</summary>
        public string DocumentId { get; set; } = string.Empty;
        public string DocumentNumber { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        /// <summary>Set when the cancellation could not be noted on the Xero document, or the fiscal marker not removed from its Reference.</summary>
        public string? WritebackWarning { get; set; }
    }

    /// <summary>Optional body when fiscalising a credit note: the invoice it reverses, when Xero has no allocation to say so.</summary>
    public class FiscalizeCreditNoteRequest
    {
        /// <summary>Document number of a fiscalised invoice.</summary>
        public string? BaseDocNo { get; set; }
    }

    /// <summary>Which fiscalised invoice a credit note reverses, as far as it can be worked out, plus the choices.</summary>
    public class CreditNoteBaseDocumentModel
    {
        /// <summary>Document number of the original invoice, from the credit note's allocations in Xero (the largest one when there are several). Null while it is unallocated.</summary>
        public string? ResolvedDocNo { get; set; }
        /// <summary>True when that invoice has been fiscalised here (ZIMRA needs the original receipt).</summary>
        public bool ResolvedIsFiscalized { get; set; }
        /// <summary>Why it is not usable, when it is not.</summary>
        public string? Problem { get; set; }
        /// <summary>Every invoice the credit is allocated to in Xero, largest allocation first.</summary>
        public List<CreditNoteAllocationModel> Allocations { get; set; } = new();
        /// <summary>This company's fiscalised invoices, newest first, for the user to choose from.</summary>
        public List<BaseInvoiceCandidate> Candidates { get; set; } = new();
    }

    public class CreditNoteAllocationModel
    {
        public string InvoiceId { get; set; } = string.Empty;
        public string? InvoiceNumber { get; set; }
        public decimal Amount { get; set; }
        public bool IsFiscalized { get; set; }
    }

    public class BaseInvoiceCandidate
    {
        /// <summary>The Xero invoice id (a GUID, lower case).</summary>
        public string DocumentId { get; set; } = string.Empty;
        public string DocumentNumber { get; set; } = string.Empty;
        public long? ReceiptNumber { get; set; }
        /// <summary>Always "invoice": a credit note is issued against a fiscal invoice.</summary>
        public string DocumentType { get; set; } = string.Empty;
        public DateTime FiscalizedAt { get; set; }
    }

    /// <summary>Whether the fiscal PDF is attached to the Xero document.</summary>
    public class FiscalAttachmentStatusModel
    {
        public bool Attached { get; set; }
        public string? FileName { get; set; }
        /// <summary>Set when attaching is switched off in Fiscalization settings.</summary>
        public bool AttachmentDisabled { get; set; }
        /// <summary>Whether the customer sees the file on Xero's online invoice.</summary>
        public bool? IncludeOnline { get; set; }
        /// <summary>Set after an attach attempt that did not succeed, or that attached the stamped PDF in place of the chosen one.</summary>
        public string? Warning { get; set; }
        /// <summary>Which PDF the company attaches: "XeroStamp", "FiscalizeLayout" or "FiscalizeStandard".</summary>
        public string? PdfLayout { get; set; }
    }

    /// <summary>A Xero tax rate with its effective rate, for showing tax per line. Lines name it by <see cref="TaxType"/>.</summary>
    public class TaxRateModel
    {
        public string TaxType { get; set; } = string.Empty;
        public string? Name { get; set; }
        public decimal Percent { get; set; }
        /// <summary>The ZIMRA category the rate is reported under: Rated, ZeroRated or Exempt.</summary>
        public string Category { get; set; } = string.Empty;
        /// <summary>ACTIVE, ARCHIVED or DELETED in Xero.</summary>
        public string? Status { get; set; }
    }
}
