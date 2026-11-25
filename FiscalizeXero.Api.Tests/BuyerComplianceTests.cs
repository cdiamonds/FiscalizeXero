using FiscalizeXero.Api.Data;
using FiscalizeXero.Api.Services.Fiscalization;
using FiscalizeXero.Xero.Models;
using FiscalizeXero.Shared.Models.Fiscalize;
using Xunit;

namespace FiscalizeXero.Api.Tests
{
    public class BuyerContactResolverTests
    {
        [Theory]
        [InlineData("+263 77 123 4567", "+263771234567")]
        [InlineData("0771 234 567", "0771234567")]
        [InlineData("(024) 270-1234", "0242701234")]
        [InlineData("00263771234567", "+263771234567")]
        [InlineData("0771234567 ext 12", "0771234567")]
        [InlineData("0771234567 / 0712345678", "0771234567")]
        [InlineData("n/a / 0712345678", "0712345678")]
        public void NormalisesRealWorldNumbers(string raw, string expected)
            => Assert.Equal(expected, BuyerContactResolver.FirstPhone(raw));

        [Theory]
        [InlineData("n/a")]
        [InlineData("0")]
        [InlineData("000000000")]
        [InlineData("12345")]
        [InlineData("")]
        [InlineData(null)]
        public void RejectsUnusableNumbers(string? raw) => Assert.Null(BuyerContactResolver.FirstPhone(raw));

        [Fact]
        public void PhoneEmptyFallsBackToMobile()
            => Assert.Equal("0712345678", BuyerContactResolver.FirstPhone(null, "  ", "0712 345 678"));

        [Fact]
        public void PhoneWinsWhenBothPresent()
            => Assert.Equal("0242701234", BuyerContactResolver.FirstPhone("024 270 1234", "0712345678"));

        [Theory]
        [InlineData("accounts@acme.co.zw", "accounts@acme.co.zw")]
        [InlineData("accounts@acme.co.zw, ceo@acme.co.zw", "accounts@acme.co.zw")]
        [InlineData("not an email; billing@acme.com", "billing@acme.com")]
        [InlineData("<billing@acme.com>", "billing@acme.com")]
        public void PicksFirstValidEmail(string raw, string expected) => Assert.Equal(expected, BuyerContactResolver.FirstEmail(raw));

        [Theory]
        [InlineData("none")]
        [InlineData("acme@")]
        [InlineData("acme.co.zw")]
        public void RejectsInvalidEmail(string raw) => Assert.Null(BuyerContactResolver.FirstEmail(raw));
    }

    public class BuyerAddressParserTests
    {
        private static BuyerAddress Parse(string? line1, string? line2 = null, string? city = null, string? province = null, string? country = null, params string[] names)
            => BuyerAddressParser.Parse(new RawAddress(new[] { line1, line2 }, city, province, null, country), names);

        [Fact]
        public void LeadingNumber()
        {
            var a = Parse("12 Samora Machel Avenue", city: "Harare");
            Assert.Equal("12", a.HouseNo);
            Assert.Equal("Samora Machel Avenue", a.Street);
            Assert.Equal("Harare", a.City);
            Assert.Equal("Harare", a.Province); // worked out from the city
        }

        [Theory]
        [InlineData("12A Main Street", "12A", "Main Street")]
        [InlineData("12-14 Main Street", "12-14", "Main Street")]
        [InlineData("Stand 4512, Borrowdale", "Stand 4512", "Borrowdale")]
        [InlineData("Stand No. 4512 Borrowdale Road", "Stand 4512", "Borrowdale Road")]
        [InlineData("Plot 7 Airport Road", "Plot 7", "Airport Road")]
        [InlineData("Shop 3, Avondale Shopping Centre", "Shop 3", "Avondale Shopping Centre")]
        [InlineData("No. 5 Lomagundi Road", "5", "Lomagundi Road")]
        [InlineData("#14 Fife Avenue", "14", "Fife Avenue")]
        [InlineData("Josiah Tongogara Street 45", "45", "Josiah Tongogara Street")]
        public void FindsHouseNumberInItsUsualForms(string line, string house, string street)
        {
            var a = Parse(line);
            Assert.Equal(house, a.HouseNo);
            Assert.Equal(street, a.Street);
        }

