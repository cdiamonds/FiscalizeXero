using FiscalizeXero.Xero.Exceptions;
using FiscalizeXero.Xero.Models;
using FiscalizeXero.Xero.Services;

namespace FiscalizeXero.Api.Services.XeroAuth
{
    /// <summary>
    /// Xero's connection list for a signed-in Xero user. Behind an interface so the OAuth callback's decisions
    /// (which organisation, whose connection) can be tested without a network.
    /// </summary>
    public interface IXeroConnections
    {
        /// <summary>Every organisation the token's user has connected to this app.</summary>
        Task<List<XeroConnection>> ListAsync(string accessToken);

        /// <summary>Removes one organisation's connection. Best effort: false (never throws) when Xero refused or could not be reached.</summary>
        Task<bool> TryDisconnectAsync(string accessToken, Guid connectionId);
    }

    public class XeroConnections : IXeroConnections
    {
        private readonly ILogger<XeroConnections> _logger;
        public XeroConnections(ILogger<XeroConnections> logger) => _logger = logger;

        public Task<List<XeroConnection>> ListAsync(string accessToken) => ConnectionService.ListAsync(accessToken);

        public async Task<bool> TryDisconnectAsync(string accessToken, Guid connectionId)
        {
            try
            {
                await ConnectionService.DisconnectAsync(accessToken, connectionId);
                return true;
            }
            catch (Exception ex) when (ex is XeroApiException or HttpRequestException or TaskCanceledException)
            {
                _logger.LogWarning(ex, "Could not remove Xero connection {ConnectionId}", connectionId);
                return false;
            }
        }
    }
}
