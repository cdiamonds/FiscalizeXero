using FiscalizeXero.Api.Controllers.Xero;
using FiscalizeXero.Api.Data;
using FiscalizeXero.Api.Services.Fiscalization;
using FiscalizeXero.Shared.Models.TaxDetails;
using FiscalizeXero.Xero.Models;
using Xunit;

namespace FiscalizeXero.Api.Tests
{
    /// <summary>What a save on the Customers page writes to the Xero contact (TIN → Tax Number, VAT → Company Number), and what stays in Fiscalize.</summary>
    public class XeroContactWritebackTests
    {
        private static Contact Xero(string? taxNumber = null, string? companyNumber = null) => new()
        {
            ContactID = Guid.Parse("5b96e86b-418e-48e8-8949-308c14aec278"), Name = "Acme", TaxNumber = taxNumber, CompanyNumber = companyNumber
        };

        [Fact]
        public void TinGoesToTaxNumberAndVatToCompanyNumber()
        {
            var u = XeroContactWriteback.Build(Xero(), new ContactVatRecord { Tin = "2000123456", VatNumber = "220123456", Phone = "0771234567" })!;
            Assert.Equal("2000123456", u.TaxNumber);
            Assert.Equal("220123456", u.CompanyNumber);
        }

        [Fact]
        public void NothingXeroCanHoldMeansNothingSent()
            => Assert.Null(XeroContactWriteback.Build(Xero(), new ContactVatRecord { TradeName = "Acme Shop", Phone = "0771234567", City = "Harare" }));

        [Fact]
        public void UnchangedValuesAreNotSentAgain()
            => Assert.Null(XeroContactWriteback.Build(Xero("2000123456", "220123456"), new ContactVatRecord { Tin = "2000123456", VatNumber = "220 123 456" }));

        [Fact]
        public void OnlyTheChangedBoxIsSent()
        {
            var u = XeroContactWriteback.Build(Xero("2000123456", "220123456"), new ContactVatRecord { VatNumber = "220999999" })!;
            Assert.Null(u.TaxNumber);                 // null = leave alone
            Assert.Equal("220999999", u.CompanyNumber);
        }

        [Fact]
        public void ATinFoundInCompanyNumberIsMovedNotLost()
        {
            // Xero holds the TIN in the wrong box; the user types only a VAT number.
            var u = XeroContactWriteback.Build(Xero(null, "2000123456"), new ContactVatRecord { VatNumber = "220123456" })!;
            Assert.Equal("2000123456", u.TaxNumber);
            Assert.Equal("220123456", u.CompanyNumber);
        }

        [Fact]
        public void ABoxHoldingSomethingElseIsLeftAlone()
        {
            var contact = Xero(null, "REG 1234/2019");
            var typed = new ContactVatRecord { Tin = "2000123456", VatNumber = "220123456" };
            var u = XeroContactWriteback.Build(contact, typed)!;
            Assert.Equal("2000123456", u.TaxNumber);
            Assert.Null(u.CompanyNumber);             // the registration number is the user's
            Assert.Equal(new[] { "Company Number" }, XeroContactWriteback.BoxesLeftAlone(contact, typed));

            // After the update the VAT number is still not in Xero, so it stays in Fiscalize; the TIN is dropped.
            var kept = XeroContactWriteback.DropHeldByXero(typed, Xero("2000123456", "REG 1234/2019"));
            Assert.Null(kept.Tin);
            Assert.Equal("220123456", kept.VatNumber);
        }

        [Fact]
        public void ValuesXeroNowHoldsAreDroppedTheRestStay()
        {
            var typed = new ContactVatRecord { Tin = "2000123456", VatNumber = "220123456", TradeName = "Acme Shop", Email = "a@acme.co.zw", Phone = "0771234567" };
            var kept = XeroContactWriteback.DropHeldByXero(typed, Xero("2000123456", "220123456"));
            Assert.Null(kept.Tin);
            Assert.Null(kept.VatNumber);
            Assert.Equal("Acme Shop", kept.TradeName);   // Xero has no home for these
            Assert.Equal("a@acme.co.zw", kept.Email);
            Assert.Equal("0771234567", kept.Phone);
        }
    }

    /// <summary>An emptied box on the customer page clears the value (""), it doesn't fall back to Xero (null).</summary>
    public class ClearingValuesTests
    {
        private static Contact Xero() => new()
        {
            ContactID = Guid.Parse("5b96e86b-418e-48e8-8949-308c14aec278"), Name = "Acme (Pvt) Ltd",
            TaxNumber = "2000123456", CompanyNumber = "220123456", EmailAddress = "a@acme.co.zw",
            Phones = new() { new ContactPhone { PhoneType = "DEFAULT", PhoneNumber = "0771234567" } },
            Addresses = new() { new ContactAddress { AddressType = "POBOX", AddressLine1 = "12 Samora Machel Avenue", City = "Harare", Region = "Harare" } }
        };

        [Fact]
        public void ClearedVatAndPhoneAreNotTakenFromXero()
        {
            var saved = new ContactVatRecord { VatNumber = "", Phone = "" };
            var a = BuyerCompliance.Assess(BuyerResolver.Resolve(Xero(), saved), Xero());
            Assert.Null(a.VatNumber);
            Assert.Null(a.Phone);
            Assert.Contains(BuyerCompliance.MissingPhone, a.Missing);
            Assert.Equal("2000123456", a.Tin); // untouched values still come from Xero
        }

        [Fact]
        public void SaveKeepsEmptyAsClearAndNullAsNotSet()
        {
            var (v, error) = CustomersController.Normalise(new SaveCustomerTaxDetailsModel { VatNumber = "", Phone = "  ", Email = null });
            Assert.Null(error);
            Assert.Equal("", v.VatNumber);
            Assert.Equal("", v.Phone);
            Assert.Null(v.Email);
        }

        [Fact]
        public void ClearingWritesAnEmptyValueToXero()
        {
            var u = XeroContactWriteback.Build(Xero(), new ContactVatRecord { VatNumber = "" })!;
            Assert.Equal("", u.CompanyNumber);   // "" clears the box in Xero
            Assert.Null(u.TaxNumber);            // TIN untouched
        }

        [Fact]
        public void ClearStaysWhileXeroStillHasTheValue()
        {
            var typed = new ContactVatRecord { VatNumber = "", Tin = "" };
            var after = Xero();
            after.TaxNumber = null;              // Xero cleared the TIN, but kept the VAT number
            var kept = XeroContactWriteback.DropHeldByXero(typed, after);
            Assert.Equal("", kept.VatNumber);
            Assert.Null(kept.Tin);
        }
    }
}