        [Theory]
        [InlineData("Main Street")]
        [InlineData("2nd Street Extension")]
        [InlineData("1st Floor, Eastgate Building")]
        [InlineData("Norton Road")]
        public void NeverTakesWordsOrOrdinalsAsHouseNumbers(string line) => Assert.Null(Parse(line).HouseNo);

        [Fact]
        public void HouseNumberOnSecondLineWhenFirstIsFloorOrBuilding()
        {
            var a = Parse("2nd Floor, Eastgate Building", "12 Robert Mugabe Road");
            Assert.Equal("12", a.HouseNo);
            Assert.Equal("Robert Mugabe Road", a.Street);
        }

        [Fact]
        public void SkipsLineThatRepeatsCustomerName()
        {
            var a = Parse("ACME (PVT) LTD", "45 Coventry Road", city: "Harare", names: "Acme (Pvt) Ltd");
            Assert.Equal("45", a.HouseNo);
            Assert.Equal("Coventry Road", a.Street);
        }

        [Fact]
        public void FindsCityAndProvinceInFreeTextLines()
        {
            var a = Parse("12 Main Street, Avondale, Harare");
            Assert.Equal("12", a.HouseNo);
            Assert.Equal("Main Street, Avondale", a.Street);
            Assert.Equal("Harare", a.City);
            Assert.Equal("Harare", a.Province);
        }

        [Fact]
        public void CityWithoutCommaAtEndOfLine()
        {
            var a = Parse("8 Coventry Road", "Workington Harare");
            Assert.Equal("Harare", a.City);
            Assert.Equal("Coventry Road, Workington", a.Street);
        }

        [Fact]
        public void SuburbNamesTheCity()
        {
            var a = Parse("14 Fife Avenue", "Borrowdale");
            Assert.Equal("Harare", a.City);
            Assert.Equal("Harare", a.Province);
        }

        [Theory]
        [InlineData("Mutare", "Manicaland")]
        [InlineData("Gweru", "Midlands")]
        [InlineData("Victoria Falls", "Matabeleland North")]
        [InlineData("Bulawayo", "Bulawayo")]
        public void ProvinceFromCity(string city, string province) => Assert.Equal(province, Parse("1 Main Street", city: city).Province);

        [Theory]
        [InlineData("ME", "Mashonaland East")]
        [InlineData("Mash West", "Mashonaland West")]
        [InlineData("Mat South", "Matabeleland South")]
        [InlineData("Harare Metropolitan", "Harare")]
        public void ProvinceCodesAndShortFormsWrittenOut(string raw, string expected)
            => Assert.Equal(expected, Parse("1 Main Street", city: "Somewhere", province: raw).Province);

        [Fact]
        public void IsoCodeNotTrustedOutsideZimbabwe()
            => Assert.Equal("MA", Parse("1 Main Street", city: "Boston", province: "MA", country: "USA").Province);

        [Fact]
        public void PoBoxIsNotAStreet()
        {
            var a = Parse("P.O. Box 1234", city: "Harare");
            Assert.Null(a.Street);
            Assert.Null(a.HouseNo);
        }

        [Fact]
        public void NumberAloneOnLineTakesNextLineAsStreet()
        {
            var a = Parse("12", "Main Street");
            Assert.Equal("12", a.HouseNo);
            Assert.Equal("Main Street", a.Street);
        }

        [Fact]
        public void LongLabelledNumberFallsBackToTheNumber()
        {
            var a = Parse("Stand 12345678 Main Road");
            Assert.Equal("12345678", a.HouseNo); // "Stand 12345678" is over ZIMRA's 10 characters
            Assert.Equal("Main Road", a.Street);
        }

