using FiscalizeXero.Api.Services.CurrentUser;
using FiscalizeXero.Api.Services.XeroAuth;
using FiscalizeXero.Shared.Models;
using FiscalizeXero.Xero.Models;
using FiscalizeXero.Xero.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FiscalizeXero.Api.Controllers.Xero
{
    [Route("api/xero/organisation")]
    [ApiController]
    [Authorize]
    public class OrganisationController : XeroBaseController
    {
        public OrganisationController(ICurrentUserService currentUserService, IXeroAuthBuilder authBuilder)
            : base(currentUserService, authBuilder) { }

        /// <summary>
        /// The connected Xero organisation. If this answers 200, the connection, its token and the tenant id all work.
        /// </summary>
        [HttpGet]
        [ProducesResponseType(typeof(Organisation), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorModel), StatusCodes.Status404NotFound)]
        public async Task<ActionResult> GetAsync()
        {
            var (auth, _, error) = await ResolveAuthAsync();
            if (auth == null) return error!;

            var organisation = await OrganisationService.GetAsync(auth);
            if (organisation == null)
                return NotFound(new ErrorModel { Code = 404, Message = "Xero did not return the organisation. Reconnect Xero for this company." });
            return Ok(organisation);
        }
    }
}
