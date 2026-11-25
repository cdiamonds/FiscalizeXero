using FiscalizeXero.Api.Services.CurrentUser;
using FiscalizeXero.Api.Services.Membership;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace FiscalizeXero.Api.Filters
{
    /// <summary>
    /// Marks a controller (or action) that reads or changes the active company's Xero connection or data.
    /// <see cref="CompanyMembershipFilter"/> re-confirms with the Fiscalize platform that the user still belongs to that
    /// company before it runs. Inherited, so every Xero controller gets it from its base class.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true)]
    public sealed class CompanyScopedAttribute : Attribute { }

    /// <summary>
    /// Global filter for <see cref="CompanyScopedAttribute"/> endpoints. A user the platform no longer lists for the
    /// company gets 403; one whose membership can't be confirmed (platform session lapsed past the grace period) gets
    /// the 401 + <c>fiscalizeSessionExpired</c> marker the client already turns into "sign in again".
    /// </summary>
    public sealed class CompanyMembershipFilter : IAsyncActionFilter
    {
        private readonly ICurrentUserService _currentUser;
        private readonly ICompanyMembershipVerifier _verifier;

        /// <summary>Creates the filter.</summary>
        public CompanyMembershipFilter(ICurrentUserService currentUser, ICompanyMembershipVerifier verifier)
        {
            _currentUser = currentUser;
            _verifier = verifier;
        }

        /// <inheritdoc />
        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var metadata = context.ActionDescriptor.EndpointMetadata;
            if (!metadata.OfType<CompanyScopedAttribute>().Any() || metadata.OfType<IAllowAnonymous>().Any())
            {
                await next();
                return;
            }

            var user = await _currentUser.GetCurrentUserAsync();
            if (user?.ActiveCompany == null)
            {
                await next(); // the endpoint answers 401/404 itself; there is no company data to protect
                return;
            }

            var name = string.IsNullOrWhiteSpace(user.ActiveCompany.CompanyName) ? "this company" : user.ActiveCompany.CompanyName;
            switch (await _verifier.CheckAsync(user.ActiveCompany, context.HttpContext.RequestAborted))
            {
                case MembershipDecision.Allowed:
                    await next();
                    return;
                case MembershipDecision.Revoked:
                    context.Result = new ObjectResult(new
                    {
                        code = 403,
                        message = $"You no longer have access to {name} on Fiscalize. Switch to another company, or ask its administrator to add you again.",
                        companyAccessRevoked = true
                    }) { StatusCode = StatusCodes.Status403Forbidden };
                    return;
                default:
                    context.Result = new ObjectResult(new
                    {
                        code = 401,
                        message = $"Sign in to Fiscalize again to confirm your access to {name}.",
                        fiscalizeSessionExpired = true
                    }) { StatusCode = StatusCodes.Status401Unauthorized };
                    return;
            }
        }
    }
}
