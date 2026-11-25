using System.Security.Claims;
using System.Text.Json;
using FiscalizeXero.Api.Data;
using FiscalizeXero.Shared.Models;
using FiscalizeXero.Shared.Models.Auths;

namespace FiscalizeXero.Api.Security
{
    /// <summary>
    /// Who may change a company's Xero connection and fiscalisation set-up: the person who connected Xero (so a staff
    /// member running the plugin alone is never locked out of their own connection). Anyone else in the company takes
    /// over by reconnecting with their own Xero login, so nobody is stuck when that person leaves.
    /// </summary>
    public static class ConnectionManagers
    {
        /// <summary>True for the person who connected, and for everyone on a connection made before this rule.</summary>
        public static bool CanManage(ClientConfig? config, long? userId) =>
            config?.ConnectedByUserId == null || (userId.HasValue && config.ConnectedByUserId == userId.Value);

        public static ErrorModel Refusal(ClientConfig config) => new()
        {
            Code = 403,
            Message = $"Only {(string.IsNullOrWhiteSpace(config.ConnectedByName) ? "the person" : config.ConnectedByName)}, who connected Xero for this company, can change this. " +
                      "To manage it yourself, reconnect Xero with your own Xero login under Xero Connection."
        };

        /// <summary>The signed-in person's name from the session claim, for "connected by".</summary>
        public static string? UserName(ClaimsPrincipal? user)
        {
            var raw = user?.FindFirst("Session")?.Value;
            if (string.IsNullOrEmpty(raw)) return null;
            try
            {
                var name = JsonSerializer.Deserialize<SessionModel>(raw, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })?.UserName?.Trim();
                return string.IsNullOrEmpty(name) ? null : (name.Length > 200 ? name[..200] : name);
            }
            catch (JsonException) { return null; }
        }
    }
}
