using System.Text.Json.Serialization;

namespace FiscalizeXero.Shared.Models.Fiscalize
{
    /// <summary>
    /// Request payload for fiscalizing an invoice via Fiscalize API
    /// </summary>
    public class FiscalizeInvoiceRequest
    {
        [JsonPropertyName("docNo")]
        public string DocNo { get; set; } = string.Empty;

        [JsonPropertyName("baseDocNo")]
        public string? BaseDocNo { get; set; }

        [JsonPropertyName("reference")]
        public string? Reference { get; set; }

        [JsonPropertyName("customerCode")]
        public string CustomerCode { get; set; } = string.Empty;

        [JsonPropertyName("customerName")]
        public string CustomerName { get; set; } = string.Empty;

        [JsonPropertyName("discountPercentage")]
        public decimal DiscountPercentage { get; set; }

        [JsonPropertyName("total")]
        public decimal Total { get; set; }

        [JsonPropertyName("currency")]
        public string Currency { get; set; } = "ZMW";

        [JsonPropertyName("deviceId")]
        public int DeviceId { get; set; }

        [JsonPropertyName("isDraft")]
        public bool IsDraft { get; set; }

        [JsonPropertyName("fiscalize")]
        public bool Fiscalize { get; set; } = true;

        [JsonPropertyName("notes")]
        public string? Notes { get; set; }

        [JsonPropertyName("docDate")]
        public DateTime DocDate { get; set; }

        [JsonPropertyName("dueDate")]
        public DateTime? DueDate { get; set; }

        [JsonPropertyName("buyerData")]
        public FiscalizeInvoiceBuyerData? BuyerData { get; set; }

        [JsonPropertyName("payments")]
        public List<FiscalizeInvoicePayment> Payments { get; set; } = new();

        [JsonPropertyName("lines")]
        public List<FiscalizeInvoiceLine> Lines { get; set; } = new();
    }

    /// <summary>
    /// Buyer data for fiscalization (required when customer has TIN)
    /// </summary>
    public class FiscalizeInvoiceBuyerData
    {
        [JsonPropertyName("registeredName")]
        public string RegisteredName { get; set; } = string.Empty;

        [JsonPropertyName("tradeName")]
        public string? TradeName { get; set; }

        [JsonPropertyName("vatNumber")]
        public string? VatNumber { get; set; }

        [JsonPropertyName("tin")]
        public string? Tin { get; set; }

        [JsonPropertyName("contacts")]
        public FiscalizeInvoiceBuyerContacts? Contacts { get; set; }

        [JsonPropertyName("address")]
        public FiscalizeInvoiceBuyerAddress? Address { get; set; }
    }

    /// <summary>
    /// Buyer contact information
    /// </summary>
    public class FiscalizeInvoiceBuyerContacts
    {
        [JsonPropertyName("phoneNumber")]
        public string? PhoneNumber { get; set; }

        [JsonPropertyName("email")]
        public string? Email { get; set; }
    }

    /// <summary>
    /// Buyer address information
    /// </summary>
    public class FiscalizeInvoiceBuyerAddress
    {
        [JsonPropertyName("province")]
        public string? Province { get; set; }

        [JsonPropertyName("street")]
        public string? Street { get; set; }

        [JsonPropertyName("houseNo")]
        public string? HouseNo { get; set; }

        [JsonPropertyName("city")]
        public string? City { get; set; }
    }

    /// <summary>
    /// Payment information
    /// </summary>
    public class FiscalizeInvoicePayment
    {
        [JsonPropertyName("moneyTypeCode")]
        public string MoneyTypeCode { get; set; } = "Cash";

        [JsonPropertyName("paymentAmount")]
        public decimal PaymentAmount { get; set; }
    }

    /// <summary>
    /// Invoice line item
    /// </summary>
    public class FiscalizeInvoiceLine
    {
        [JsonPropertyName("lineNo")]
        public int LineNo { get; set; }

        [JsonPropertyName("itemCode")]
        public string ItemCode { get; set; } = string.Empty;

        [JsonPropertyName("itemName")]
        public string ItemName { get; set; } = string.Empty;

        [JsonPropertyName("itemHsCode")]
        public string ItemHsCode { get; set; } = string.Empty;

        [JsonPropertyName("price")]
        public decimal Price { get; set; }

        [JsonPropertyName("isFinalPrice")]
        public bool IsFinalPrice { get; set; } = true;

        [JsonPropertyName("quantity")]
        public decimal Quantity { get; set; }

        [JsonPropertyName("discountPercentage")]
        public decimal DiscountPercentage { get; set; }

        [JsonPropertyName("lineTotal")]
        public decimal LineTotal { get; set; }

        [JsonPropertyName("taxPercent")]
        public decimal TaxPercent { get; set; }

        [JsonPropertyName("taxCategory")]
        public string TaxCategory { get; set; } = "Rated";
    }

