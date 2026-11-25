using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Fiscalize.LayoutRenderer;
using FiscalizeXero.Api.Data;
using FiscalizeXero.Api.Services.ConnectedApps;
using FiscalizeXero.Api.Services.Fiscalization;
using FiscalizeXero.Api.Services.Layouts;
using FiscalizeXero.Xero.Models;
using Xunit;

namespace FiscalizeXero.Api.Tests
{
    /// <summary>Fiscalize layouts for Xero: the mapper (Xero document to the renderer's document) and drawing it in this host.</summary>
    public class XeroLayoutMapperTests
    {
        private static readonly IReadOnlyDictionary<string, LineTax> Taxes = XeroTaxResolver.Resolve(new[]
        {
            new TaxRate { TaxType = "OUTPUT", Name = "VAT on Sales", EffectiveRate = 15m },
            new TaxRate { TaxType = "ZERORATED", Name = "Zero Rated", EffectiveRate = 0m }
        });

        private static Invoice Invoice(string amounts = XeroLineAmountTypes.Exclusive) => new()
        {
            InvoiceID = Guid.NewGuid(), InvoiceNumber = "INV-0042", Type = "ACCREC", Status = "AUTHORISED",
            DateString = "2026-11-23T00:00:00", DueDateString = "2026-12-23T00:00:00", LineAmountTypes = amounts,
            CurrencyCode = "USD", CurrencyRate = 1m, Reference = "PO-4471 [ZIMRA 77/ABCD-1234]",
            SubTotal = 200m, TotalTax = 30m, Total = 230m, AmountDue = 130m, AmountPaid = 100m, AmountCredited = 0m,
            Contact = new Contact { ContactID = Guid.NewGuid(), Name = "Kopje Traders" },
            LineItems = new()
            {
                amounts == XeroLineAmountTypes.Inclusive
                    ? new LineItem { ItemCode = "MILK", Description = "Milk 1L", Quantity = 2, UnitAmount = 115m, LineAmount = 230m, TaxAmount = 30m, TaxType = "OUTPUT",
                                     Tracking = new() { new LineItemTracking { Name = "Region", Option = "Harare" } } }
                    : new LineItem { ItemCode = "MILK", Description = "Milk 1L", Quantity = 2, UnitAmount = 100m, LineAmount = 200m, TaxAmount = 30m, TaxType = "OUTPUT",
                                     Tracking = new() { new LineItemTracking { Name = "Region", Option = "Harare" } } },
                new LineItem { Description = "Thank you for your order" }
            }
        };

        private static Contact FullContact() => new()
        {
            ContactID = Guid.NewGuid(), Name = "Kopje Traders", FirstName = "Tendai", LastName = "Moyo", AccountNumber = "KOP001",
            EmailAddress = "accounts@kopje.example", CompanyNumber = "100123456" /* 9 digits: a VAT number, not a registration */,
            Website = "kopje.example",
            Addresses = new()
            {
                new ContactAddress { AddressType = "POBOX", AddressLine1 = "P O Box 1234", City = "Harare", Country = "Zimbabwe" },
                new ContactAddress { AddressType = "STREET", AddressLine1 = "7 Herbert Chitepo Street", City = "Mutare", AttentionTo = "Stores" }
            },
            Phones = new() { new ContactPhone { PhoneType = "MOBILE", PhoneNumber = "771234567", PhoneCountryCode = "263" } },
            ContactGroups = new() { new ContactGroupRef { Name = "Wholesale", Status = "ACTIVE" } },
            Balances = new ContactBalances { AccountsReceivable = new ContactBalance { Outstanding = 1250m } },
            PaymentTerms = new ContactPaymentTerms { Sales = new PaymentTerm { Day = 30, Type = "DAYSAFTERBILLDATE" } }
        };

