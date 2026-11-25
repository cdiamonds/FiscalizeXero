using FiscalizeXero.Api.Filters;
using FiscalizeXero.Api.Services.CurrentUser;
using FiscalizeXero.Api.Services.DeviceConfig;
using FiscalizeXero.Shared.Models;
using FiscalizeXero.Shared.Models.Fiscalize;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FiscalizeXero.Api.Controllers.Fiscalization
{
    /// <summary>
    /// Manages locally configured devices used for fiscalization
    /// </summary>
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    [FiscalizeXero.Api.Filters.CompanyScoped]
    public class DeviceConfigController : ControllerBase
    {
        private readonly ICurrentUserService _currentUserService;
        private readonly IDeviceConfigService _deviceConfigService;
        private readonly ILogger<DeviceConfigController> _logger;

        public DeviceConfigController(
            ICurrentUserService currentUserService,
            IDeviceConfigService deviceConfigService,
            ILogger<DeviceConfigController> logger)
        {
            _currentUserService = currentUserService;
            _deviceConfigService = deviceConfigService;
            _logger = logger;
        }

        /// <summary>
        /// Get all configured devices for the active company
        /// </summary>
        [HttpGet]
        [ProducesResponseType(typeof(List<DeviceConfigModel>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorModel), StatusCodes.Status401Unauthorized)]
        public async Task<ActionResult<List<DeviceConfigModel>>> GetAll()
        {
            var (companyId, error) = await GetActiveCompanyIdAsync();
            if (error != null) return error;

            var configs = await _deviceConfigService.GetAllAsync(companyId);
            return Ok(configs.Select(MapToModel).ToList());
        }

        /// <summary>
        /// Save or update the configuration for a specific device.
        /// The first device saved becomes the default automatically.
        /// </summary>
        [ConnectionManager]
        [HttpPut]
        [ProducesResponseType(typeof(DeviceConfigModel), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorModel), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ErrorModel), StatusCodes.Status401Unauthorized)]
        public async Task<ActionResult<DeviceConfigModel>> Save([FromBody] SaveDeviceConfigModel model)
        {
            if (model.DeviceId <= 0)
                return BadRequest(new ErrorModel { Code = 400, Message = "DeviceId is required" });

            var (companyId, error) = await GetActiveCompanyIdAsync();
            if (error != null) return error;

            try
            {
                var (config, warning) = await _deviceConfigService.SaveAsync(companyId, model);
                _logger.LogInformation("Device {DeviceId} configured for company {CompanyId}", model.DeviceId, companyId);
                var result = MapToModel(config);
                result.Warning = warning;
                return Ok(result);
            }
            catch (FiscalizeXero.Api.Exceptions.UserFacingException ex)
            {
                return BadRequest(new ErrorModel { Code = 400, Message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving device config for company {CompanyId}", companyId);
                return BadRequest(new ErrorModel { Code = 400, Message = "Failed to save device configuration" });
            }
        }

        /// <summary>
        /// Set a configured device as the default used for fiscalization
        /// </summary>
        [ConnectionManager]
        [HttpPost("{id}/set-default")]
        [ProducesResponseType(typeof(DeviceConfigModel), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorModel), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ErrorModel), StatusCodes.Status401Unauthorized)]
        public async Task<ActionResult<DeviceConfigModel>> SetDefault(int id)
        {
            var (companyId, error) = await GetActiveCompanyIdAsync();
            if (error != null) return error;

            var config = await _deviceConfigService.SetDefaultAsync(companyId, id);
            if (config == null)
                return NotFound(new ErrorModel { Code = 404, Message = "Device configuration not found" });

            _logger.LogInformation("Device config {ConfigId} set as default for company {CompanyId}", id, companyId);
            return Ok(MapToModel(config));
        }

        /// <summary>
        /// Remove a specific device configuration
        /// </summary>
        [ConnectionManager]
        [HttpDelete("{id}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(typeof(ErrorModel), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ErrorModel), StatusCodes.Status401Unauthorized)]
        public async Task<ActionResult> Delete(int id)
        {
            var (companyId, error) = await GetActiveCompanyIdAsync();
            if (error != null) return error;

            var deleted = await _deviceConfigService.DeleteAsync(companyId, id);
            if (!deleted)
                return NotFound(new ErrorModel { Code = 404, Message = "Device configuration not found" });

            return NoContent();
        }

        private async Task<(int companyId, ActionResult? error)> GetActiveCompanyIdAsync()
        {
            var appUser = await _currentUserService.GetCurrentUserAsync();
            if (appUser == null)
                return (0, Unauthorized(new ErrorModel { Code = 401, Message = "User not found" }));

            // Devices belong to the Fiscalize company, stored with its Xero connection: every colleague sees and
            // manages the same ones.
            var anchorId = await _currentUserService.GetCompanyAnchorIdAsync();
            if (anchorId == null)
                return (0, BadRequest(new ErrorModel { Code = 400, Message = "No active company selected" }));

            return (anchorId.Value, null);
        }

        private static DeviceConfigModel MapToModel(Data.AppUserDeviceConfig config) => new()
        {
            Id = config.Id,
            DeviceId = config.DeviceId,
            Platform = config.Platform,
            DeviceNumber = config.DeviceNumber,
            ApiKeyId = config.ApiKeyId,
            ApiKeyDescription = config.ApiKeyDescription,
            ApiKey = config.ApiKey,
            IsDefault = config.IsDefault,
            DateConfigured = config.DateConfigured,
            DateUpdated = config.DateUpdated
        };
    }
}