        [Fact]
        public void EmptyAddressGivesNothing()
        {
            var a = Parse(null);
            Assert.Equal(new BuyerAddress(null, null, null, null), a);
        }
    }

    public class BuyerComplianceTests
    {
        /// <summary>A Xero contact: DEFAULT phone, MOBILE phone, email and a billing (POBOX) address.</summary>
        private static Contact Customer(string? phone = "0242701234", string? mobile = "0771234567", string? email = "accounts@acme.co.zw",
            string? line1 = "12 Samora Machel Avenue", string? city = "Harare", string? province = "Harare")
            => new()
            {
                ContactID = Guid.Parse("5b96e86b-418e-48e8-8949-308c14aec278"),
                Name = "Acme",
                EmailAddress = email,
                Phones = new List<ContactPhone>
                {
                    new() { PhoneType = "DEFAULT", PhoneNumber = phone },
                    new() { PhoneType = "MOBILE", PhoneNumber = mobile },
                    new() { PhoneType = "DDI" },
                    new() { PhoneType = "FAX", PhoneNumber = "0242000000" }
                },
                Addresses = new List<ContactAddress>
                {
                    new() { AddressType = "STREET" },
                    new() { AddressType = "POBOX", AddressLine1 = line1, City = city, Region = province }
                }
            };

        private static ContactVatRecord Identity(string? tin = "2000123456", string? vat = null)
            => new() { RegisteredName = "Acme (Pvt) Ltd", TradeName = "Acme", Tin = tin, VatNumber = vat };

        [Fact]
        public void CustomerWithPhoneAndMobileIsCompliant()
        {
            var a = BuyerCompliance.Assess(Identity(), Customer());
            Assert.Equal(BuyerStatus.Compliant, a.Status);
            Assert.Equal("0242701234", a.Phone);
            Assert.Equal("Phone", a.PhoneSource);
        }

        [Fact]
        public void MobileOnlyIsEnough_TheReportedBug()
        {
            var a = BuyerCompliance.Assess(Identity(), Customer(phone: null));
            Assert.Equal(BuyerStatus.Compliant, a.Status);
            Assert.Equal("0771234567", a.Phone);
            Assert.Equal("Mobile", a.PhoneSource);
        }

        [Fact]
        public void BuyerBlockCarriesPhoneEmailAndAddress()
        {
            var data = BuyerCompliance.BuildBuyerData(BuyerCompliance.Assess(Identity(), Customer()), FiscalizationValidationMode.Strict)!;
            Assert.Equal("2000123456", data.Tin);
            Assert.Equal("0242701234", data.Contacts!.PhoneNumber);
            Assert.Equal("accounts@acme.co.zw", data.Contacts.Email);
            Assert.Equal("12", data.Address!.HouseNo);
            Assert.Equal("Samora Machel Avenue", data.Address.Street);
            Assert.Equal("Harare", data.Address.City);
            Assert.Equal("Harare", data.Address.Province);
        }

        [Fact]
        public void NoTinIsCashSaleWithNoBuyerBlock()
        {
            var a = BuyerCompliance.Assess(Identity(tin: null), Customer(phone: null, mobile: null, email: null, line1: null, city: null, province: null));
            Assert.Equal(BuyerStatus.NoTin, a.Status);
            Assert.Empty(a.Missing);
            Assert.Null(BuyerCompliance.BuildBuyerData(a, FiscalizationValidationMode.Strict));
        }

        [Fact]
        public void StrictListsEverythingMissing()
        {
            var a = BuyerCompliance.Assess(Identity(), Customer(phone: null, mobile: null, line1: "Samora Machel Avenue"));
            Assert.Equal(BuyerStatus.MissingDetails, a.Status);
            Assert.Equal(new[] { BuyerCompliance.MissingPhone, BuyerCompliance.MissingHouseNo }, a.Missing);
            var ex = Assert.Throws<BuyerDetailsException>(() => BuyerCompliance.BuildBuyerData(a, FiscalizationValidationMode.Strict));
            Assert.Contains("phone number, house/building number", ex.Message);
            Assert.Contains("Lenient", ex.Message);
        }

