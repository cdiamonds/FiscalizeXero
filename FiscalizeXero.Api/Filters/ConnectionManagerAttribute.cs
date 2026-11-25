using FiscalizeXero.Api.Security;
using FiscalizeXero.Api.Services.CurrentUser;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace FiscalizeXero.Api.Filters
{
    /// <summary>
    /// Only the person who manages the company's Xero connection (<see cref="ConnectionManagers"/>) may run this
    /// action; anyone else gets 403 with who to ask and how to take over.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
    public sealed class ConnectionManagerAttribute : Attribute, IAsyncActionFilter
    {
        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var currentUser = context.HttpContext.RequestServices.GetRequiredService<ICurrentUserService>();
            var config = await currentUser.GetCurrentClientConfigAsync();
            if (config != null && !ConnectionManagers.CanManage(config, currentUser.GetCurrentUserId()))
            {
                context.Result = new ObjectResult(ConnectionManagers.Refusal(config)) { StatusCode = StatusCodes.Status403Forbidden };
                return;
            }
            await next();
        }
    }
}