        private static FiscalRecord Record() => new()
        {
            DocumentNumber = "INV-0042", DocumentType = FiscalDocumentType.Invoice, ReceiptNumber = 77, GlobalNumber = 1077,
            DeviceId = 38292, FiscalDayNumber = 12, VerificationCode = "ABCD-1234-EFGH-5678", QrCodeUrl = "https://fdms.zimra.co.zw/0000038292/x"
        };

        [Fact]
        public void A_tax_exclusive_invoice_maps_net_lines_totals_and_the_fiscal_block()
        {
            var doc = XeroLayoutMapper.Map(new XeroLayoutSource { Document = Invoice(), Contact = FullContact(), TaxByType = Taxes, Record = Record() }, null);

            Assert.Equal("FISCAL TAX INVOICE", doc.Title);
            Assert.Equal("INV-0042", doc.Number);
            var line = Assert.Single(doc.Lines);                     // the description-only line is a note, not a sale
            Assert.Equal(100m, line.UnitPrice);
            Assert.Equal(200m, line.Amount);
            Assert.Equal(15m, line.TaxPercent);
            Assert.Equal("Thank you for your order", doc.Notes);
            Assert.Equal((200m, 30m, 230m, 100m), (doc.Subtotal, doc.TaxTotal, doc.Total, doc.AmountPaid));
            Assert.Equal("77", doc.Fiscal!.ReceiptNumber);
            Assert.False(doc.Fiscal.IsInvalid);
            Assert.Contains(doc.Meta, m => m.Label == "Due" && m.Value == "23 Dec 2026");
            Assert.Empty(doc.Fields);                                // no design asked for any field
        }

        [Fact]
        public void A_tax_inclusive_invoice_prints_net_amounts_so_the_totals_still_add_up()
        {
            var doc = XeroLayoutMapper.Map(new XeroLayoutSource { Document = Invoice(XeroLineAmountTypes.Inclusive), TaxByType = Taxes, Record = Record() }, null);
            var line = Assert.Single(doc.Lines);
            Assert.Equal(200m, line.Amount);
            Assert.Equal(100m, Math.Round(line.UnitPrice, 2));
            Assert.Equal(0m, line.DiscountAmount);
        }

        [Fact]
        public void Only_the_fields_the_design_uses_are_filled_and_our_marker_never_prints_as_the_reference()
        {
            var used = new[] { "ExtOrderNum", "Terms", "CustomerAccount", "BalanceDue", "DeliveryAddress", "ShipToName", "ContactPerson",
                               "CustomerGroup", "CustomerBalance", "CustomerRegistration", "Doc.Region", "CustomerMobile" };
            var doc = XeroLayoutMapper.Map(new XeroLayoutSource { Document = Invoice(), Contact = FullContact(), Record = Record() }, used);

            Assert.Equal("PO-4471", doc.Fields["ExtOrderNum"]);
            Assert.Equal("30 days after the invoice date", doc.Fields["Terms"]);
            Assert.Equal("KOP001", doc.Fields["CustomerAccount"]);
            Assert.Equal("130.00", doc.Fields["BalanceDue"]);
            Assert.Equal("7 Herbert Chitepo Street, Mutare", doc.Fields["DeliveryAddress"]);
            Assert.Equal("Stores", doc.Fields["ShipToName"]);
            Assert.Equal("Tendai Moyo", doc.Fields["ContactPerson"]);
            Assert.Equal("Wholesale", doc.Fields["CustomerGroup"]);
            Assert.Equal("1,250.00", doc.Fields["CustomerBalance"]);
            Assert.Equal("Harare", doc.Fields["Doc.Region"]);
            Assert.Equal("263771234567", doc.Fields["CustomerMobile"]);
            Assert.False(doc.Fields.ContainsKey("CustomerRegistration")); // a VAT number in the company number box is not a registration
            Assert.False(doc.Fields.ContainsKey("Currency"));             // not asked for

            Assert.NotNull(doc.ShipTo);                                   // street differs from postal: a ship-to block
            Assert.Equal("Stores", doc.ShipTo!.Name);
            Assert.Equal(new[] { "P O Box 1234", "Harare", "Zimbabwe" }, doc.Party!.AddressLines);
        }

