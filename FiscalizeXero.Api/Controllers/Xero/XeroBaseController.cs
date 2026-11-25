using FiscalizeXero.Api.Data;
using FiscalizeXero.Api.Services.CurrentUser;
using FiscalizeXero.Api.Services.XeroAuth;
using FiscalizeXero.Shared.Models;
using Microsoft.AspNetCore.Mvc;
using XeroAuthModel = FiscalizeXero.Xero.XeroAuth;

namespace FiscalizeXero.Api.Controllers.Xero
{
    /// <summary>
    /// Shared base for controllers that read or write the connected Xero organisation. Centralises the connection
    /// handshake so each endpoint just calls ResolveAuthAsync(); a null auth means the right error response is
    /// already in hand. Carries <c>[CompanyScoped]</c>, so every Xero controller re-checks company membership.
    /// </summary>
    [FiscalizeXero.Api.Filters.CompanyScoped]
    public abstract class XeroBaseController : ControllerBase
    {
        protected readonly ICurrentUserService CurrentUserService;
        protected readonly IXeroAuthBuilder AuthBuilder;

        protected XeroBaseController(ICurrentUserService currentUserService, IXeroAuthBuilder authBuilder)
        {
            CurrentUserService = currentUserService;
            AuthBuilder = authBuilder;
        }

        protected async Task<(XeroAuthModel? Auth, ClientConfig? Config, ActionResult? Error)> ResolveAuthAsync()
        {
            var config = await CurrentUserService.GetCurrentClientConfigAsync();
            if (config == null)
            {
                return (null, null, NotFound(new ErrorModel
                {
                    Code = 404,
                    Message = "Xero is not connected for this company. Connect Xero first."
                }));
            }

            var auth = await AuthBuilder.BuildAsync(config);
            if (auth == null)
            {
                // 424, not 401: the user's own sign-in is fine, it is the Xero connection that needs attention.
                return (null, config, StatusCode(StatusCodes.Status424FailedDependency, new ErrorModel
                {
                    Code = 424,
                    Message = "The Xero connection is not authorised any more. Reconnect Xero for this company, then try again."
                }));
            }
            return (auth, config, null);
        }
    }
}
