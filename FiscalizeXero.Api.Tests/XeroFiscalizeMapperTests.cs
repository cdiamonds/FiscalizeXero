using FiscalizeXero.Api.Data;
using FiscalizeXero.Api.Services.Fiscalization;
using FiscalizeXero.Api.Services.Webhooks;
using FiscalizeXero.Xero.Models;
using System.Text;
using Xunit;

namespace FiscalizeXero.Api.Tests
{
    public class XeroFiscalizeMapperTests
    {
        private static readonly Guid ContactId = Guid.Parse("025867f1-d741-4d6b-b1af-9ac774b59ba7");

        private static readonly IReadOnlyDictionary<string, LineTax> Taxes = XeroTaxResolver.Resolve(new[]
        {
            new TaxRate { TaxType = "OUTPUT", Name = "VAT on Sales", EffectiveRate = 15m },
            new TaxRate { TaxType = "ZERORATED", Name = "Zero Rated", EffectiveRate = 0m },
            new TaxRate { TaxType = "EXEMPTOUTPUT", Name = "Exempt Sales", EffectiveRate = 0m },
            new TaxRate { TaxType = "TAX001", Name = "Sales Tax", EffectiveRate = 8.25m,
                TaxComponents = new() { new TaxComponent { Name = "State", Rate = 4m }, new TaxComponent { Name = "City", Rate = 4.25m } } }
        });

        private static readonly Dictionary<string, string?> Hs = new() { ["MILK"] = "04021000" };

        private static LineItem Line(decimal qty, decimal unit, decimal lineAmount, decimal tax, string taxType = "OUTPUT", string? item = "MILK") => new()
        {
            ItemCode = item, Description = "Milk 1L\nfull cream", Quantity = qty, UnitAmount = unit,
            LineAmount = lineAmount, TaxAmount = tax, TaxType = taxType
        };

        private static Invoice Doc(string amounts, decimal total, params LineItem[] lines) => new()
        {
            Type = "ACCREC", InvoiceID = Guid.NewGuid(), InvoiceNumber = "INV-0005", Status = "AUTHORISED",
            DateString = "2026-11-20T00:00:00", DueDateString = "2026-12-20T00:00:00", CurrencyCode = "USD",
            Reference = "PO 88", LineAmountTypes = amounts, Total = total,
            Contact = new Contact { ContactID = ContactId, Name = "City Agency" },
            LineItems = lines.ToList()
        };

        [Fact]
        public void TaxExclusiveLinesAreSentWithTheirTaxAsFinalPrices()
        {
            // Xero: 100 net + 15 tax = 115. ZIMRA must get 115, as a final price so nothing is added or stripped.
            var req = XeroFiscalizeMapper.MapInvoice(Doc("Exclusive", 115m, Line(1m, 100m, 100m, 15m)), 7, null, Hs, Taxes);

            var line = Assert.Single(req.Lines);
            Assert.True(line.IsFinalPrice);
            Assert.Equal((115m, 115m, 1m), (line.Price, line.LineTotal, line.Quantity));
            Assert.Equal((15m, "Rated"), (line.TaxPercent, line.TaxCategory));
            Assert.Equal(("MILK", "Milk 1L", "04021000"), (line.ItemCode, line.ItemName, line.ItemHsCode));
            Assert.Equal(115m, req.Total);
            Assert.Equal(115m, Assert.Single(req.Payments).PaymentAmount);
        }

        [Fact]
        public void TaxInclusiveLinesAlreadyHoldTheFinalAmount()
        {
            var req = XeroFiscalizeMapper.MapInvoice(Doc("Inclusive", 115m, Line(1m, 115m, 115m, 15m)), 7, null, Hs, Taxes);
            var line = Assert.Single(req.Lines);
            Assert.Equal((115m, 115m, 15m), (line.Price, line.LineTotal, line.TaxPercent));
        }