        [Fact]
        public void Buyer_details_saved_in_Fiscalize_win_over_Xero()
        {
            var buyer = new ContactVatRecord { RegisteredName = "Kopje Traders (Pvt) Ltd", TradeName = "Kopje", Tin = "2000123456", VatNumber = "100123456",
                                               HouseNo = "4", Street = "Kopje Street", City = "Harare" };
            var doc = XeroLayoutMapper.Map(new XeroLayoutSource { Document = Invoice(), Contact = FullContact(), Buyer = buyer, Record = Record() }, null);
            Assert.Equal("Kopje Traders (Pvt) Ltd", doc.Party!.Name);
            Assert.Equal("Kopje", doc.Party.TradeName);
            Assert.Equal(("2000123456", "100123456"), (doc.Party.Tin, doc.Party.Vat));
            Assert.Equal(new[] { "4 Kopje Street", "Harare" }, doc.Party.AddressLines);
        }

        [Fact]
        public void A_credit_note_names_its_invoice_and_a_rejected_receipt_is_not_called_fiscal()
        {
            var credit = new CreditNote
            {
                CreditNoteID = Guid.NewGuid(), CreditNoteNumber = "CN-0007", Type = "ACCRECCREDIT", DateString = "2026-11-24T00:00:00",
                LineAmountTypes = XeroLineAmountTypes.Exclusive, SubTotal = 10m, TotalTax = 1.5m, Total = 11.5m, RemainingCredit = 0m,
                LineItems = new() { new LineItem { Description = "Return", Quantity = 1, UnitAmount = 10m, LineAmount = 10m, TaxAmount = 1.5m, TaxType = "OUTPUT" } }
            };
            var record = Record();
            record.ValidationError = "Red";
            var doc = XeroLayoutMapper.Map(new XeroLayoutSource { Document = credit, Record = record, BaseInvoiceNumber = "INV-0042" }, new[] { "BaseInvoiceNumber", "CreditRemaining" });
            Assert.Equal("CREDIT NOTE", doc.Title);
            Assert.Contains(doc.Meta, m => m.Label == "Against invoice" && m.Value == "INV-0042");
            Assert.Equal("INV-0042", doc.Fields["BaseInvoiceNumber"]);
            Assert.Equal("0.00", doc.Fields["CreditRemaining"]);
            Assert.True(doc.Fiscal!.IsInvalid);
        }

        [Theory]
        [InlineData(20, "DAYSAFTERBILLMONTH", "20 days after the end of the invoice month")]
        [InlineData(1, "OFFOLLOWINGMONTH", "1st of the following month")]
        [InlineData(22, "OFCURRENTMONTH", "22nd of the current month")]
        [InlineData(13, "OFCURRENTMONTH", "13th of the current month")]
        [InlineData(0, "OFCURRENTMONTH", null)]
        public void Payment_terms_read_like_Xero_says_them(int day, string type, string? expected) =>
            Assert.Equal(expected, XeroLayoutMapper.Terms(new PaymentTerm { Day = day, Type = type }));

        /// <summary>
        /// The keys this app fills must be the ones the platform lists for "Xero" (PluginBuiltInFields), which never get renamed.
        /// This list is that contract, copied: change both together.
        /// </summary>
        [Fact]
        public void The_field_keys_are_the_platforms_xero_keys()
        {
            var platform = new[]
            {
                "SalesRep", "Terms", "ShipMethod", "ExchangeRate", "CustomerAccount", "CustomerFax", "CustomerMobile", "CustomerCreditLimit",
                "ExtOrderNum", "BaseInvoiceNumber", "Currency", "TaxInclusive", "OnlineInvoiceUrl", "BalanceDue", "AmountApplied", "AmountCredited",
                "DocumentDiscount", "CreditRemaining", "DeliveryAddress", "ShipToName", "ContactPerson", "CustomerAddress", "CustomerPostalCode",
                "CustomerRegistration", "CustomerTelephone2", "CustomerWebsite", "CustomerGroup", "CustomerBalance"
            };
            var ours = XeroLayoutMapper.Fields.Select(f => f.Key).ToList();
            Assert.All(ours, k => Assert.Contains(k, platform));
            Assert.Equal(new[] { "SalesRep", "ShipMethod", "CustomerCreditLimit" }, platform.Except(ours).ToArray()); // Xero has no such thing
            Assert.DoesNotContain(ours, k => k.StartsWith("Doc.") || k.StartsWith("Customer."));
        }

