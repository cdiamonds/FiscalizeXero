using FiscalizeXero.Api.Data;
using FiscalizeXero.Xero.Models;
using FiscalizeXero.Xero.Services;
using XeroAuthModel = FiscalizeXero.Xero.XeroAuth;

namespace FiscalizeXero.Api.Services.Fiscalization
{
    /// <summary>
    /// Sends what <see cref="XeroContactWriteback"/> worked out to Xero. Best effort: whatever Xero does not take
    /// stays saved in Fiscalize, and fiscalising never depends on it.
    /// </summary>
    public static class XeroContactUpdater
    {
        /// <param name="Values">What still has to be kept in Fiscalize afterwards.</param>
        /// <param name="Contact">The contact as Xero has it now.</param>
        /// <param name="Updated">True when Xero was changed, false when it was not.</param>
        /// <param name="Warning">Why Xero does not hold everything that was typed, when it does not.</param>
        public sealed record Outcome(ContactVatRecord Values, Contact Contact, bool Updated, string? Warning);

        public static async Task<Outcome> ApplyAsync(XeroAuthModel auth, Contact contact, ContactVatRecord typed, ILogger logger)
        {
            var leftAlone = XeroContactWriteback.BoxesLeftAlone(contact, typed);
            var boxWarning = leftAlone.Count == 0 ? null
                : $"The {string.Join(" and ", leftAlone)} of this contact in Xero already holds something that is not a TIN or VAT number, so it was left as it is. " +
                  "The value you typed is kept in Fiscalize and is still sent to ZIMRA.";

            var update = XeroContactWriteback.Build(contact, typed);
            if (update == null)
                return new Outcome(XeroContactWriteback.DropHeldByXero(typed, contact), contact, false, boxWarning);

            try
            {
                var after = await ContactService.UpdateTaxDetailsAsync(auth, contact.ContactID, update.TaxNumber, update.CompanyNumber) ?? contact;
                var kept = XeroContactWriteback.DropHeldByXero(typed, after);
                // Xero may accept an update and still not keep a value; say so rather than let the user think it is there.
                var notKept = boxWarning == null && ((!string.IsNullOrEmpty(typed.Tin) && kept.Tin != null) || (!string.IsNullOrEmpty(typed.VatNumber) && kept.VatNumber != null));
                var warning = boxWarning ?? (notKept
                    ? "Xero accepted the update but did not keep the TIN or VAT number on the contact. It is kept in Fiscalize and is still sent to ZIMRA."
                    : null);
                return new Outcome(kept, after, true, warning);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Could not copy the TIN/VAT number to Xero contact {ContactId}; kept in Fiscalize", contact.ContactID);
                return new Outcome(typed, contact, false, $"Saved in Fiscalize, but Xero didn't accept the update to the contact: {ex.Message}");
            }
        }
    }
}
