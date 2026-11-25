using FiscalizeXero.Api.Data;
using FiscalizeXero.Api.Services.Fiscalization;
using FiscalizeXero.Xero.Models;
using Xunit;

namespace FiscalizeXero.Api.Tests
{
    public class BuyerResolverTests
    {
        private static readonly Guid Id = Guid.Parse("5b96e86b-418e-48e8-8949-308c14aec278");

        private static Contact Xero(string? taxNumber = "2000123456", string? companyNumber = "220123456") => new()
        {
            ContactID = Id, Name = "Acme (Pvt) Ltd", TaxNumber = taxNumber, CompanyNumber = companyNumber
        };

        [Fact]
        public void FallsBackToTheXeroContact()
        {
            var r = BuyerResolver.Resolve(Xero(), null);
            Assert.Equal("Acme (Pvt) Ltd", r.RegisteredName);
            Assert.Equal("Acme (Pvt) Ltd", r.TradeName);
            Assert.Equal("2000123456", r.Tin);
            Assert.Equal("220123456", r.VatNumber);
            Assert.Equal(Id.ToString(), r.ContactId);
        }

        [Fact]
        public void SavedValuesWinPerField()
        {
            var saved = new ContactVatRecord { Tin = "  2000999999  ", VatNumber = "" };
            var r = BuyerResolver.Resolve(Xero(), saved);
            Assert.Equal("2000999999", r.Tin);     // saved wins, trimmed
            Assert.Null(r.VatNumber);              // "" means the user cleared it: Xero's value is not used
        }

        [Fact]
        public void WorksWithoutAXeroContact()
        {
            var r = BuyerResolver.Resolve(null, new ContactVatRecord { ContactId = "9", Tin = "T" });
            Assert.Equal("9", r.ContactId);
            Assert.Equal("T", r.Tin);
            Assert.Null(r.VatNumber);
        }

        [Fact]
        public void HasOverrideOnlyWhenSomethingSaved()
        {
            Assert.False(BuyerResolver.HasOverride(null));
            Assert.False(BuyerResolver.HasOverride(new ContactVatRecord()));
            Assert.True(BuyerResolver.HasOverride(new ContactVatRecord { Tin = " " })); // a cleared value is a choice too
            Assert.True(BuyerResolver.HasOverride(new ContactVatRecord { VatNumber = "1" }));
        }
    }

    /// <summary>Exactly 10 digits is a TIN, exactly 9 digits is a VAT number, whichever of the contact's two boxes it is in.</summary>
    public class XeroTaxIdentifierTests
    {
        [Theory]
        [InlineData("2000123456", "220123456", "2000123456", "220123456")] // the expected boxes
        [InlineData("220123456", "2000123456", "2000123456", "220123456")] // swapped: the format decides
        [InlineData("2000123456", null, "2000123456", null)]
        [InlineData(null, "2000123456", "2000123456", null)]               // TIN typed into Company Number
        [InlineData("220123456", null, null, "220123456")]                 // only a VAT number, in Tax Number
        [InlineData("200-012 3456", "220 123 456", "200-012 3456", "220 123 456")] // separators don't change what it is
        [InlineData("12345", "REG 1234/2019", null, null)]                 // neither: a registration number is not a tax id
        [InlineData("12345678901", "12345678", null, null)]                // 11 and 8 digits are neither
        [InlineData("", "  ", null, null)]
        public void ClassifiesByFormat(string? taxNumber, string? companyNumber, string? tin, string? vat)
        {
            var r = XeroTaxIdentifiers.Classify(taxNumber, companyNumber);
            Assert.Equal(tin, r.Tin);
            Assert.Equal(vat, r.VatNumber);
        }

        [Fact]
        public void TaxNumberWinsWhenBothBoxesHoldTheSameKind()
        {
            var r = XeroTaxIdentifiers.Classify("2000123456", "2000999999");
            Assert.Equal("2000123456", r.Tin);
            Assert.Null(r.VatNumber);
        }

        [Fact]
        public void ATinInTheWrongBoxStillMakesTheCustomerATinBuyer()
        {
            var contact = new Contact { ContactID = Guid.NewGuid(), Name = "Acme", CompanyNumber = "2000123456" };
            var a = BuyerCompliance.Assess(BuyerResolver.Resolve(contact, null), contact);
            Assert.Equal("2000123456", a.Tin);
            Assert.Equal(BuyerStatus.MissingDetails, a.Status); // a TIN buyer, not silently a cash sale
        }
    }
}