    /// <summary>
    /// Response from Fiscalize API after fiscalizing an invoice
    /// </summary>
    public class FiscalizeInvoiceResponse
    {
        public long Id { get; set; }
        public long CompanyId { get; set; }
        public string DocNo { get; set; } = string.Empty;
        public string? BaseDocNo { get; set; }
        public string? Reference { get; set; }
        public string CustomerCode { get; set; } = string.Empty;
        public string CustomerName { get; set; } = string.Empty;
        public decimal DiscountPercentage { get; set; }
        public decimal Total { get; set; }
        public decimal TotalTax { get; set; }
        public decimal TotalDiscount { get; set; }
        public string Currency { get; set; } = string.Empty;
        public long DeviceId { get; set; }
        public DateTime DocDate { get; set; }
        public DateTime DueDate { get; set; }
        public bool IsCancelled { get; set; }
        public bool IsDraft { get; set; }
        public string? Notes { get; set; }
        public long CreatedBy { get; set; }
        public bool IsDeviceGenerated { get; set; }
        public bool Fiscalize { get; set; }
        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid? ShareToken { get; set; }
        public DateTime? ShareTokenExpiry { get; set; }
        public bool IsShareEnabled { get; set; }
        public FiscalizeInvoiceBuyerDataResponse? BuyerData { get; set; }
        public List<FiscalizeInvoicePaymentResponse>? Payments { get; set; }
        public FiscalizeInvoiceFiscalData? FiscalData { get; set; }
        public List<FiscalizeInvoiceFiscalTaxSummary>? FiscalTaxSummaries { get; set; }
        public List<FiscalizeInvoiceLineResponse>? Lines { get; set; }
        public List<FiscalizeInvoiceActivity>? Activities { get; set; }
    }

    /// <summary>
    /// Buyer data in response
    /// </summary>
    public class FiscalizeInvoiceBuyerDataResponse
    {
        public long Id { get; set; }
        public long InvoiceId { get; set; }
        public string RegisteredName { get; set; } = string.Empty;
        public string? TradeName { get; set; }
        public string? VatNumber { get; set; }
        public string? TIN { get; set; }
        public FiscalizeInvoiceBuyerContactsResponse? Contacts { get; set; }
        public FiscalizeInvoiceBuyerAddressResponse? Address { get; set; }
    }

    /// <summary>
    /// Buyer contacts in response
    /// </summary>
    public class FiscalizeInvoiceBuyerContactsResponse
    {
        public long Id { get; set; }
        public long InvoiceBuyerDataId { get; set; }
        public string? PhoneNumber { get; set; }
        public string? Email { get; set; }
    }

    /// <summary>
    /// Buyer address in response
    /// </summary>
    public class FiscalizeInvoiceBuyerAddressResponse
    {
        public long Id { get; set; }
        public long InvoiceBuyerDataId { get; set; }
        public string? Province { get; set; }
        public string? Street { get; set; }
        public string? HouseNo { get; set; }
        public string? City { get; set; }
    }

    /// <summary>
    /// Payment in response
    /// </summary>
    public class FiscalizeInvoicePaymentResponse
    {
        public long Id { get; set; }
        public long InvoiceId { get; set; }
        public string MoneyTypeCode { get; set; } = string.Empty;
        public decimal PaymentAmount { get; set; }
    }

    /// <summary>
    /// Fiscal data returned directly by the Fiscalize API (POST api/Invoices/Devices).
    /// </summary>
    public class FiscalizeInvoiceFiscalData
    {
        public long Id { get; set; }
        public long DeviceId { get; set; }
        public string? VerificationCode { get; set; }
        public string? QrlUrl { get; set; }
        public long? ReceiptNumber { get; set; }
        public long? GlobalNumber { get; set; }
        public int? FiscalDayNumber { get; set; }
        public string? InvoiceDate { get; set; }
        public int? ReceiptType { get; set; }
        public int? ValidationError { get; set; }
        /// <summary>True when the platform already held this document: the data returned is the existing fiscal record.</summary>
        public bool Exists { get; set; }
        public bool IsPendingZimra { get; set; }
    }

    /// <summary>
    /// Tax summary in response
    /// </summary>
    public class FiscalizeInvoiceFiscalTaxSummary
    {
        public long Id { get; set; }
        public long InvoiceId { get; set; }
        public string TaxCode { get; set; } = string.Empty;
        public decimal TaxPercent { get; set; }
        public int TaxID { get; set; }
        public decimal TaxAmount { get; set; }
        public decimal SalesAmountWithTax { get; set; }
    }

    /// <summary>
    /// Line item in response
    /// </summary>
    public class FiscalizeInvoiceLineResponse
    {
        public long Id { get; set; }
        public long InvoiceId { get; set; }
        public int LineNo { get; set; }
        public string ItemCode { get; set; } = string.Empty;
        public string ItemName { get; set; } = string.Empty;
        public string ItemHsCode { get; set; } = string.Empty;
        public decimal UnitPrice { get; set; }
        public decimal FinalUnitPrice { get; set; }
        public decimal PriceAfterDiscount { get; set; }
        public decimal Quantity { get; set; }
        public decimal DiscountPercentage { get; set; }
        public decimal LineTotal { get; set; }
        public decimal LineTotalTax { get; set; }
        public decimal LineTotalDiscount { get; set; }
        public decimal TaxPercent { get; set; }
        public int TaxID { get; set; }
        public string TaxCategory { get; set; } = string.Empty;
    }

    /// <summary>
    /// Activity/audit log entry
    /// </summary>
    public class FiscalizeInvoiceActivity
    {
        public long Id { get; set; }
        public long InvoiceId { get; set; }
        public string Status { get; set; } = string.Empty;
        public string? Code { get; set; }
        public string? Comment { get; set; }
        public long CreatedBy { get; set; }
        public string? CreatedByName { get; set; }
        public DateTime DateCreated { get; set; }
    }
}
