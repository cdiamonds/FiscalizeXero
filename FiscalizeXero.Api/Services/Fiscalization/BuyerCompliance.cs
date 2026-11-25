using FiscalizeXero.Api.Data;
using FiscalizeXero.Xero.Models;
using FiscalizeXero.Shared.Models.Fiscalize;

namespace FiscalizeXero.Api.Services.Fiscalization
{
    /// <summary>Where a customer stands for ZIMRA. The names are what the API and the Customers page use.</summary>
    public static class BuyerStatus
    {
        /// <summary>No TIN (and no VAT number): fiscalised as a cash (B2C) sale, no buyer details needed.</summary>
        public const string NoTin = "NoTin";
        /// <summary>TIN plus every buyer detail ZIMRA asks for.</summary>
        public const string Compliant = "Compliant";
        /// <summary>A valid TIN but some buyer details are missing: Strict blocks, Lenient fills placeholders.</summary>
        public const string MissingDetails = "MissingDetails";
        /// <summary>A TIN or VAT number that ZIMRA will reject, or a VAT number without a TIN. Blocked in both modes.</summary>
        public const string Invalid = "Invalid";
    }

    /// <summary>
    /// A buyer-details problem that stops fiscalisation. It is an <see cref="InvalidOperationException"/> so every
    /// existing caller reports it as a 400 with its message, and it carries the individual items for the UI.
    /// </summary>
    public sealed class BuyerDetailsException : FiscalizeXero.Api.Exceptions.UserFacingException
    {
        public BuyerDetailsException(string message, IReadOnlyList<string> details) : base(message) { Details = details; }
        public IReadOnlyList<string> Details { get; }
    }

    /// <summary>Everything known about one buyer, and whether it is enough for ZIMRA.</summary>
    public sealed class BuyerAssessment
    {
        public string CustomerName { get; init; } = string.Empty;
        public string? RegisteredName { get; init; }
        public string? TradeName { get; init; }
        /// <summary>The TIN as sent (separators removed). Null when there is none or it is invalid.</summary>
        public string? Tin { get; init; }
        /// <summary>The VAT number as sent (separators removed). Null when there is none or it is invalid.</summary>
        public string? VatNumber { get; init; }
        public string? RawTin { get; init; }
        public string? RawVatNumber { get; init; }
        public string? Phone { get; init; }
        /// <summary>Where the phone came from: "Saved in Fiscalize", or the Xero phone "Phone", "Mobile" or "Direct dial".</summary>
        public string? PhoneSource { get; init; }
        public string? Email { get; init; }
        public BuyerAddress Address { get; init; } = new(null, null, null, null);

        /// <summary>Buyer details ZIMRA needs with a TIN that could not be found, as readable labels.</summary>
        public IReadOnlyList<string> Missing { get; init; } = Array.Empty<string>();
        /// <summary>Values that are present but wrong (bad TIN or VAT format, VAT without TIN).</summary>
        public IReadOnlyList<string> Problems { get; init; } = Array.Empty<string>();

        public string Status => Problems.Count > 0 ? BuyerStatus.Invalid
            : Tin == null ? BuyerStatus.NoTin
            : Missing.Count > 0 ? BuyerStatus.MissingDetails
            : BuyerStatus.Compliant;
    }

    /// <summary>
    /// The single place that decides what buyer details go to ZIMRA. The fiscaliser and the Customers page both use
    /// it, so the compliance badge a user sees is exactly what fiscalising will do.
    /// </summary>
    public static class BuyerCompliance
    {
        public const string MissingPhone = "phone number";
        public const string MissingEmail = "email";
        public const string MissingHouseNo = "house/building number";
        public const string MissingStreet = "street";
        public const string MissingCity = "city";
        public const string MissingProvince = "province";
        public const string MissingRegisteredName = "registered name";

        // Lenient-mode placeholders: the same values the Zoho Books integration uses.
        public const string LenientEmail = "customer@email.com";
        public const string LenientPhone = "0770000000";
        public const string LenientHouseNo = "01";
        public const string LenientArea = "Harare"; // street, city and province

        /// <summary>ZIMRA rejects a trade name longer than 50 characters.</summary>
        public const int MaxTradeNameLength = 50;

