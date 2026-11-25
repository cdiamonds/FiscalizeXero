using FiscalizeXero.Api.Exceptions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace FiscalizeXero.Api.Filters
{
    public class FiscalizeSessionExceptionFilter : IExceptionFilter
    {
        public void OnException(ExceptionContext context)
        {
            if (context.Exception is FiscalizePlatformUnavailableException down)
            {
                // Not the session: the page shows "try again in a moment", and nobody is asked to sign in.
                context.Result = new ObjectResult(new { code = 503, message = down.Message, platformUnavailable = true }) { StatusCode = 503 };
                context.ExceptionHandled = true;
                return;
            }
            if (context.Exception is not FiscalizeSessionExpiredException ex)
                return;

            context.Result = new ObjectResult(new
            {
                code = 401,
                message = ex.Message,
                fiscalizeSessionExpired = true
            })
            { StatusCode = 401 };

            context.ExceptionHandled = true;
        }
    }
}