        /// <summary>The real renderer, in this host (PDFsharp 6 for the stamp alongside PdfSharpCore, ImageSharp 3), draws every look on every paper.</summary>
        [Theory]
        [InlineData("A4", "modern")]
        [InlineData("80mm", "modern")]
        [InlineData("58mm", "modern")]
        public void The_renderer_draws_a_xero_document_here(string paper, string style)
        {
            var spec = LayoutSpec.Starter(paper, style);
            spec.MetaFields.Add(new FieldSpec { Key = "ExtOrderNum", Label = "Reference" });
            var package = LayoutPackage.Build(spec.Normalise(), 0, issuer: new LayoutIssuer { LegalName = "Bedford Trading", Tin = "2000123456" });
            var reparsed = LayoutPackage.TryParse(package.ToJson(), out var error);
            Assert.True(reparsed != null, error);

            var document = XeroLayoutMapper.Map(new XeroLayoutSource { Document = Invoice(), Contact = FullContact(), TaxByType = Taxes, Record = Record() },
                reparsed!.FieldsUsed);
            var pdf = FiscalLayoutRenderer.Render(reparsed, document);
            Assert.True(pdf.Length > 1000);
            Assert.Equal("%PDF", Encoding.ASCII.GetString(pdf, 0, 4));
        }
    }

    public class ConnectedAppReportTests
    {
        private static ClientConfig Config() => new()
        {
            Id = 5, AppUserCompanyId = 9, TenantId = Guid.Parse("8f5c3c4e-1b6d-4c55-9a39-5d8a5e2b3c11"), TenantName = "Demo Company (Global)",
            RefreshToken = "r", PlatformInstallationId = Guid.Parse("0b6c1d2e-3f40-4a5b-8c6d-7e8f9a0b1c2d"),
            FiscalPdfLayout = FiscalPdfLayouts.FiscalizeLayout, LayoutPaperSize = "A4", OrganisationShortCode = "!23eYt", OrganisationClass = "DEMO",
            OrganisationIsDemo = true, OrganisationBaseCurrency = "USD", LastSyncRunAt = new DateTime(2026, 11, 24, 18, 5, 0, DateTimeKind.Utc),
            PendingPdfsWithLayout = 3, PendingPdfsWithStandard = 1, PendingPdfFallbacks = 1, LastPdfFallbackReason = "the platform answered 503",
            LastPdfFallbackAt = new DateTime(2026, 11, 24, 17, 0, 0, DateTimeKind.Utc)
        };

        private static readonly LayoutPackageCache[] Held =
        {
            new() { DocumentType = "Invoice", PaperSize = "A4", Version = 3, Hash = "h3" },
            new() { DocumentType = "CreditNote", PaperSize = "A4", Version = 0, Hash = "std", IsStandardDesign = true },
            new() { DocumentType = "Invoice", PaperSize = "80mm", Version = 1, Hash = "h1" }
        };

        private static readonly ConnectedAppActivity Activity = new(new DateTime(2026, 11, 24, 18, 0, 0, DateTimeKind.Utc), 2, 41);

