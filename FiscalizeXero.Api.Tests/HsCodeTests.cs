using FiscalizeXero.Api.Controllers.Xero;
using FiscalizeXero.Api.Data;
using FiscalizeXero.Api.Services.Fiscalization;
using FiscalizeXero.Api.Services.HsCodes;
using FiscalizeXero.Shared.Models.Fiscalize;
using FiscalizeXero.Xero.Models;
using Xunit;

namespace FiscalizeXero.Api.Tests
{
    /// <summary>HS codes: per Xero item code, then the company default, then the system default 04021099.</summary>
    public class DefaultHsCodeTests
    {
        private static FiscalizeInvoiceLine Line(int no, string name, string? hs) => new() { LineNo = no, ItemName = name, ItemHsCode = hs ?? string.Empty };

        [Fact]
        public void LinesWithoutACodeTakeTheCompanyDefault()
        {
            var lines = new[] { Line(1, "Milk", "04021000"), Line(2, "Bread", null), Line(3, "Butter", " ") };
            ItemHsCodes.ApplyDefault(lines, " 04021099 ");
            Assert.Equal(new[] { "04021000", "04021099", "04021099" }, lines.Select(l => l.ItemHsCode));
        }

        [Fact]
        public void WithoutADefaultMissingCodesStillBlockFiscalisation()
        {
            var lines = new[] { Line(1, "Milk", "04021000"), Line(2, "Bread", null) };
            var ex = Assert.Throws<FiscalizeXero.Api.Exceptions.UserFacingException>(() => ItemHsCodes.ApplyDefault(lines, null));
            Assert.Contains("Bread", ex.Message);
            Assert.DoesNotContain("Milk", ex.Message);
            Assert.Contains("default HS code", ex.Message);
        }

        [Fact]
        public void BlankDefaultCountsAsNoDefault()
            => Assert.Throws<FiscalizeXero.Api.Exceptions.UserFacingException>(() => ItemHsCodes.ApplyDefault(new[] { Line(1, "Bread", null) }, "   "));

        [Fact]
        public void CompanyDefaultElseSystemDefault()
        {
            Assert.Equal("04021099", ClientConfig.SystemDefaultHsCode);
            Assert.Equal("04021099", new ClientConfig().EffectiveDefaultHsCode);
            Assert.Equal("04021099", new ClientConfig { DefaultHsCode = " " }.EffectiveDefaultHsCode);
            Assert.Equal("84212300", new ClientConfig { DefaultHsCode = " 84212300 " }.EffectiveDefaultHsCode);
        }

        [Fact]
        public void LookupIsByItemCodeIgnoringCaseAndSpaces()
        {
            var codes = ItemHsCodes.Lookup(new (string, string?)[] { (" MILK-1 ", "04021000"), ("BREAD", null), ("", "11111111") });
            Assert.Equal("04021000", codes["milk-1"]);
            Assert.False(codes.ContainsKey("BREAD"));
            Assert.Single(codes);
        }

        [Fact]
        public void ALineWithNoItemCodeTakesTheDefault_EndToEndThroughTheMapper()
        {
            var invoice = new Invoice
            {
                InvoiceID = Guid.NewGuid(), InvoiceNumber = "INV-1", Type = "ACCREC", Status = "AUTHORISED", LineAmountTypes = "Exclusive",
                DateString = "2026-11-23T00:00:00", CurrencyCode = "USD", Total = 23m,
                LineItems = new()
                {
                    new LineItem { Description = "Milk", ItemCode = "MILK-1", Quantity = 1, UnitAmount = 10m, LineAmount = 10m, TaxAmount = 1.5m, TaxType = "OUTPUT" },
                    new LineItem { Description = "Delivery", Quantity = 1, UnitAmount = 10m, LineAmount = 10m, TaxAmount = 1.5m, TaxType = "OUTPUT" }
                }
            };
            var request = XeroFiscalizeMapper.MapInvoice(invoice, 1, null, ItemHsCodes.Lookup(new (string, string?)[] { ("milk-1", "04021000") }));
            ItemHsCodes.ApplyDefault(request.Lines, new ClientConfig().EffectiveDefaultHsCode);
            Assert.Equal(new[] { "04021000", "04021099" }, request.Lines.Select(l => l.ItemHsCode));
        }

        [Theory]
        [InlineData("8421.23.00", "84212300")]
        [InlineData("0402 10 99", "04021099")]
        [InlineData("040210", "040210")]
        [InlineData("MOT-4100", null)]
        [InlineData("12345", null)]
        [InlineData("12345678901", null)]
        [InlineData("", null)]
        public void HsCodeFormat(string raw, string? expected) => Assert.Equal(expected, ItemHsCodes.AsHsCode(raw));

        [Fact]
        public void DescribeSaysWhereTheCodeComesFrom()
        {
            var item = new Item { ItemID = Guid.NewGuid(), Code = "MILK-1", Name = "Milk" };
            var saved = ItemHsCodes.Describe("MILK-1", item, "04021000", "04021099");
            Assert.Equal(("04021000", "Saved", "Milk"), (saved.HsCode, saved.HsCodeSource, saved.Name));
            var fallback = ItemHsCodes.Describe("MILK-1", null, null, "04021099");
            Assert.Equal(("04021099", "Default"), (fallback.HsCode, fallback.HsCodeSource));
            Assert.Null(fallback.ItemId);
        }
    }

    public class ItemFilterTests
    {
        private static readonly Item[] Items =
        {
            new() { Code = "MILK-1", Name = "Fresh milk" },
            new() { Code = "BR-2", Name = "Bread", Description = "White loaf, O'Neil bakery" },
            new() { Code = null, Name = "Milk without a code" },
        };

        [Fact]
        public void No_search_lists_items_that_have_a_code()
            => Assert.Equal(new[] { "MILK-1", "BR-2" }, ItemsController.Filter(Items, null).Select(i => i.Code));

        [Fact]
        public void Search_matches_part_of_code_name_or_description_any_case()
        {
            Assert.Equal(new[] { "MILK-1" }, ItemsController.Filter(Items, " milk ").Select(i => i.Code));
            Assert.Equal(new[] { "BR-2" }, ItemsController.Filter(Items, "br-").Select(i => i.Code));
            Assert.Equal(new[] { "BR-2" }, ItemsController.Filter(Items, "O'Neil").Select(i => i.Code));
        }
    }
}
