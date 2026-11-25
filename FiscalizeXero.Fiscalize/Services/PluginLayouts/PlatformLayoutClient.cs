using System.Net;
using System.Text.Json;
using FiscalizeXero.Fiscalize.Models.PluginLayouts;
using FiscalizeXero.Shared.Models.Auths;

namespace FiscalizeXero.Fiscalize.Services.PluginLayouts
{
    /// <summary>The platform's layout endpoints for a device key (the same two the desktop plugins call).</summary>
    public static class PlatformLayoutClient
    {
        public const string CurrentEndpoint = "api/plugin-layouts/devices/current";
        public const string FieldsEndpoint = "api/plugin-layouts/devices/fields";

        private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

        /// <summary>
        /// Asks for the layout for one plugin, document ("Invoice"/"CreditNote") and paper. Pass the hash already held so a
        /// current copy costs nothing. Never throws for an HTTP answer; a network failure does throw, and the caller keeps its copy.
        /// </summary>
        public static async Task<PluginLayoutFetch> GetCurrentAsync(FiscalizeAuthModel auth, string pluginType, string documentType, string paperSize, string? heldHash)
        {
            var endpoint = $"{CurrentEndpoint}?pluginType={Uri.EscapeDataString(pluginType)}&documentType={Uri.EscapeDataString(documentType)}&paperSize={Uri.EscapeDataString(paperSize)}";
            var (status, body) = await RestClient.GetRawAsync(auth, endpoint, heldHash);
            switch (status)
            {
                case HttpStatusCode.NotModified:
                    return new PluginLayoutFetch(PluginLayoutFetchOutcome.NotModified, null);
                case HttpStatusCode.NotFound:
                    return new PluginLayoutFetch(PluginLayoutFetchOutcome.None, null);
                case HttpStatusCode.OK:
                    try
                    {
                        var current = JsonSerializer.Deserialize<PluginLayoutCurrentModel>(body, Json);
                        return current == null || string.IsNullOrWhiteSpace(current.Package)
                            ? new PluginLayoutFetch(PluginLayoutFetchOutcome.Failed, null, "the platform sent an empty layout")
                            : new PluginLayoutFetch(PluginLayoutFetchOutcome.Updated, current);
                    }
                    catch (JsonException ex)
                    {
                        return new PluginLayoutFetch(PluginLayoutFetchOutcome.Failed, null, $"the platform's answer could not be read ({ex.Message})");
                    }
                case HttpStatusCode.Unauthorized:
                    throw new Exceptions.FiscalizeApiException(status, body);
                default:
                    return new PluginLayoutFetch(PluginLayoutFetchOutcome.Failed, null, $"the platform answered {(int)status}");
            }
        }

        /// <summary>Tells the platform which fields this app can fill, so the designer can offer them. Names only.</summary>
        public static Task<JsonElement> UploadFieldsAsync(FiscalizeAuthModel auth, UploadPluginFieldsModel model) =>
            RestClient.PutAsync<UploadPluginFieldsModel, JsonElement>(auth, FieldsEndpoint, model);
    }
}
