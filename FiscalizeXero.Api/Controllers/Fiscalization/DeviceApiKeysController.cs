using FiscalizeXero.Api.Filters;
using FiscalizeXero.Api.Services.FiscalizeSession;
using FiscalizeXero.Fiscalize.Exceptions;
using FiscalizeXero.Fiscalize.Services.DeviceApiKeys;
using FiscalizeXero.Shared.Models;
using FiscalizeXero.Shared.Models.Fiscalize;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FiscalizeXero.Api.Controllers.Fiscalization
{
    /// <summary>API keys for a ZIMRA device, managed on the Fiscalize platform.</summary>
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    [FiscalizeXero.Api.Filters.CompanyScoped]
    public class DeviceApiKeysController : FiscalizeProxyControllerBase
    {
        public DeviceApiKeysController(IFiscalizeSessionService session, ILogger<DeviceApiKeysController> logger) : base(session, logger) { }

        [HttpGet("devices/{deviceId}")]
        [ProducesResponseType(typeof(List<FiscalizeDeviceApiKey>), StatusCodes.Status200OK)]
        public async Task<ActionResult<List<FiscalizeDeviceApiKey>>> GetByDevice(long deviceId)
        {
            try { return Ok(await Session.RunAsync(a => FiscalizeDeviceApiKeyService.GetByDeviceAsync(a, deviceId))); }
            catch (FiscalizeApiException ex) { return FiscalizeError(ex, $"fetching API keys for device {deviceId}"); }
        }

        [ConnectionManager]
        [HttpPost("devices/{deviceId}")]
        [ProducesResponseType(typeof(FiscalizeDeviceApiKey), StatusCodes.Status200OK)]
        public async Task<ActionResult<FiscalizeDeviceApiKey>> Create(long deviceId, [FromBody] CreateFiscalizeApiKeyRequest request)
        {
            try { return Ok(await Session.RunAsync(a => FiscalizeDeviceApiKeyService.CreateAsync(a, deviceId, request))); }
            catch (FiscalizeApiException ex) { return FiscalizeError(ex, $"creating API key for device {deviceId}"); }
        }

        /// <summary>Rotates the key.</summary>
        [ConnectionManager]
        [HttpPost("{id}/generate")]
        [ProducesResponseType(typeof(FiscalizeDeviceApiKey), StatusCodes.Status200OK)]
        public async Task<ActionResult<FiscalizeDeviceApiKey>> GenerateNewKey(long id)
        {
            try { return Ok(await Session.RunAsync(a => FiscalizeDeviceApiKeyService.GenerateNewKeyAsync(a, id))); }
            catch (FiscalizeApiException ex) { return FiscalizeError(ex, $"generating new key for API key {id}"); }
        }

        [ConnectionManager]
        [HttpPost("{id}/status")]
        [ProducesResponseType(typeof(bool), StatusCodes.Status200OK)]
        public async Task<ActionResult<bool>> UpdateStatus(long id, [FromBody] UpdateFiscalizeApiKeyStatusRequest request)
        {
            try { return Ok(await Session.RunAsync(a => FiscalizeDeviceApiKeyService.UpdateStatusAsync(a, id, request))); }
            catch (FiscalizeApiException ex) { return FiscalizeError(ex, $"updating status for API key {id}"); }
        }

        [ConnectionManager]
        [HttpPost("{id}/permissions")]
        [ProducesResponseType(typeof(bool), StatusCodes.Status200OK)]
        public async Task<ActionResult<bool>> UpdatePermissions(long id, [FromBody] UpdateFiscalizeApiKeyPermissionsRequest request)
        {
            try { return Ok(await Session.RunAsync(a => FiscalizeDeviceApiKeyService.UpdatePermissionsAsync(a, id, request))); }
            catch (FiscalizeApiException ex) { return FiscalizeError(ex, $"updating permissions for API key {id}"); }
        }

        [HttpPost("{id}/test")]
        [ProducesResponseType(typeof(bool), StatusCodes.Status200OK)]
        public async Task<ActionResult<bool>> Test(long id)
        {
            try { return Ok(await Session.RunAsync(a => FiscalizeDeviceApiKeyService.TestAsync(a, id))); }
            catch (FiscalizeApiException ex) { return FiscalizeError(ex, $"testing API key {id}"); }
        }

        /// <summary>Revokes the key permanently.</summary>
        [ConnectionManager]
        [HttpDelete("{id}")]
        [ProducesResponseType(typeof(bool), StatusCodes.Status200OK)]
        public async Task<ActionResult<bool>> Revoke(long id)
        {
            try { return Ok(await Session.RunAsync(a => FiscalizeDeviceApiKeyService.RevokeAsync(a, id))); }
            catch (FiscalizeApiException ex) { return FiscalizeError(ex, $"revoking API key {id}"); }
        }
    }
}
