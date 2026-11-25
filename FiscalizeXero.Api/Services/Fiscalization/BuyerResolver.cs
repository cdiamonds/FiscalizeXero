using FiscalizeXero.Api.Data;
using FiscalizeXero.Xero.Models;

namespace FiscalizeXero.Api.Services.Fiscalization
{
    /// <summary>
    /// Decides the buyer details sent to Fiscalize. Each value is what the user saved in Fiscalize
    /// (<see cref="ContactVatRecord"/>) and otherwise what the Xero contact holds:
    /// <list type="bullet">
    /// <item>Registered name and trade name: the contact's Name.</item>
    /// <item>TIN and VAT number: the contact's Tax Number and Company Number, told apart by format
    /// (<see cref="XeroTaxIdentifiers"/>).</item>
    /// </list>
    /// Saved phone, email and address parts are carried through; <see cref="BuyerCompliance"/> prefers them over
    /// the Xero contact.
    /// </summary>
    public static class BuyerResolver
    {
        public static ContactVatRecord Resolve(Contact? contact, ContactVatRecord? saved)
        {
            var ids = XeroTaxIdentifiers.Classify(contact);
            return new ContactVatRecord
            {
                ContactId = contact != null ? contact.ContactID.ToString("D") : saved?.ContactId ?? string.Empty,
                RegisteredName = Pick(saved?.RegisteredName, contact?.Name),
                TradeName = Pick(saved?.TradeName, contact?.Name),
                VatNumber = Pick(saved?.VatNumber, ids.VatNumber),
                Tin = Pick(saved?.Tin, ids.Tin),
                // Contact details pass through as saved, "" included, so BuyerCompliance knows a value was cleared.
                Phone = saved?.Phone?.Trim(),
                Email = saved?.Email?.Trim(),
                HouseNo = saved?.HouseNo?.Trim(),
                Street = saved?.Street?.Trim(),
                City = saved?.City?.Trim(),
                Province = saved?.Province?.Trim()
            };
        }

        /// <summary>The saved value when there is one ("" = cleared, so nothing), otherwise Xero's.</summary>
        private static string? Pick(string? saved, string? xero)
        {
            if (saved == null) return string.IsNullOrWhiteSpace(xero) ? null : xero.Trim();
            var t = saved.Trim();
            return t.Length == 0 ? null : t;
        }

        public static bool HasOverride(ContactVatRecord? saved)
            => saved != null && (
                saved.RegisteredName != null ||
                saved.TradeName != null ||
                saved.VatNumber != null ||
                saved.Tin != null ||
                saved.Phone != null ||
                saved.Email != null ||
                saved.HouseNo != null ||
                saved.Street != null ||
                saved.City != null ||
                saved.Province != null);
    }
}
