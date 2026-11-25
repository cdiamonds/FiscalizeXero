using FiscalizeXero.Api.Controllers.Xero;
using FiscalizeXero.Api.Data;
using FiscalizeXero.Api.Services.Fiscalization;
using FiscalizeXero.Xero.Models;
using FiscalizeXero.Shared.Models.Fiscalize;
using FiscalizeXero.Shared.Models.TaxDetails;
using Xunit;

namespace FiscalizeXero.Api.Tests
{
    /// <summary>Buyer details typed on the Customers page win over the Xero contact, field by field.</summary>
    public class CustomerOverrideTests
    {
        /// <summary>A Xero contact with nothing but a name.</summary>
        private static Contact BareCustomer() => new() { ContactID = Guid.Parse("7b96e86b-418e-48e8-8949-308c14aec278"), Name = "Acme" };

        [Fact]
        public void EverythingTypedInFiscalizeMakesABareCustomerCompliant()
        {
            var saved = new ContactVatRecord
            {
                Tin = "2000123456", VatNumber = "220123456", Phone = "077 123 4567", Email = "accounts@acme.co.zw",
                HouseNo = "12", Street = "Samora Machel Avenue", City = "Harare", Province = "Harare"
            };

            var a = BuyerCompliance.Assess(BuyerResolver.Resolve(BareCustomer(), saved), BareCustomer());

            Assert.Equal(BuyerStatus.Compliant, a.Status);
            Assert.Equal("0771234567", a.Phone);
            Assert.Equal(BuyerCompliance.SavedSource, a.PhoneSource);
            var data = BuyerCompliance.BuildBuyerData(a, FiscalizationValidationMode.Strict)!;
            Assert.Equal("Samora Machel Avenue", data.Address!.Street);
            Assert.Equal("accounts@acme.co.zw", data.Contacts!.Email);
        }

        [Fact]
        public void SavedFieldsWinAndXeroFillsTheRest()
        {
            var xero = BareCustomer();
            xero.Phones = new() { new ContactPhone { PhoneType = "DEFAULT", PhoneNumber = "0242701234" } };
            xero.EmailAddress = "old@acme.co.zw";
            xero.Addresses = new() { new ContactAddress { AddressType = "POBOX", AddressLine1 = "12 Samora Machel Avenue", City = "Harare", Region = "Harare" } };
            var saved = new ContactVatRecord { Tin = "2000123456", Email = "new@acme.co.zw", City = "Bulawayo", Province = "Bulawayo" };

            var a = BuyerCompliance.Assess(BuyerResolver.Resolve(xero, saved), xero);

            Assert.Equal("new@acme.co.zw", a.Email);
            Assert.Equal("0242701234", a.Phone);
            Assert.Equal("Phone", a.PhoneSource);
            Assert.Equal("12", a.Address.HouseNo);
            Assert.Equal("Bulawayo", a.Address.City);
            Assert.Equal(BuyerStatus.Compliant, a.Status);
        }

        [Fact]
        public void MissingDetailsMessagePointsToTheCustomersPage()
        {
            var a = BuyerCompliance.Assess(BuyerResolver.Resolve(BareCustomer(), new ContactVatRecord { Tin = "2000123456" }), BareCustomer());
            var ex = Assert.Throws<BuyerDetailsException>(() => BuyerCompliance.BuildBuyerData(a, FiscalizationValidationMode.Strict));
            Assert.Contains("Customers page", ex.Message);
        }

        [Fact]
        public void ContactOnlyOverrideCounts()
            => Assert.True(BuyerResolver.HasOverride(new ContactVatRecord { Street = "Fife Street" }));

        [Fact]
        public void SaveCleansSeparatorsAndPhone()
        {
            var (v, error) = CustomersController.Normalise(new SaveCustomerTaxDetailsModel
            {
                Tin = "200-012 3456", VatNumber = "220 123 456", Phone = "+263 77 123 4567", Email = " a@b.co.zw ", Street = "  ", City = " Gweru "
            });
            Assert.Null(error);
            Assert.Equal("2000123456", v.Tin);
            Assert.Equal("220123456", v.VatNumber);
            Assert.Equal("+263771234567", v.Phone);
            Assert.Equal("a@b.co.zw", v.Email);
            Assert.Equal("", v.Street);              // blank = cleared, not "use Xero"
            Assert.Equal("Gweru", v.City);
        }

        [Theory]
        [InlineData("123", null, null, null, "TIN")]
        [InlineData("0000000000", null, null, null, "TIN")]
        [InlineData(null, "12345", null, null, "VAT")]
        [InlineData(null, null, "12", null, "phone")]
        [InlineData(null, null, null, "not-an-email", "email")]
        public void SaveRejectsWhatZimraWouldReject(string? tin, string? vat, string? phone, string? email, string word)
        {
            var (_, error) = CustomersController.Normalise(new SaveCustomerTaxDetailsModel { Tin = tin, VatNumber = vat, Phone = phone, Email = email });
            Assert.NotNull(error);
            Assert.Contains(word, error, StringComparison.OrdinalIgnoreCase);
        }
    }
}