        [Fact]
        public void LenientFillsOnlyWhatIsMissing()
        {
            var a = BuyerCompliance.Assess(Identity(), Customer(phone: null, mobile: null, email: null, line1: "Samora Machel Avenue", city: "Bulawayo", province: null));
            var data = BuyerCompliance.BuildBuyerData(a, FiscalizationValidationMode.Lenient)!;
            Assert.Equal(BuyerCompliance.LenientPhone, data.Contacts!.PhoneNumber);
            Assert.Equal(BuyerCompliance.LenientEmail, data.Contacts.Email);
            Assert.Equal(BuyerCompliance.LenientHouseNo, data.Address!.HouseNo);
            Assert.Equal("Samora Machel Avenue", data.Address.Street); // real value kept
            Assert.Equal("Bulawayo", data.Address.City);
            Assert.Equal("Bulawayo", data.Address.Province); // from the city, not the placeholder
        }

        [Theory]
        [InlineData("12345", null)]
        [InlineData("ABC1234567", null)]
        [InlineData("0000000000", null)]
        [InlineData("2000123456", "12345")]
        public void InvalidTinOrVatBlockedInBothModes(string tin, string? vat)
        {
            var a = BuyerCompliance.Assess(Identity(tin, vat), Customer());
            Assert.Equal(BuyerStatus.Invalid, a.Status);
            Assert.Throws<BuyerDetailsException>(() => BuyerCompliance.BuildBuyerData(a, FiscalizationValidationMode.Strict));
            Assert.Throws<BuyerDetailsException>(() => BuyerCompliance.BuildBuyerData(a, FiscalizationValidationMode.Lenient));
        }

        [Fact]
        public void VatWithoutTinIsInvalid()
        {
            var a = BuyerCompliance.Assess(Identity(tin: null, vat: "100200300"), Customer());
            Assert.Equal(BuyerStatus.Invalid, a.Status);
            Assert.Contains(a.Problems, p => p.Contains("no TIN"));
        }

        [Fact]
        public void SeparatorsInTinAndVatAreIgnored()
        {
            var a = BuyerCompliance.Assess(Identity("200-012 3456", "100 200 300"), Customer());
            Assert.Equal(BuyerStatus.Compliant, a.Status);
            Assert.Equal("2000123456", a.Tin);
            Assert.Equal("100200300", a.VatNumber);
        }

        [Fact]
        public void ZeroVatPlaceholderMeansNoVat()
        {
            var a = BuyerCompliance.Assess(Identity(vat: "000000000"), Customer());
            Assert.Equal(BuyerStatus.Compliant, a.Status);
            Assert.Null(a.VatNumber);
        }

        [Fact]
        public void DeliveryAddressUsedWhenBillingIsEmpty()
        {
            var c = Customer(line1: null, city: null, province: null);
            c.Addresses!.Single(x => x.AddressType == "STREET").AddressLine1 = "9 Kwame Nkrumah Avenue";
            c.Addresses!.Single(x => x.AddressType == "STREET").City = "Harare";
            var a = BuyerCompliance.Assess(Identity(), c);
            Assert.Equal(BuyerStatus.Compliant, a.Status);
            Assert.Equal("9", a.Address.HouseNo);
        }

        [Fact]
        public void BillingAddressWinsWhenItHasContent()
        {
            var c = Customer();
            c.Addresses!.Single(x => x.AddressType == "STREET").AddressLine1 = "9 Kwame Nkrumah Avenue";
            c.Addresses!.Single(x => x.AddressType == "STREET").City = "Bulawayo";
            var a = BuyerCompliance.Assess(Identity(), c);
            Assert.Equal("12", a.Address.HouseNo);
            Assert.Equal("Harare", a.Address.City);
        }

