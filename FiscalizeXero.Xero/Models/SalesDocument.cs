using FiscalizeXero.Xero.Converters;
using System.Text.Json.Serialization;

namespace FiscalizeXero.Xero.Models
{
    /// <summary>Values Xero uses for <see cref="SalesDocument.Status"/>.</summary>
    public static class XeroStatus
    {
        public const string Draft = "DRAFT";
        public const string Submitted = "SUBMITTED";
        public const string Authorised = "AUTHORISED";
        public const string Paid = "PAID";
        public const string Voided = "VOIDED";
        public const string Deleted = "DELETED";

        /// <summary>
        /// Approved documents: the only ones that are real sales. Drafts and those awaiting approval can still change
        /// or be thrown away, and voided or deleted ones never happened as far as the books go.
        /// </summary>
        public static bool IsApproved(string? status) =>
            string.Equals(status, Authorised, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(status, Paid, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Values Xero uses for <see cref="SalesDocument.LineAmountTypes"/>: how prices on the lines relate to tax.</summary>
    public static class XeroLineAmountTypes
    {
        public const string Exclusive = "Exclusive";
        public const string Inclusive = "Inclusive";
        public const string NoTax = "NoTax";
    }

    /// <summary>
    /// What a Xero sales invoice and a customer credit note have in common: the same contact, lines, tax and totals,
    /// so one code path maps both for ZIMRA. Each subclass adds its own id, number and the fields only it has.
    /// </summary>
    public abstract class SalesDocument
    {
        /// <summary>"ACCREC" (sales invoice) or "ACCRECCREDIT" (customer credit note). "ACCPAY"/"ACCPAYCREDIT" are purchases.</summary>
        public string? Type { get; set; }
        public Contact? Contact { get; set; }
        /// <summary>The document date as Xero's ISO string, e.g. <c>2026-11-23T00:00:00</c> (no time zone: it is a calendar date).</summary>
        public string? DateString { get; set; }
        public string? Status { get; set; }
        public string? LineAmountTypes { get; set; }
        public List<LineItem> LineItems { get; set; } = new();
        public decimal? SubTotal { get; set; }
        public decimal? TotalTax { get; set; }
        public decimal? Total { get; set; }
        public string? CurrencyCode { get; set; }
        public decimal? CurrencyRate { get; set; }
        public string? Reference { get; set; }
        public Guid? BrandingThemeID { get; set; }
        public bool? HasAttachments { get; set; }
        [JsonConverter(typeof(XeroDateConverter))]
        public DateTime? UpdatedDateUTC { get; set; }

        /// <summary>Xero's id for the document.</summary>
        [JsonIgnore] public abstract Guid DocumentId { get; }
        /// <summary>The number printed on the document.</summary>
        [JsonIgnore] public abstract string? DocumentNumber { get; }

        /// <summary>The document date as <c>yyyy-MM-dd</c>, or null.</summary>
        [JsonIgnore]
        public string? Date => string.IsNullOrWhiteSpace(DateString) ? null : DateString.Length >= 10 ? DateString[..10] : DateString;

        [JsonIgnore] public bool IsApproved => XeroStatus.IsApproved(Status);
        [JsonIgnore] public bool IsTaxInclusive => string.Equals(LineAmountTypes, XeroLineAmountTypes.Inclusive, StringComparison.OrdinalIgnoreCase);
        [JsonIgnore] public bool IsNoTax => string.Equals(LineAmountTypes, XeroLineAmountTypes.NoTax, StringComparison.OrdinalIgnoreCase);
    }

    public class LineItem
    {
        public Guid? LineItemID { get; set; }
        public string? Description { get; set; }
        public decimal? Quantity { get; set; }
        /// <summary>Price for one unit, including or excluding tax according to the document's LineAmountTypes.</summary>
        public decimal? UnitAmount { get; set; }
        public string? ItemCode { get; set; }
        public string? AccountCode { get; set; }
        /// <summary>The tax rate's code (<see cref="TaxRate.TaxType"/>).</summary>
        public string? TaxType { get; set; }
        /// <summary>Tax on this line, all components of the rate together.</summary>
        public decimal? TaxAmount { get; set; }
        /// <summary>Quantity × UnitAmount less discount; includes tax only on a tax-inclusive document.</summary>
        public decimal? LineAmount { get; set; }
        /// <summary>Discount as a percentage of the line.</summary>
        public decimal? DiscountRate { get; set; }
        /// <summary>Discount as an amount, when the line was discounted by amount instead of by percentage.</summary>
        public decimal? DiscountAmount { get; set; }
        public LineItemItem? Item { get; set; }
        /// <summary>The tracking options on this line, e.g. Region = "Harare". Empty when the organisation does not track.</summary>
        public List<LineItemTracking>? Tracking { get; set; }

        /// <summary>A line with nothing on it but text: Xero allows description-only lines, which are not sales.</summary>
        [JsonIgnore]
        public bool IsDescriptionOnly => !Quantity.HasValue && !UnitAmount.HasValue && (LineAmount ?? 0m) == 0m;
    }

    public class LineItemTracking
    {
        public Guid? TrackingCategoryID { get; set; }
        /// <summary>The category's name, e.g. "Region".</summary>
        public string? Name { get; set; }
        /// <summary>The option chosen on the line, e.g. "Harare".</summary>
        public string? Option { get; set; }
    }

    public class LineItemItem
    {
        public Guid? ItemID { get; set; }
        public string? Name { get; set; }
        public string? Code { get; set; }
    }

    public class Invoice : SalesDocument
    {
        public Guid InvoiceID { get; set; }
        public string? InvoiceNumber { get; set; }
        public string? DueDateString { get; set; }
        public decimal? AmountDue { get; set; }
        public decimal? AmountPaid { get; set; }
        public decimal? AmountCredited { get; set; }
        public decimal? TotalDiscount { get; set; }
        public bool? SentToContact { get; set; }
        /// <summary>Credit notes applied to this invoice.</summary>
        public List<AppliedCreditNote>? CreditNotes { get; set; }

        public override Guid DocumentId => InvoiceID;
        public override string? DocumentNumber => InvoiceNumber;

        [JsonIgnore] public bool IsSale => string.Equals(Type, "ACCREC", StringComparison.OrdinalIgnoreCase);
    }

    public class AppliedCreditNote
    {
        public Guid CreditNoteID { get; set; }
        public string? CreditNoteNumber { get; set; }
        public decimal? AppliedAmount { get; set; }
    }

    public class CreditNote : SalesDocument
    {
        public Guid CreditNoteID { get; set; }
        public string? CreditNoteNumber { get; set; }
        public decimal? RemainingCredit { get; set; }
        /// <summary>
        /// The invoices this credit was applied to. This is the only link from a credit note to an invoice in Xero;
        /// it is empty until the user allocates the credit, and can name several invoices.
        /// </summary>
        public List<Allocation>? Allocations { get; set; }

        public override Guid DocumentId => CreditNoteID;
        public override string? DocumentNumber => CreditNoteNumber;

        [JsonIgnore] public bool IsCustomerCredit => string.Equals(Type, "ACCRECCREDIT", StringComparison.OrdinalIgnoreCase);
    }

    public class Allocation
    {
        public Guid? AllocationID { get; set; }
        public decimal? Amount { get; set; }
        [JsonConverter(typeof(XeroDateConverter))]
        public DateTime? Date { get; set; }
        public AllocationInvoice? Invoice { get; set; }
    }

    public class AllocationInvoice
    {
        public Guid InvoiceID { get; set; }
        public string? InvoiceNumber { get; set; }
    }

    public class InvoicesEnvelope
    {
        public List<Invoice>? Invoices { get; set; }
        [JsonPropertyName("pagination")] public XeroPagination? Pagination { get; set; }
    }

    public class CreditNotesEnvelope
    {
        public List<CreditNote>? CreditNotes { get; set; }
        [JsonPropertyName("pagination")] public XeroPagination? Pagination { get; set; }
    }

    public class OnlineInvoice
    {
        public string? OnlineInvoiceUrl { get; set; }
    }

    public class OnlineInvoicesEnvelope
    {
        public List<OnlineInvoice>? OnlineInvoices { get; set; }
    }
}
