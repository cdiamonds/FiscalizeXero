using FiscalizeXero.Fiscalize.Models.Auths;
using FiscalizeXero.Shared.Models;
using FiscalizeXero.Shared;
using FiscalizeXero.Shared.Models.Auths;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Net;
using FiscalizeXero.Api.Services.Auths;

namespace FiscalizeXero.Api.Controllers.Auths
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthsController : ControllerBase
    {
        private readonly IAuthService _authService;
        private readonly VC _vc = new();

        public AuthsController(IAuthService authService)
        {
            _authService = authService;
        }

        /// <summary>
        /// Ends every sign-in of the current user on every device and browser (a lost phone, a shared computer). The
        /// token used for this call stops working too; the user signs in again where they still want to be.
        /// </summary>
        [HttpPost("SignOutEverywhere")]
        [Microsoft.AspNetCore.Authorization.Authorize]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public async Task<ActionResult> SignOutEverywhereAsync([FromServices] FiscalizeXero.Api.Security.ISessionRevocation revocation)
        {
            if (!long.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var userId))
                return Unauthorized();
            await revocation.RevokeAllAsync(userId);
            return NoContent();
        }

        /// <summary>
        /// This logs in a user
        /// </summary>
        /// <param name="login"></param>
        /// <returns></returns>
        [HttpPost("Login")]
        [EnableRateLimiting("auth")]
        [ProducesResponseType(typeof(LoginResponseModel), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorModel), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ErrorModel), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ErrorModel), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ErrorModel), StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<LoginResponseModel>> LoginUserAsync([FromBody] LoginModel login)
        {
            var response = await _authService.LoginAsync(User, login);

            if (_vc.IsHttpSuccess(response.Status))
            {
                Response.Headers.Append("Authorization", $"Bearer {response.Data?.Token}");
                return Ok(response.Data);
            }

            // Return proper error response based on status code
            var errorModel = new ErrorModel
            {
                Code = (int)response.Status,
                Message = response.Message ?? GetDefaultErrorMessage(response.Status)
            };

            return response.Status switch
            {
                HttpStatusCode.Unauthorized => Unauthorized(errorModel),
                HttpStatusCode.NotFound => NotFound(errorModel),
                HttpStatusCode.BadRequest => BadRequest(errorModel),
                HttpStatusCode.Forbidden => StatusCode(403, errorModel),
                _ => StatusCode((int)response.Status, errorModel)
            };
        }

        [AllowAnonymous]
        [EnableRateLimiting("auth")]
        [HttpPost("ExchangeLaunchToken")]
        [ProducesResponseType(typeof(LoginResponseModel), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorModel), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ErrorModel), StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<LoginResponseModel>> ExchangeLaunchTokenAsync([FromBody] ExchangeLaunchTokenRequest request)
        {
            var response = await _authService.ExchangeLaunchTokenAsync(request.Token);

            if (_vc.IsHttpSuccess(response.Status))
            {
                Response.Headers.Append("Authorization", $"Bearer {response.Data?.Token}");
                return Ok(response.Data);
            }

            return StatusCode((int)response.Status, new ErrorModel { Code = (int)response.Status, Message = response.Message });
        }

        [Authorize]
        [HttpPost("OpenFiscalize")]
        [ProducesResponseType(typeof(string), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorModel), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ErrorModel), StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<string>> OpenFiscalizeAsync()
        {
            var response = await _authService.OpenFiscalizeAsync(User);

            if (_vc.IsHttpSuccess(response.Status))
                return Ok(new { launchUrl = response.Data });
            if (response.Status == HttpStatusCode.Unauthorized)
                return StatusCode(401, new { code = 401, message = response.Message, fiscalizeSessionExpired = true });

            return StatusCode((int)response.Status, new ErrorModel { Code = (int)response.Status, Message = response.Message });
        }

        /// <summary>
        /// Called by the browser every few minutes while the user is active, and when they come back to the tab: keeps
        /// the Fiscalize platform session alive (renewed ahead of expiry, otherwise used once), and when it is over
        /// answers 401 + fiscalizeSessionExpired so the user is asked to sign in again before they meet any error.
        /// 204 also when the platform can't be reached right now: that is never a reason to ask for a sign-in.
        /// </summary>
        [Authorize]
        [HttpPost("KeepAlive")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<ActionResult> KeepAliveAsync([FromServices] FiscalizeXero.Api.Services.FiscalizeSession.IFiscalizeSessionService session)
        {
            await session.KeepAliveAsync();
            return NoContent();
        }

        private static string GetDefaultErrorMessage(HttpStatusCode status)
        {
            return status switch
            {
                HttpStatusCode.Unauthorized => "Invalid credentials",
                HttpStatusCode.NotFound => "User not found",
                HttpStatusCode.BadRequest => "Invalid request",
                HttpStatusCode.Forbidden => "Access denied",
                _ => "An error occurred"
            };
        }
    }
}
