using FiscalizeXero.Xero.Models;

namespace FiscalizeXero.Api.Services.Fiscalization
{
    /// <summary>
    /// A Xero contact has two boxes that can hold a tax identifier, Tax Number and Company Number, and neither is
    /// named for ZIMRA. So the value decides, not the box: exactly 10 digits is a TIN, exactly 9 digits is a VAT
    /// number, whichever box it is in. Anything else (a company registration number, a note) is neither.
    /// </summary>
    public static class XeroTaxIdentifiers
    {
        public const int TinLength = 10;
        public const int VatLength = 9;

        public sealed record Result(string? Tin, string? VatNumber);

        /// <summary>The TIN and VAT number found in the contact's two boxes. Tax Number is read first, so it wins when both boxes hold the same kind.</summary>
        public static Result Classify(Contact? contact) => Classify(contact?.TaxNumber, contact?.CompanyNumber);

        public static Result Classify(string? taxNumber, string? companyNumber)
        {
            string? tin = null, vat = null;
            foreach (var value in new[] { taxNumber, companyNumber })
            {
                if (IsTin(value)) tin ??= value!.Trim();
                else if (IsVatNumber(value)) vat ??= value!.Trim();
            }
            return new Result(tin, vat);
        }

        public static bool IsTin(string? value) => BuyerCompliance.NormaliseDigits(value, TinLength) != null;

        public static bool IsVatNumber(string? value) => BuyerCompliance.NormaliseDigits(value, VatLength) != null;

        /// <summary>True when a box is empty or holds a TIN or VAT number: something Fiscalize may replace. Anything else belongs to the user.</summary>
        public static bool IsOursToWrite(string? value) => string.IsNullOrWhiteSpace(value) || IsTin(value) || IsVatNumber(value);
    }
}
