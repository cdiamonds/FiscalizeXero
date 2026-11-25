using FiscalizeXero.Api.Services.CurrentUser;
using FiscalizeXero.Api.Services.Documents;
using FiscalizeXero.Api.Services.XeroAuth;
using FiscalizeXero.Shared.Models;
using FiscalizeXero.Shared.Models.Documents;
using FiscalizeXero.Xero.Exceptions;
using FiscalizeXero.Xero.Models;
using FiscalizeXero.Xero.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FiscalizeXero.Api.Controllers.Xero
{
    /// <summary>Xero sales invoices (type ACCREC). Purchase bills share Xero's endpoint and are never returned.</summary>
    [Route("api/xero/invoices")]
    [ApiController]
    [Authorize]
    public class InvoicesController : XeroBaseController
    {
        public InvoicesController(ICurrentUserService currentUserService, IXeroAuthBuilder authBuilder)
            : base(currentUserService, authBuilder) { }

        /// <summary>
        /// One page of sales invoices, newest invoice date first. <c>search</c> matches the invoice number, the
        /// reference or the contact's name. <c>status</c> is empty for approved invoices (AUTHORISED and PAID), "all",
        /// or a comma-separated list of Xero statuses.
        /// </summary>
        [HttpGet("paged")]
        [ProducesResponseType(typeof(DocumentPageModel<Invoice>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorModel), StatusCodes.Status404NotFound)]
        public async Task<ActionResult> PagedAsync([FromQuery] int page = 1, [FromQuery] int pageSize = DocumentLister.DefaultPageSize,
            [FromQuery] string? search = null, [FromQuery] string? status = null)
        {
            var (auth, _, error) = await ResolveAuthAsync();
            if (auth == null) return error!;

            var query = DocumentLister.BuildQuery(page, pageSize, search, status);
            return Ok(DocumentLister.ToPage(await InvoiceService.ListAsync(auth, query), query));
        }

        [HttpGet("{id:guid}")]
        [ProducesResponseType(typeof(Invoice), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorModel), StatusCodes.Status404NotFound)]
        public async Task<ActionResult> GetAsync(Guid id)
        {
            var (auth, _, error) = await ResolveAuthAsync();
            if (auth == null) return error!;

            Invoice? invoice;
            try { invoice = await InvoiceService.GetByIdAsync(auth, id); }
            catch (XeroApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound) { invoice = null; }

            // A purchase bill has an id in the same space; it is not ours to show.
            if (invoice == null || !invoice.IsSale)
                return NotFound(new ErrorModel { Code = 404, Message = $"Xero has no sales invoice with id {id} in this organisation." });
            return Ok(invoice);
        }

        /// <summary>
        /// Approved sales invoices, most recently changed first. <c>since</c> (UTC) restricts the list to invoices
        /// created or changed after that time.
        /// </summary>
        [HttpGet]
        [ProducesResponseType(typeof(List<Invoice>), StatusCodes.Status200OK)]
        public async Task<ActionResult> ListAsync([FromQuery] DateTime? since, [FromQuery] int max = 100)
        {
            var (auth, _, error) = await ResolveAuthAsync();
            if (auth == null) return error!;

            var result = await InvoiceService.ListAsync(auth, new DocumentQuery
            {
                PageSize = Math.Clamp(max, 1, 1000),
                Statuses = DocumentLister.ApprovedStatuses.ToArray(),
                ModifiedSince = since?.ToUniversalTime(),
                Order = "UpdatedDateUTC DESC"
            });
            return Ok(result.Items);
        }
    }
}
