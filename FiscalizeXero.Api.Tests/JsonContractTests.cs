using System.Text.Json;
using System.Text.Json.Serialization;
using FiscalizeXero.Api.Data;
using FiscalizeXero.Shared.Models.ClientConfigs.Manage;
using FiscalizeXero.Shared.Models.Documents;
using FiscalizeXero.Shared.Models.Fiscalize;
using FiscalizeXero.Xero.Models;
using Xunit;

namespace FiscalizeXero.Api.Tests
{
    /// <summary>
    /// Pins the JSON the browser receives. The API serialises with ASP.NET's web defaults (camelCase), so a Xero
    /// model's <c>InvoiceID</c> goes out as <c>invoiceID</c>, not as Xero's own PascalCase.
    /// </summary>
    public class JsonContractTests
    {
        // What AddControllers().AddJsonOptions(...) in Program.cs amounts to.
        private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web) { ReferenceHandler = ReferenceHandler.IgnoreCycles };

        internal static string Json(object value) => JsonSerializer.Serialize(value, Web);

        private static readonly Invoice Sample = RestClientRead<InvoicesEnvelope>("""
            {"Invoices":[{"Type":"ACCREC","InvoiceID":"717f2bfc-c6d4-41fd-b238-3f2f0c0cf777","InvoiceNumber":"INV-0042","Reference":"PO-1",
              "DateString":"2026-11-23T00:00:00","DueDateString":"2026-12-23T00:00:00","Status":"AUTHORISED","LineAmountTypes":"Exclusive",
              "SubTotal":100.00,"TotalTax":15.00,"Total":115.00,"AmountDue":115.00,"AmountPaid":0,"CurrencyCode":"USD","UpdatedDateUTC":"/Date(1795000000000+0000)/",
              "Contact":{"ContactID":"5b96e86b-418e-48e8-8949-308c14aec278","Name":"Acme"},
              "LineItems":[{"LineItemID":"11111111-2222-3333-4444-555555555555","Description":"Milk","Quantity":2,"UnitAmount":50.00,"ItemCode":"MILK-1","TaxType":"OUTPUT","TaxAmount":15.00,"LineAmount":100.00}]}]}
            """).Invoices![0];

        private static T RestClientRead<T>(string json) => JsonSerializer.Deserialize<T>(json, FiscalizeXero.Xero.RestClient.ReadOptions)!;

        [Fact]
        public void An_invoice_goes_out_camelCased_with_Xeros_own_field_names()
        {
            var json = Json(Sample);
            foreach (var expected in new[]
                     {
                         "\"invoiceID\":\"717f2bfc-c6d4-41fd-b238-3f2f0c0cf777\"", "\"invoiceNumber\":\"INV-0042\"", "\"type\":\"ACCREC\"", "\"status\":\"AUTHORISED\"",
                         "\"dateString\":\"2026-11-23T00:00:00\"", "\"dueDateString\":\"2026-12-23T00:00:00\"", "\"lineAmountTypes\":\"Exclusive\"",
                         "\"subTotal\":100.00", "\"totalTax\":15.00", "\"total\":115.00", "\"amountDue\":115.00", "\"currencyCode\":\"USD\"", "\"reference\":\"PO-1\"",
                         "\"contact\":{\"contactID\":\"5b96e86b-418e-48e8-8949-308c14aec278\"", "\"name\":\"Acme\"",
                         "\"lineItems\":[{\"lineItemID\":\"11111111-2222-3333-4444-555555555555\",\"description\":\"Milk\",\"quantity\":2,\"unitAmount\":50.00,\"itemCode\":\"MILK-1\"",
                         "\"taxType\":\"OUTPUT\",\"taxAmount\":15.00,\"lineAmount\":100.00", "\"updatedDateUTC\":\"2026-"
                     })
                Assert.Contains(expected, json);

            // The type-neutral id and number are sent too (the overrides in Invoice/CreditNote are not [JsonIgnore]d),
            // so list code shared by invoices and credit notes can use them.
            Assert.Contains("\"documentId\":\"717f2bfc-c6d4-41fd-b238-3f2f0c0cf777\"", json);
            Assert.Contains("\"documentNumber\":\"INV-0042\"", json);

            // The other computed helpers are for the server only, and nothing is PascalCase.
            foreach (var absent in new[] { "\"date\":", "\"isApproved\"", "\"isSale\"", "\"isTaxInclusive\"", "\"InvoiceID\"" })
                Assert.DoesNotContain(absent, json);
        }

