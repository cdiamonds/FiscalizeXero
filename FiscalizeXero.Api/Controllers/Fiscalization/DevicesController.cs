using FiscalizeXero.Api.Services.FiscalizeSession;
using FiscalizeXero.Fiscalize.Exceptions;
using FiscalizeXero.Fiscalize.Services.Devices;
using FiscalizeXero.Shared.Models;
using FiscalizeXero.Shared.Models.Fiscalize;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FiscalizeXero.Api.Controllers.Fiscalization
{
    /// <summary>The user's ZIMRA devices on the Fiscalize platform.</summary>
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    [FiscalizeXero.Api.Filters.CompanyScoped]
    public class DevicesController : FiscalizeProxyControllerBase
    {
        public DevicesController(IFiscalizeSessionService session, ILogger<DevicesController> logger) : base(session, logger) { }

        [HttpGet]
        [ProducesResponseType(typeof(List<FiscalizeDevice>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorModel), StatusCodes.Status401Unauthorized)]
        public async Task<ActionResult<List<FiscalizeDevice>>> GetAll()
        {
            try { return Ok(await Session.RunAsync(a => FiscalizeDeviceService.GetAllAsync(a))); }
            catch (FiscalizeApiException ex) { return FiscalizeError(ex, "fetching devices"); }
        }

        [HttpGet("{id}")]
        [ProducesResponseType(typeof(FiscalizeDevice), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorModel), StatusCodes.Status404NotFound)]
        public async Task<ActionResult<FiscalizeDevice>> GetById(long id)
        {
            try { return Ok(await Session.RunAsync(a => FiscalizeDeviceService.GetByIdAsync(a, id))); }
            catch (FiscalizeApiException ex) { return FiscalizeError(ex, $"fetching device {id}"); }
        }

        /// <summary>The default device with fiscalization enabled, or 404.</summary>
        [HttpGet("default")]
        [ProducesResponseType(typeof(FiscalizeDevice), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorModel), StatusCodes.Status404NotFound)]
        public async Task<ActionResult<FiscalizeDevice>> GetDefault()
        {
            try
            {
                var device = await Session.RunAsync(a => FiscalizeDeviceService.GetDefaultAsync(a));
                if (device == null)
                    return NotFound(new ErrorModel { Code = 404, Message = "No default device found with fiscalization enabled" });
                return Ok(device);
            }
            catch (FiscalizeApiException ex) { return FiscalizeError(ex, "fetching default device"); }
        }
    }
}
