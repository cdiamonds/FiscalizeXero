using FiscalizeXero.Api.Services.Fiscalization;
using FiscalizeXero.Shared.Models.Fiscalize;
using Xunit;

namespace FiscalizeXero.Api.Tests
{
    public class BuyerDataValidationTests
    {
        private static UpdateInvoiceFiscalInfoBuyerData Valid() => new()
        {
            RegisteredName = "Acme Trading (Pvt) Ltd", TradeName = "Acme", Tin = "1234567890", VatNumber = "123456789",
            Contacts = new UpdateInvoiceFiscalInfoBuyerContacts { PhoneNumber = "+263771234567", Email = "accounts@acme.example" },
            Address = new UpdateInvoiceFiscalInfoBuyerAddress { Province = "Harare", City = "Harare", HouseNo = "42", Street = "Samora Machel Ave" }
        };

        [Fact]
        public void CompleteBuyerPasses() => Assert.Null(FiscalizationService.ValidateBuyerData(Valid()));

        [Fact]
        public void TinMustBeTenDigits()
        {
            var b = Valid(); b.Tin = "12345";
            Assert.Contains("10 numeric digits", FiscalizationService.ValidateBuyerData(b));
        }

        [Fact]
        public void ZeroTinIsRejected()
        {
            var b = Valid(); b.Tin = "0000000000";
            Assert.Contains("0000000000", FiscalizationService.ValidateBuyerData(b));
        }

        [Fact]
        public void VatIsOptionalButMustBeNineDigitsWhenGiven()
        {
            var b = Valid(); b.VatNumber = null;
            Assert.Null(FiscalizationService.ValidateBuyerData(b));
            b.VatNumber = "12";
            Assert.Contains("9 numeric digits", FiscalizationService.ValidateBuyerData(b));
        }

        [Fact]
        public void ContactAndAddressAreRequiredWithATin()
        {
            var b = Valid(); b.Contacts!.Email = " ";
            Assert.Contains("email", FiscalizationService.ValidateBuyerData(b));
            b = Valid(); b.Address!.City = null;
            Assert.Contains("city", FiscalizationService.ValidateBuyerData(b));
        }

        [Fact]
        public void MissingBuyerPointsToTheCashSaleOption()
            => Assert.Contains("cash sale", FiscalizationService.ValidateBuyerData(null));
    }
}