        [Fact]
        public void DirectDialIsTheLastPhoneTried_AndFaxNever()
        {
            var c = Customer(phone: null, mobile: null);
            Assert.Null(BuyerCompliance.Assess(Identity(), c).Phone); // the fax number is not a phone to reach the buyer on

            c.Phones!.Single(x => x.PhoneType == "DDI").PhoneNumber = "0242 555 111";
            var a = BuyerCompliance.Assess(Identity(), c);
            Assert.Equal("0242555111", a.Phone);
            Assert.Equal("Direct dial", a.PhoneSource);
        }

        [Fact]
        public void PhoneIsCountryAreaAndNumberRunTogether()
        {
            var c = Customer(phone: null, mobile: null);
            var mobile = c.Phones!.Single(x => x.PhoneType == "MOBILE");
            (mobile.PhoneCountryCode, mobile.PhoneAreaCode, mobile.PhoneNumber) = ("+263", "77", "123 4567");
            Assert.Equal("+263771234567", BuyerCompliance.Assess(Identity(), c).Phone);
        }

        [Fact]
        public void EveryMessageNamesXero()
        {
            var missing = BuyerCompliance.Assess(Identity(), Customer(phone: null, mobile: null));
            var strict = Assert.Throws<BuyerDetailsException>(() => BuyerCompliance.BuildBuyerData(missing, FiscalizationValidationMode.Strict));
            Assert.Contains("Xero", strict.Message);
            var invalid = BuyerCompliance.Assess(Identity("12345"), Customer());
            Assert.Contains("Xero", Assert.Throws<BuyerDetailsException>(() => BuyerCompliance.BuildBuyerData(invalid, FiscalizationValidationMode.Strict)).Message);
        }

        [Fact]
        public void TradeNameCappedAt50()
        {
            var id = Identity();
            id.TradeName = new string('A', 80);
            var data = BuyerCompliance.BuildBuyerData(BuyerCompliance.Assess(id, Customer()), FiscalizationValidationMode.Strict)!;
            Assert.Equal(50, data.TradeName!.Length);
        }
    }

    public class FiscalFailureClassificationTests
    {
        [Fact]
        public void BuyerProblemsKeepTheirItems()
        {
            var (category, _, details) = FiscalFailureService.Classify(new BuyerDetailsException("x", new[] { "Missing phone number" }));
            Assert.Equal(FiscalFailureCategory.Buyer, category);
            Assert.Equal(new[] { "Missing phone number" }, details);
        }

        [Theory]
        [InlineData("Item \"Widget\" has no HS code and there is no default HS code.", FiscalFailureCategory.HsCode)]
        [InlineData("No ZIMRA device configured for this company. Set one up under Device Setup.", FiscalFailureCategory.Device)]
        [InlineData("Invoice 1001 has not been fiscalized here.", FiscalFailureCategory.OriginalSale)]
        [InlineData("Credit note CN-7 has not been allocated to an invoice in Xero, so the original invoice is unknown.", FiscalFailureCategory.OriginalSale)]
        [InlineData("Xero is not connected for this company. Connect Xero first.", FiscalFailureCategory.Xero)]
        [InlineData("The Xero connection is not authorised any more. Reconnect Xero for this company, then try again.", FiscalFailureCategory.Xero)]
        [InlineData("Something unexpected", FiscalFailureCategory.Other)]
        public void ClassifiesByMessage(string message, string expected)
            => Assert.Equal(expected, FiscalFailureService.Classify(new FiscalizeXero.Api.Exceptions.UserFacingException(message)).Category);

        [Fact]
        public void XeroErrorsCarryTheCorrelationId()
        {
            var ex = new FiscalizeXero.Xero.Exceptions.XeroApiException(System.Net.HttpStatusCode.TooManyRequests, "", "corr-123", "day");
            var (category, message, _) = FiscalFailureService.Classify(ex);
            Assert.Equal(FiscalFailureCategory.Xero, category);
            Assert.Contains("daily limit", message);
            Assert.Contains("corr-123", message);
        }

        [Fact]
        public void TimeoutsAreNetwork()
            => Assert.Equal(FiscalFailureCategory.Network, FiscalFailureService.Classify(new TaskCanceledException("timeout")).Category);
    }
}
