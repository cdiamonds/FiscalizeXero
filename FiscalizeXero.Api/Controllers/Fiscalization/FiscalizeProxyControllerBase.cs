using FiscalizeXero.Api.Services.FiscalizeSession;
using FiscalizeXero.Fiscalize.Exceptions;
using FiscalizeXero.Shared.Models;
using FiscalizeXero.Shared.Models.Auths;
using Microsoft.AspNetCore.Mvc;

namespace FiscalizeXero.Api.Controllers.Fiscalization
{
    /// <summary>
    /// Base for controllers that proxy calls to the Fiscalize platform with the user's session. Calls go through
    /// <see cref="IFiscalizeSessionService.RunAsync{T}"/>: a lapsed or platform-ended session is renewed and the call
    /// retried; one that can't be renewed throws FiscalizeSessionExpiredException, which FiscalizeSessionExceptionFilter
    /// turns into the 401 + fiscalizeSessionExpired marker the browser answers with "sign in again".
    /// </summary>
    public abstract class FiscalizeProxyControllerBase : ControllerBase
    {
        protected readonly IFiscalizeSessionService Session;
        protected readonly ILogger Logger;

        protected FiscalizeProxyControllerBase(IFiscalizeSessionService session, ILogger logger)
        {
            Session = session;
            Logger = logger;
        }

        protected ActionResult FiscalizeError(FiscalizeApiException ex, string context)
        {
            Logger.LogError(ex, "Fiscalize API error — {Context}. Status: {StatusCode}", context, ex.StatusCode);
            // A 401 here comes with a session that was just renewed, so it is the platform refusing this call (a device
            // or API key it doesn't accept), not the session: 424 lets the page show it without signing the user out.
            if (ex.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                return StatusCode(StatusCodes.Status424FailedDependency, new ErrorModel { Code = 424, Message = WithHint(string.IsNullOrWhiteSpace(ex.Message)
                    ? "The Fiscalize platform didn't accept this request. If it's about a device or API key, rotate or re-enter the key; otherwise open this app again from the Fiscalize platform."
                    : ex.Message) });
            if ((int)ex.StatusCode >= 500)
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new ErrorModel { Code = 503,
                    Message = "The Fiscalize platform isn't answering right now. Try again in a moment; you don't need to sign in again." });
            if (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
                return NotFound(new ErrorModel { Code = 404, Message = ex.Message });
            return BadRequest(new ErrorModel { Code = (int)ex.StatusCode, Message = WithHint(ex.Message) });
        }

        /// <summary>
        /// The platform refuses changes from a read-only support session. This app holds the platform session it was
        /// opened with, so switching to "Act on their behalf" on the platform only reaches it on the next launch.
        /// </summary>
        internal static string WithHint(string message)
            => message.Contains("support mode", StringComparison.OrdinalIgnoreCase)
                ? message + " If you already chose \"Act on their behalf\" on the Fiscalize platform, open Fiscalize for Xero again from the platform so it gets that session, then try again."
                : message;
    }
}
