using FiscalizeXero.Api.Data;
using FiscalizeXero.Xero.Models;

namespace FiscalizeXero.Api.Services.Fiscalization
{
    /// <summary>
    /// Copies a TIN and VAT number typed in Fiscalize onto the Xero contact, so Xero itself holds them: the TIN goes
    /// to Tax Number and the VAT number to Company Number. Xero has no other home for ZIMRA buyer details (and no
    /// custom fields), so names, phone, email and address typed in Fiscalize stay in Fiscalize.
    /// <para>
    /// A box is only written when it is empty or already holds a TIN or VAT number. A box holding anything else
    /// (a company registration number, say) belongs to the user and is left alone; the value then stays in Fiscalize.
    /// </para>
    /// </summary>
    public static class XeroContactWriteback
    {
        /// <param name="TaxNumber">New Tax Number; null leaves the box alone, "" clears it.</param>
        /// <param name="CompanyNumber">New Company Number; null leaves the box alone, "" clears it.</param>
        public sealed record Update(string? TaxNumber, string? CompanyNumber)
        {
            public bool IsEmpty => TaxNumber == null && CompanyNumber == null;
        }

        /// <summary>The change to send to Xero for what was typed, or null when there is nothing to write.</summary>
        public static Update? Build(Contact contact, ContactVatRecord typed)
        {
            if (typed.Tin == null && typed.VatNumber == null) return null;

            var current = XeroTaxIdentifiers.Classify(contact);
            // What the contact should hold afterwards: typed values win ("" = cleared), the rest stay as Xero has them.
            // Both boxes are rewritten together so that a TIN found in Company Number moves to Tax Number instead of
            // being overwritten and lost.
            var tin = typed.Tin != null ? Digits(typed.Tin) : current.Tin ?? string.Empty;
            var vat = typed.VatNumber != null ? Digits(typed.VatNumber) : current.VatNumber ?? string.Empty;

            var taxNumber = XeroTaxIdentifiers.IsOursToWrite(contact.TaxNumber) && !Same(contact.TaxNumber, tin) ? tin : null;
            var companyNumber = XeroTaxIdentifiers.IsOursToWrite(contact.CompanyNumber) && !Same(contact.CompanyNumber, vat) ? vat : null;

            var update = new Update(taxNumber, companyNumber);
            return update.IsEmpty ? null : update;
        }

        /// <summary>
        /// After Xero accepted the update: drops the typed TIN and VAT number when Xero now gives them back exactly,
        /// so Xero is the one place they live and later edits there flow through. Everything else typed in Fiscalize
        /// is kept as it is.
        /// </summary>
        public static ContactVatRecord DropHeldByXero(ContactVatRecord typed, Contact updated)
        {
            var held = XeroTaxIdentifiers.Classify(updated);
            var result = new ContactVatRecord();
            result.CopyBuyerValuesFrom(typed);
            result.Tin = Drop(typed.Tin, held.Tin);
            result.VatNumber = Drop(typed.VatNumber, held.VatNumber);
            return result;
        }

        /// <summary>The boxes of the contact that hold something Fiscalize will not overwrite, by their Xero names.</summary>
        public static List<string> BoxesLeftAlone(Contact contact, ContactVatRecord typed)
        {
            var boxes = new List<string>();
            if (typed.Tin != null && !XeroTaxIdentifiers.IsOursToWrite(contact.TaxNumber)) boxes.Add("Tax Number");
            if (typed.VatNumber != null && !XeroTaxIdentifiers.IsOursToWrite(contact.CompanyNumber)) boxes.Add("Company Number");
            return boxes;
        }

        // A cleared value ("") is dropped once Xero is empty too; if Xero kept a value, "" stays so it isn't used.
        private static string? Drop(string? typed, string? held)
        {
            if (typed == null) return null;
            if (typed.Trim().Length == 0) return held == null ? null : "";
            return Same(typed, held) ? null : typed;
        }

        private static bool Same(string? a, string? b) => Digits(a) == Digits(b);

        private static string Digits(string? v) => new((v ?? string.Empty).Where(char.IsAsciiDigit).ToArray());
    }
}
