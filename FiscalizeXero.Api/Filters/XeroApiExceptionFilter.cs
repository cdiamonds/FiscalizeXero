using FiscalizeXero.Shared.Models;
using FiscalizeXero.Xero.Exceptions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace FiscalizeXero.Api.Filters
{
    /// <summary>
    /// A Xero error that no action handled itself (a list or lookup that Xero refused) goes out with Xero's own
    /// message instead of a bare 500: "Xero's daily limit of calls for this organisation has been reached…" tells
    /// the user what happened and what to do.
    /// </summary>
    public class XeroApiExceptionFilter : IExceptionFilter
    {
        private readonly ILogger<XeroApiExceptionFilter> _logger;
        public XeroApiExceptionFilter(ILogger<XeroApiExceptionFilter> logger) => _logger = logger;

        public void OnException(ExceptionContext context)
        {
            if (context.Exception is not XeroApiException ex) return;

            _logger.LogWarning(ex, "Xero refused a call made for {Path}", context.HttpContext.Request.Path.Value);
            context.Result = new ObjectResult(ToError(ex)) { StatusCode = UpstreamStatus(ex.StatusCode) };
            context.ExceptionHandled = true;
        }

        public static ErrorModel ToError(XeroApiException ex) => new()
        {
            Code = (int)ex.StatusCode,
            Message = ex.IsAuthenticationFailure || ex.IsForbidden
                ? $"Xero: {ex.Message} If this keeps happening, reconnect Xero for this company."
                : $"Xero: {ex.Message}",
            Errors = ex.ValidationErrors.Count > 0 ? ex.ValidationErrors.ToList() : null
        };

        /// <summary>
        /// A 401/403 from Xero or the Fiscalize platform (e.g. a device API key refused) is not this app's sign-in
        /// failing, so it goes out as 424 Failed Dependency: the browser shows the error instead of signing the user out.
        /// </summary>
        public static int UpstreamStatus(System.Net.HttpStatusCode status)
            => status is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden ? StatusCodes.Status424FailedDependency : (int)status;
    }
}