        [Fact]
        public void A_credit_note_carries_its_allocations()
        {
            var note = new CreditNote
            {
                CreditNoteID = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000009"), CreditNoteNumber = "CN-7", Type = "ACCRECCREDIT", RemainingCredit = 0m,
                Allocations = new() { new Allocation { Amount = 50m, Invoice = new AllocationInvoice { InvoiceID = Guid.Parse("717f2bfc-c6d4-41fd-b238-3f2f0c0cf777"), InvoiceNumber = "INV-0042" } } }
            };
            var json = Json(note);
            Assert.Contains("\"creditNoteID\":\"aaaaaaaa-0000-0000-0000-000000000009\"", json);
            Assert.Contains("\"creditNoteNumber\":\"CN-7\"", json);
            Assert.Contains("\"allocations\":[{\"allocationID\":null,\"amount\":50,\"date\":null,\"invoice\":{\"invoiceID\":\"717f2bfc-c6d4-41fd-b238-3f2f0c0cf777\",\"invoiceNumber\":\"INV-0042\"}}]", json);
        }

        [Fact]
        public void A_page_of_documents()
        {
            var json = Json(new DocumentPageModel<Invoice> { Items = { Sample }, Total = 53, Page = 3, PageSize = 25, TotalPages = 3, Search = "acme", Statuses = { "AUTHORISED", "PAID" } });
            Assert.StartsWith("{\"items\":[{", json);
            Assert.EndsWith("\"total\":53,\"page\":3,\"pageSize\":25,\"totalPages\":3,\"search\":\"acme\",\"statuses\":[\"AUTHORISED\",\"PAID\"]}", json);
        }

        [Fact]
        public void Fiscalisation_settings()
            => Assert.Equal(
                "{\"autoFiscalizeInvoices\":true,\"autoFiscalizeCreditNotes\":true,\"writebackHistoryNote\":true,\"fiscalStampEnabled\":true," +
                "\"attachmentIncludeOnline\":true,\"writebackReference\":false,\"fiscalPdfLayout\":\"XeroStamp\",\"layoutPaperSize\":\"A4\",\"layoutDesignerUrl\":null,\"fiscalStampPosition\":\"bottom-right\",\"fiscalStampMarginTop\":15," +
                "\"fiscalStampMarginBottom\":15,\"fiscalStampMarginLeft\":15,\"fiscalStampMarginRight\":15,\"defaultHsCode\":null,\"fiscalizationValidationMode\":\"Strict\"}",
                Json(new FiscalizationSettingsModel()));

        [Fact]
        public void A_fiscal_result()
        {
            var json = Json(new FiscalizeResultModel { DocumentId = "717f2bfc-c6d4-41fd-b238-3f2f0c0cf777", DocumentNumber = "INV-0042", DocumentType = "invoice", ReceiptNumber = 1024 });
            Assert.StartsWith("{\"documentId\":\"717f2bfc-c6d4-41fd-b238-3f2f0c0cf777\",\"documentNumber\":\"INV-0042\",\"documentType\":\"invoice\",\"verificationCode\":null,\"qrCodeUrl\":null,\"receiptNumber\":1024,\"globalNumber\":null,\"deviceId\":0,\"fiscalDayNumber\":null,\"validationError\":\"\",\"fiscalizedAt\":", json);
            Assert.EndsWith("\"writebackWarning\":null,\"alreadyFiscalized\":false,\"attachmentWarning\":null,\"attachmentQueued\":false}", json);
        }

        [Fact]
        public void The_connection_never_carries_tokens_its_owner_or_the_oauth_state()
        {
            var config = new ClientConfig
            {
                Id = 5, AppUserCompanyId = 7, TenantId = Guid.Parse("c2cc9b6e-9458-4c7d-93cc-f02b81b0594f"), TenantName = "Acme Org", XeroConnectionId = Guid.NewGuid(),
                AccessToken = "ACCESS-SECRET", RefreshToken = "REFRESH-SECRET", State = Guid.Parse("99999999-9999-9999-9999-999999999999"),
                AppUserCompany = new AppUserCompany { FiscalizeToken = "PLATFORM-SECRET" }
            };
            var json = Json(config);
            foreach (var secret in new[] { "ACCESS-SECRET", "REFRESH-SECRET", "PLATFORM-SECRET", "99999999-9999", "accessToken", "refreshToken", "appUserCompany\":", "\"state\"" })
                Assert.DoesNotContain(secret, json);
            foreach (var expected in new[] { "\"tenantId\":\"c2cc9b6e-9458-4c7d-93cc-f02b81b0594f\"", "\"tenantName\":\"Acme Org\"", "\"xeroConnectionId\":", "\"lastSyncAt\":null", "\"writebackHistoryNote\":true", "\"attachmentIncludeOnline\":true", "\"writebackReference\":false", "\"fiscalizationValidationMode\":\"Strict\"", "\"effectiveDefaultHsCode\":\"04021099\"" })
                Assert.Contains(expected, json);
        }
    }
}