        /// <summary><see cref="BuyerAssessment.PhoneSource"/> when the phone was typed in Fiscalize.</summary>
        public const string SavedSource = "Saved in Fiscalize";

        /// <param name="identity">Names, TIN, VAT and any saved contact details from <see cref="BuyerResolver"/> (saved values win over Xero).</param>
        /// <param name="contact">The Xero contact, for phone, email and address not saved in Fiscalize. Null when it no longer exists.</param>
        /// <param name="fallbackName">Name to use in messages when the customer has none (the document's customer name).</param>
        public static BuyerAssessment Assess(ContactVatRecord identity, Contact? contact, string? fallbackName = null)
        {
            var name = First(contact?.Name, identity.TradeName, identity.RegisteredName, fallbackName) ?? "this customer";

            var rawTin = identity.Tin?.Trim();
            var rawVat = identity.VatNumber?.Trim();
            var tin = NormaliseDigits(rawTin, 10);
            var vat = NormaliseDigits(rawVat, 9);
            var hasTin = !string.IsNullOrEmpty(rawTin);
            // "000000000" is the conventional "no VAT number" placeholder, not a VAT number.
            var hasVat = !string.IsNullOrEmpty(rawVat) && vat != "000000000";
            if (vat == "000000000") vat = null;

            var problems = new List<string>();
            if (hasTin && tin == null)
                problems.Add($"TIN \"{rawTin}\" is not valid: a ZIMRA TIN is exactly 10 digits.");
            else if (tin == "0000000000")
            {
                problems.Add("TIN 0000000000 is not a real TIN.");
                tin = null;
            }
            if (hasVat && vat == null)
                problems.Add($"VAT number \"{rawVat}\" is not valid: a ZIMRA VAT number is exactly 9 digits.");
            if (!hasTin && hasVat)
                problems.Add("Has a VAT number but no TIN. ZIMRA needs the TIN for every VAT-registered buyer.");

            // Anything typed in Fiscalize wins and a value cleared there ("") stays empty; Xero fills the rest.
            var savedPhone = BuyerContactResolver.FirstPhone(identity.Phone);
            var (phone, phoneSource) = Cleared(identity.Phone) ? ((string?)null, (string?)null)
                : savedPhone != null ? (savedPhone, SavedSource) : ResolvePhone(contact);
            var email = Cleared(identity.Email) ? null : BuyerContactResolver.FirstEmail(identity.Email, contact?.EmailAddress);
            var xeroAddress = ResolveAddress(contact);
            var address = new BuyerAddress(
                Over(identity.HouseNo, xeroAddress.HouseNo),
                Over(identity.Street, xeroAddress.Street),
                Over(identity.City, xeroAddress.City),
                Over(identity.Province, xeroAddress.Province));

            var missing = new List<string>();
            if (tin != null)
            {
                if (string.IsNullOrWhiteSpace(identity.RegisteredName)) missing.Add(MissingRegisteredName);
                if (phone == null) missing.Add(MissingPhone);
                if (email == null) missing.Add(MissingEmail);
                if (address.HouseNo == null) missing.Add(MissingHouseNo);
                if (address.Street == null) missing.Add(MissingStreet);
                if (address.City == null) missing.Add(MissingCity);
                if (address.Province == null) missing.Add(MissingProvince);
            }

            return new BuyerAssessment
            {
                CustomerName = name,
                RegisteredName = identity.RegisteredName?.Trim(),
                TradeName = identity.TradeName?.Trim(),
                Tin = tin,
                VatNumber = vat,
                RawTin = rawTin,
                RawVatNumber = rawVat,
                Phone = phone,
                PhoneSource = phoneSource,
                Email = email,
                Address = address,
                Missing = missing,
                Problems = problems
            };
        }

