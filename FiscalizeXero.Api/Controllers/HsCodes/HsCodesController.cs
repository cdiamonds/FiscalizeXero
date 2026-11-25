using FiscalizeXero.Api.Exceptions;
using FiscalizeXero.Api.Services.HsCodes;
using FiscalizeXero.Shared.Models;
using FiscalizeXero.Shared.Models.HsCodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FiscalizeXero.Api.Controllers.HsCodes
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    [FiscalizeXero.Api.Filters.CompanyScoped]
    public class HsCodesController : ControllerBase
    {
        private readonly IHsCodeService _hsCodeService;
        private readonly ILogger<HsCodesController> _logger;

        public HsCodesController(IHsCodeService hsCodeService, ILogger<HsCodesController> logger)
        {
            _hsCodeService = hsCodeService;
            _logger = logger;
        }

        [HttpGet("validate")]
        [ProducesResponseType(typeof(HsCodeLookupItem), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ErrorModel), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ErrorModel), StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<HsCodeLookupItem>> Validate([FromQuery] string code)
        {
            if (string.IsNullOrWhiteSpace(code))
                return BadRequest(new ErrorModel { Code = 400, Message = "Code is required" });

            try
            {
                var result = await _hsCodeService.ValidateAsync(code);
                if (result == null)
                    return NotFound();

                return Ok(result);
            }
            catch (Exception ex) when (ex is FiscalizeSessionExpiredException or FiscalizePlatformUnavailableException)
            {
                throw; // answered by FiscalizeSessionExceptionFilter: "sign in again", or "try again in a moment"
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating HS code {Code}", code);
                return StatusCode(500, new ErrorModel { Code = 500, Message = "Failed to validate HS code" });
            }
        }

        [HttpGet]
        [ProducesResponseType(typeof(PagedHsCodes), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorModel), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ErrorModel), StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<PagedHsCodes>> Search(
            [FromQuery] string? search,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            try
            {
                var result = await _hsCodeService.SearchAsync(search, page, pageSize);
                return Ok(result);
            }
            catch (Exception ex) when (ex is FiscalizeSessionExpiredException or FiscalizePlatformUnavailableException)
            {
                throw; // answered by FiscalizeSessionExceptionFilter: "sign in again", or "try again in a moment"
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching HS codes");
                return StatusCode(500, new ErrorModel
                {
                    Code = 500,
                    Message = "Failed to retrieve HS codes. Please try again."
                });
            }
        }
    }
}
