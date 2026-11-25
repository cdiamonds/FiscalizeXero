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
    /// <summary>Xero customer credit notes (type ACCRECCREDIT). Supplier credits share Xero's endpoint and are never returned.</summary>
    [Route("api/xero/creditnotes")]
    [ApiController]
    [Authorize]
    public class CreditNotesController : XeroBaseController
    {
        public CreditNotesController(ICurrentUserService currentUserService, IXeroAuthBuilder authBuilder)
            : base(currentUserService, authBuilder) { }

        /// <summary>
        /// One page of customer credit notes, newest date first. <c>search</c> matches the credit note number, the
        /// reference or the contact's name. <c>status</c> is empty for approved credit notes (AUTHORISED and PAID),
        /// "all", or a comma-separated list of Xero statuses.
        /// </summary>
        [HttpGet("paged")]
        [ProducesResponseType(typeof(DocumentPageModel<CreditNote>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorModel), StatusCodes.Status404NotFound)]
        public async Task<ActionResult> PagedAsync([FromQuery] int page = 1, [FromQuery] int pageSize = DocumentLister.DefaultPageSize,
            [FromQuery] string? search = null, [FromQuery] string? status = null)
        {
            var (auth, _, error) = await ResolveAuthAsync();
            if (auth == null) return error!;

            var query = DocumentLister.BuildQuery(page, pageSize, search, status);
            return Ok(DocumentLister.ToPage(await CreditNoteService.ListAsync(auth, query), query));
        }

        [HttpGet("{id:guid}")]
        [ProducesResponseType(typeof(CreditNote), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorModel), StatusCodes.Status404NotFound)]
        public async Task<ActionResult> GetAsync(Guid id)
        {
            var (auth, _, error) = await ResolveAuthAsync();
            if (auth == null) return error!;

            CreditNote? creditNote;
            try { creditNote = await CreditNoteService.GetByIdAsync(auth, id); }
            catch (XeroApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound) { creditNote = null; }

            if (creditNote == null || !creditNote.IsCustomerCredit)
                return NotFound(new ErrorModel { Code = 404, Message = $"Xero has no customer credit note with id {id} in this organisation." });
            return Ok(creditNote);
        }

        /// <summary>
        /// Approved customer credit notes, most recently changed first. <c>since</c> (UTC) restricts the list to
        /// credit notes created or changed after that time.
        /// </summary>
        [HttpGet]
        [ProducesResponseType(typeof(List<CreditNote>), StatusCodes.Status200OK)]
        public async Task<ActionResult> ListAsync([FromQuery] DateTime? since, [FromQuery] int max = 100)
        {
            var (auth, _, error) = await ResolveAuthAsync();
            if (auth == null) return error!;

            var result = await CreditNoteService.ListAsync(auth, new DocumentQuery
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