        [Fact]
        public void ANoTaxDocumentIsSentAtZeroPercentWhateverTheLinesSay()
        {
            var req = XeroFiscalizeMapper.MapInvoice(Doc("NoTax", 100m, Line(1m, 100m, 100m, 0m, "OUTPUT")), 7, null, Hs, Taxes);
            var line = Assert.Single(req.Lines);
            Assert.Equal((100m, 0m, "ZeroRated"), (line.LineTotal, line.TaxPercent, line.TaxCategory));
        }

        [Fact]
        public void TheHeaderCarriesTheXeroDocumentAndCustomer()
        {
            var req = XeroFiscalizeMapper.MapInvoice(Doc("Exclusive", 115m, Line(1m, 100m, 100m, 15m)), 7, null, Hs, Taxes);

            Assert.Equal(("INV-0005", "PO 88", "USD", 7), (req.DocNo, req.Reference, req.Currency, req.DeviceId));
            Assert.Equal((ContactId.ToString(), "City Agency"), (req.CustomerCode, req.CustomerName));
            Assert.Equal((new DateTime(2026, 11, 20), new DateTime(2026, 12, 20)), (req.DocDate, req.DueDate!.Value));
            Assert.Null(req.BaseDocNo);
            Assert.Null(req.BuyerData);
        }

        [Fact]
        public void ARateMadeOfSeveralComponentsIsReportedAsOneRate()
        {
            // 8.25% shown in Xero as 4% + 4.25%: ZIMRA gets one line at 8.25%.
            var req = XeroFiscalizeMapper.MapInvoice(Doc("Exclusive", 108.25m, Line(1m, 100m, 100m, 8.25m, "TAX001")), 7, null, Hs, Taxes);
            Assert.Equal((8.25m, 108.25m), (req.Lines[0].TaxPercent, req.Lines[0].LineTotal));
        }

        [Fact]
        public void ZeroRatedAndExemptAreToldApartByTheRatesName()
        {
            var req = XeroFiscalizeMapper.MapInvoice(Doc("Exclusive", 30m,
                Line(1m, 10m, 10m, 0m, "ZERORATED"), Line(1m, 20m, 20m, 0m, "EXEMPTOUTPUT")), 7, null, Hs, Taxes);
            Assert.Equal(new[] { "ZeroRated", "Exempt" }, req.Lines.Select(l => l.TaxCategory));
        }

        [Fact]
        public void ATaxRateThatNoLongerExistsIsWorkedOutFromTheLine()
        {
            var req = XeroFiscalizeMapper.MapInvoice(Doc("Exclusive", 114.5m, Line(1m, 100m, 100m, 14.5m, "DELETED01")), 7, null, Hs, Taxes);
            Assert.Equal((14.5m, "Rated"), (req.Lines[0].TaxPercent, req.Lines[0].TaxCategory));

            var inclusive = XeroFiscalizeMapper.MapInvoice(Doc("Inclusive", 114.5m, Line(1m, 114.5m, 114.5m, 14.5m, "DELETED01")), 7, null, Hs, null);
            Assert.Equal(14.5m, inclusive.Lines[0].TaxPercent);
        }

        [Fact]
        public void LineDiscountsAreAlreadyInsideTheLineAmount()
        {
            // 2 x 50 less 10% = 90 net, 13.50 tax.
            var line = Line(2m, 50m, 90m, 13.5m);
            line.DiscountRate = 10m;
            var req = XeroFiscalizeMapper.MapInvoice(Doc("Exclusive", 103.5m, line), 7, null, Hs, Taxes);

            var mapped = Assert.Single(req.Lines);
            Assert.Equal((2m, 51.75m, 103.5m, 0m), (mapped.Quantity, mapped.Price, mapped.LineTotal, mapped.DiscountPercentage));
            Assert.Equal(0m, req.DiscountPercentage);
        }

