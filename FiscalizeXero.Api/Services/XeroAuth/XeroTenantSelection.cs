using FiscalizeXero.Xero.Models;
using FiscalizeXero.Xero.OAuth;

namespace FiscalizeXero.Api.Services.XeroAuth
{
    /// <summary>
    /// Xero's redirect carries only <c>code</c> and <c>state</c>, not the organisation the user chose. After the code
    /// exchange the app lists the user's connections; this picks the one the consent was for. Pure, so it is unit
    /// tested without a network.
    /// </summary>
    public static class XeroTenantSelection
    {
        /// <summary>
        /// The organisation to connect: the one stamped with the access token's authentication event (or, when Xero
        /// gave no event id, the most recently updated one). When the consent covered several organisations, the one
        /// this company was already connected to wins, so a re-authorisation never silently switches organisation;
        /// otherwise the most recently updated. Null when the user has no organisation connected.
        /// </summary>
        public static XeroConnection? Pick(IEnumerable<XeroConnection> connections, string? accessToken, Guid? currentTenantId = null)
        {
            var fromConsent = XeroTokenClaims.ConnectionsFromConsent(connections, accessToken);
            if (fromConsent.Count == 0) return null;
            if (fromConsent.Count == 1) return fromConsent[0];

            if (currentTenantId.HasValue && fromConsent.FirstOrDefault(c => c.TenantId == currentTenantId.Value) is { } current)
                return current;
            return fromConsent.OrderByDescending(c => c.UpdatedDateUtc ?? c.CreatedDateUtc ?? DateTime.MinValue).First();
        }

        /// <summary>
        /// Whether deleting <paramref name="connection"/> at Xero is safe when its organisation turned out to belong
        /// to another Fiscalize company. Xero keeps one connection per app and organisation, so the connection this
        /// consent returned can be the very one the other company is using: deleting it would cut that company off.
        /// It is only deleted when it is provably not theirs.
        /// </summary>
        public static bool SafeToDelete(XeroConnection connection, IReadOnlyCollection<Guid?> connectionIdsHeldElsewhere) =>
            connectionIdsHeldElsewhere.Count > 0
            && connectionIdsHeldElsewhere.All(id => id.HasValue)
            && !connectionIdsHeldElsewhere.Contains(connection.Id);
    }
}