        [Fact]
        public void A_live_connection_reports_itself_as_a_connected_cloud_app_with_its_facts()
        {
            var r = ConnectedAppReport.Build(Config(), Held, Activity);
            Assert.Equal(("Xero", "cloud", "connected"), (r.PluginType, r.Hosting, r.ConnectionState));
            Assert.Equal("Demo Company (Global)", r.ErpCompany);
            Assert.Equal("Demo company", r.ErpVersion);
            Assert.Null(r.MachineName);
            Assert.True(r.UsePlatformLayouts);
            Assert.Equal(2, r.Layouts.Count);                                  // only the paper in use
            Assert.Equal((3, 1, 0, 1), (r.Prints!.WithLayout, r.Prints.WithStandard, r.Prints.Stamped, r.Prints.Fallbacks));
            var facts = r.Details.ToDictionary(d => d.Label, d => d);
            Assert.Equal("Fiscalize layout (A4)", facts["PDF attached in Xero"].Value);
            Assert.Equal("warn", facts["Documents waiting for a fix"].Tone);
            Assert.Equal("24 Nov 2026, 18:05 UTC", facts["Last check for new documents"].Value);
            Assert.Equal("8f5c3c4e-1b6d-4c55-9a39-5d8a5e2b3c11", facts["Organisation ID"].Value);
            Assert.True(r.Details.Count <= 24 && r.Details.All(d => d.Label.Length <= 60));
        }

        [Fact]
        public void Lost_tokens_a_failing_sync_and_a_disconnect_each_say_so()
        {
            var lost = Config(); lost.RefreshToken = null;
            Assert.Equal("attention", ConnectedAppReport.Connection(lost).State);
            var failing = Config(); failing.LastSyncError = "Xero answered 503";
            Assert.Contains("Xero answered 503", ConnectedAppReport.Connection(failing).Message);
            Assert.Equal("disconnected", ConnectedAppReport.Build(Config(), Held, Activity, disconnected: true).ConnectionState);
        }

        [Fact]
        public void With_the_stamp_chosen_no_layouts_are_claimed()
        {
            var c = Config(); c.FiscalPdfLayout = FiscalPdfLayouts.XeroStamp;
            var r = ConnectedAppReport.Build(c, Held, Activity);
            Assert.False(r.UsePlatformLayouts);
            Assert.Empty(r.Layouts);
        }

        /// <summary>
        /// The report as it goes over the wire (the Fiscalize RestClient: System.Text.Json, camelCase, nulls left out). The
        /// platform's test suite reads this same JSON (Fiscalyze.Api.Tests/PluginInstallations/Fixtures/xero.json); set
        /// WRITE_XERO_FIXTURE to a path to write it out again after changing the report.
        /// </summary>
        [Fact]
        public void The_wire_format_is_the_platform_contract()
        {
            var json = JsonSerializer.Serialize(ConnectedAppReport.Build(Config(), Held, Activity), new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, WriteIndented = true
            });
            foreach (var key in new[] { "\"installationId\"", "\"pluginType\": \"Xero\"", "\"hosting\": \"cloud\"", "\"connectionState\": \"connected\"",
                                        "\"details\"", "\"layouts\"", "\"prints\"", "\"withLayout\": 3" })
                Assert.Contains(key, json);
            Assert.DoesNotContain("machineName", json);
            var path = Environment.GetEnvironmentVariable("WRITE_XERO_FIXTURE");
            if (!string.IsNullOrWhiteSpace(path)) File.WriteAllText(path, json + "\n");
        }

        [Theory]
        [InlineData(null, FiscalPdfLayouts.XeroStamp)]
        [InlineData("fiscalizelayout", FiscalPdfLayouts.FiscalizeLayout)]
        [InlineData("FiscalizeStandard", FiscalPdfLayouts.FiscalizeStandard)]
        [InlineData("something", FiscalPdfLayouts.XeroStamp)]
        public void An_unknown_pdf_choice_means_the_stamp(string? value, string expected) => Assert.Equal(expected, FiscalPdfLayouts.Normalise(value));
    }
}