        [Fact]
        public void AnAmountThatDoesNotDivideIntoCentsIsSplitSoItStillAddsUp()
        {
            // 3 units for 115.49 in total: one price of 38.50 would make 115.50.
            var req = XeroFiscalizeMapper.MapInvoice(Doc("Inclusive", 115.49m, Line(3m, 38.4967m, 115.49m, 15.06m)), 7, null, Hs, Taxes);

            Assert.Equal(2, req.Lines.Count);
            Assert.Equal(115.49m, req.Lines.Sum(l => l.Quantity * l.Price));
            Assert.Equal(115.49m, req.Lines.Sum(l => l.LineTotal));
            Assert.Equal(new[] { 1, 2 }, req.Lines.Select(l => l.LineNo));
        }

        [Fact]
        public void DescriptionOnlyLinesAreLeftOut()
        {
            var note = new LineItem { Description = "Thank you for your business" };
            var req = XeroFiscalizeMapper.MapInvoice(Doc("Exclusive", 115m, note, Line(1m, 100m, 100m, 15m)), 7, null, Hs, Taxes);
            Assert.Single(req.Lines);
        }

        [Fact]
        public void ALineWithNoItemUsesItsDescriptionAndNoHsCodeOfItsOwn()
        {
            var req = XeroFiscalizeMapper.MapInvoice(Doc("Exclusive", 115m, Line(1m, 100m, 100m, 15m, item: null)), 7, null, Hs, Taxes);
            var line = Assert.Single(req.Lines);
            Assert.Equal(("", "Milk 1L", ""), (line.ItemCode, line.ItemName, line.ItemHsCode));
        }

        [Fact]
        public void ADocumentWhoseLinesDoNotAddUpIsRefusedWithTheReason()
        {
            var ex = Assert.Throws<InvalidOperationException>(() =>
                XeroFiscalizeMapper.MapInvoice(Doc("Exclusive", 200m, Line(1m, 100m, 100m, 15m)), 7, null, Hs, Taxes));
            Assert.Contains("INV-0005 has a total of 200.00, but its lines with tax come to 115.00", ex.Message);

            Assert.Throws<InvalidOperationException>(() => XeroFiscalizeMapper.MapInvoice(Doc("Exclusive", 50m), 7, null, Hs, Taxes));
            Assert.Empty(XeroFiscalizeMapper.MapInvoice(Doc("Exclusive", 0m), 7, null, Hs, Taxes).Lines);
        }

        [Fact]
        public void ACentOfRoundingIsPutOnTheLargestLine()
        {
            var req = XeroFiscalizeMapper.MapInvoice(Doc("Exclusive", 230.01m,
                Line(1m, 100m, 100m, 15m), Line(1m, 100m, 100m, 15m, item: "TEA")), 7, null, Hs, Taxes);
            Assert.Equal(230.01m, req.Lines.Sum(l => l.LineTotal));
        }

        [Fact]
        public void ACreditNoteIsSentAgainstTheInvoiceItReverses()
        {
            var note = new CreditNote
            {
                Type = "ACCRECCREDIT", CreditNoteID = Guid.NewGuid(), CreditNoteNumber = "CN-0002", Status = "PAID",
                DateString = "2026-11-21T00:00:00", CurrencyCode = "USD", LineAmountTypes = "Exclusive", Total = 57.5m,
                Contact = new Contact { ContactID = ContactId, Name = "City Agency" },
                LineItems = new() { Line(1m, 50m, 50m, 7.5m) }
            };
            var req = XeroFiscalizeMapper.MapCreditNote(note, 7, null, Hs, "INV-0005", Taxes);

            Assert.Equal(("CN-0002", "INV-0005", 57.5m), (req.DocNo, req.BaseDocNo, req.Total));
            Assert.Null(req.DueDate);
            Assert.Equal(57.5m, Assert.Single(req.Lines).LineTotal);
        }

        // ---- webhooks

        private const string Key = "a-webhook-key";

        private static byte[] Body(string json) => Encoding.UTF8.GetBytes(json);

