using FiscalizeXero.Api.Services.CurrentUser;
using FiscalizeXero.Api.Services.Fiscalization;
using FiscalizeXero.Api.Services.XeroAuth;
using FiscalizeXero.Shared.Models.Fiscalize;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FiscalizeXero.Api.Controllers.Xero
{
    /// <summary>
    /// The organisation's Xero tax rates with their effective rate and ZIMRA category, so document pages can show
    /// tax per line (a line names its rate by <c>TaxType</c>). Read from Xero at most once every few minutes.
    /// </summary>
    [Route("api/xero/taxrates")]
    [ApiController]
    [Authorize]
    public class TaxRatesController : XeroBaseController
    {
        private readonly IXeroTaxRateCache _taxRates;

        public TaxRatesController(ICurrentUserService currentUserService, IXeroAuthBuilder authBuilder, IXeroTaxRateCache taxRates)
            : base(currentUserService, authBuilder) { _taxRates = taxRates; }

        [HttpGet]
        [ProducesResponseType(typeof(List<TaxRateModel>), StatusCodes.Status200OK)]
        public async Task<ActionResult> ListAsync()
        {
            var (auth, _, error) = await ResolveAuthAsync();
            if (auth == null) return error!;

            var rates = await _taxRates.GetAsync(auth);
            var resolved = XeroTaxResolver.Resolve(rates);
            return Ok(rates
                .Where(r => !string.IsNullOrWhiteSpace(r.TaxType) && resolved.ContainsKey(r.TaxType!))
                .Select(r => new TaxRateModel
                {
                    TaxType = r.TaxType!,
                    Name = r.Name,
                    Percent = resolved[r.TaxType!].Percent,
                    Category = resolved[r.TaxType!].Category,
                    Status = r.Status
                })
                .ToList());
        }
    }
}