        /// <summary>
        /// The buyer block for the platform request: null for a cash sale; the full block for a TIN buyer.
        /// Throws <see cref="BuyerDetailsException"/> when the TIN/VAT is invalid, or details are missing in Strict mode.
        /// </summary>
        public static FiscalizeInvoiceBuyerData? BuildBuyerData(BuyerAssessment a, FiscalizationValidationMode mode)
        {
            if (a.Status == BuyerStatus.Invalid)
                throw new BuyerDetailsException(
                    $"Customer \"{a.CustomerName}\" can't be fiscalised: {string.Join(" ", a.Problems)} Correct it on the Customers page in Fiscalize or on the contact in Xero, then try again.",
                    a.Problems);

            if (a.Tin == null) return null; // cash sale

            if (a.Missing.Count > 0 && mode == FiscalizationValidationMode.Strict)
                throw new BuyerDetailsException(
                    $"Customer \"{a.CustomerName}\" has a TIN, so ZIMRA needs their full buyer details, but these are missing: {string.Join(", ", a.Missing)}. "
                    + "Add them on the Customers page in Fiscalize (or on the contact in Xero), or switch buyer validation to Lenient (Settings → Fiscalization) to fill placeholders.",
                    a.Missing.Select(m => $"Missing {m}").ToList());

            var registeredName = First(a.RegisteredName, a.CustomerName)!;
            return new FiscalizeInvoiceBuyerData
            {
                RegisteredName = registeredName,
                TradeName = CapTradeName(First(a.TradeName, registeredName)),
                VatNumber = a.VatNumber,
                Tin = a.Tin,
                Contacts = new FiscalizeInvoiceBuyerContacts
                {
                    PhoneNumber = a.Phone ?? LenientPhone,
                    Email = a.Email ?? LenientEmail
                },
                Address = new FiscalizeInvoiceBuyerAddress
                {
                    HouseNo = a.Address.HouseNo ?? LenientHouseNo,
                    Street = a.Address.Street ?? LenientArea,
                    City = a.Address.City ?? LenientArea,
                    Province = a.Address.Province ?? LenientArea
                }
            };
        }

        private static bool Cleared(string? saved) => saved != null && saved.Trim().Length == 0;

        /// <summary>A value saved in Fiscalize ("" = cleared) over Xero's.</summary>
        private static string? Over(string? saved, string? xero) => saved == null ? First(xero) : Cleared(saved) ? null : saved.Trim();

        /// <summary>Phone (Xero's DEFAULT), then Mobile, then Direct dial (DDI): the first one that holds a usable number.</summary>
        public static (string? Phone, string? Source) ResolvePhone(Contact? contact)
        {
            if (contact == null) return (null, null);
            var candidates = new (string Type, string Source)[]
            {
                ("DEFAULT", "Phone"),
                ("MOBILE", "Mobile"),
                ("DDI", "Direct dial"),
            };
            foreach (var (type, source) in candidates)
            {
                var phone = BuyerContactResolver.FirstPhone(contact.Phone(type)?.Full);
                if (phone != null) return (phone, source);
            }
            return (null, null);
        }

        /// <summary>The Xero address the buyer's address is read from: the billing one (POBOX) when it has content, else the delivery one (STREET).</summary>
        public static ContactAddress? SourceAddress(Contact? contact) => contact?.Address("POBOX") ?? contact?.Address("STREET");

        /// <summary>The buyer's address parts, parsed from <see cref="SourceAddress"/>.</summary>
        public static BuyerAddress ResolveAddress(Contact? contact)
        {
            var a = SourceAddress(contact);
            return a == null
                ? new BuyerAddress(null, null, null, null)
                : BuyerAddressParser.Parse(
                    new RawAddress(new[] { a.AddressLine1, a.AddressLine2, a.AddressLine3, a.AddressLine4 }, a.City, a.Region, a.PostalCode, a.Country),
                    contact!.Name);
        }

        /// <summary>Digits only once spaces, dashes, dots and slashes are removed, if exactly <paramref name="length"/> long.</summary>
        public static string? NormaliseDigits(string? raw, int length)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            var compact = new string(raw.Where(c => !char.IsWhiteSpace(c) && c is not ('-' or '.' or '/')).ToArray());
            return compact.Length == length && compact.All(char.IsAsciiDigit) ? compact : null;
        }

        public static string? CapTradeName(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var trimmed = value.Trim();
            return trimmed.Length <= MaxTradeNameLength ? trimmed : trimmed[..MaxTradeNameLength].TrimEnd();
        }

        private static string? First(params string?[] values)
            => values.Select(v => v?.Trim()).FirstOrDefault(v => !string.IsNullOrEmpty(v));
    }
}