        [Fact]
        public void ADeliverySignedWithOurKeyIsAcceptedAndAnyOtherRefused()
        {
            var body = Body("""{"events":[],"firstEventSequence":0,"lastEventSequence":0,"entropy":"ABC"}""");
            var good = XeroWebhookSignature.Compute(body, Key);

            Assert.True(XeroWebhookSignature.Verify(body, good, Key));
            Assert.True(XeroWebhookSignature.Verify(body, " " + good + " ", Key));
            Assert.False(XeroWebhookSignature.Verify(body, XeroWebhookSignature.Compute(body, "another-key"), Key));
            Assert.False(XeroWebhookSignature.Verify(Body("""{"events":[ ]}"""), good, Key)); // same JSON, different bytes
            Assert.False(XeroWebhookSignature.Verify(body, "not base64!", Key));
            Assert.False(XeroWebhookSignature.Verify(body, null, Key));
            Assert.False(XeroWebhookSignature.Verify(body, good, "")); // webhooks not configured: nothing is trusted
        }

        [Fact]
        public void OnlyInvoiceAndCreditNoteEventsOfAnOrganisationAreKept()
        {
            const string json = """
            {"events":[
              {"resourceUrl":"https://api.xero.com/api.xro/2.0/Invoices/717f2bfc-c6d4-41fd-b238-3f2f0c0cf777","resourceId":"717f2bfc-c6d4-41fd-b238-3f2f0c0cf777","tenantId":"c2cc9b6e-9458-4c7d-93cc-f02b81b0594f","tenantType":"ORGANISATION","eventCategory":"INVOICE","eventType":"UPDATE","eventDateUtc":"2026-11-23T01:15:39.902"},
              {"resourceId":"aea95d78-ea48-456b-9b08-6bc012600072","tenantId":"c2cc9b6e-9458-4c7d-93cc-f02b81b0594f","tenantType":"ORGANISATION","eventCategory":"CREDITNOTE","eventType":"CREATE","eventDateUtc":"2026-11-23T01:16:00.000"},
              {"resourceId":"11195d78-ea48-456b-9b08-6bc012600072","tenantId":"c2cc9b6e-9458-4c7d-93cc-f02b81b0594f","tenantType":"ORGANISATION","eventCategory":"CONTACT","eventType":"UPDATE"},
              {"resourceId":"22295d78-ea48-456b-9b08-6bc012600072","tenantId":"c2cc9b6e-9458-4c7d-93cc-f02b81b0594f","tenantType":"ORGANISATION","eventCategory":"PAYMENT","eventType":"CREATE"},
              {"resourceId":"not-a-guid","tenantId":"c2cc9b6e-9458-4c7d-93cc-f02b81b0594f","tenantType":"ORGANISATION","eventCategory":"INVOICE","eventType":"UPDATE"}
            ],"firstEventSequence":1,"lastEventSequence":5,"entropy":"S0m3r4Nd0mt3xt"}
            """;
            var changes = XeroWebhookPayload.Parse(json);

            Assert.Equal(2, changes.Count);
            Assert.Equal((FiscalDocumentType.Invoice, "Update", Guid.Parse("717f2bfc-c6d4-41fd-b238-3f2f0c0cf777")),
                (changes[0].DocumentType, changes[0].Operation, changes[0].DocumentId));
            Assert.Equal((FiscalDocumentType.CreditNote, "Create"), (changes[1].DocumentType, changes[1].Operation));
            Assert.All(changes, c => Assert.Equal(Guid.Parse("c2cc9b6e-9458-4c7d-93cc-f02b81b0594f"), c.TenantId));
        }

        [Fact]
        public void TheIntentToReceiveTestDeliveryHasNoEvents()
        {
            Assert.Empty(XeroWebhookPayload.Parse("""{"events":[],"firstEventSequence":0,"lastEventSequence":0,"entropy":"ABC"}"""));
            Assert.Empty(XeroWebhookPayload.Parse(""));
            Assert.Empty(XeroWebhookPayload.Parse("[]"));
        }
    }
}
