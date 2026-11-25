using FiscalizeXero.Api.Data;
using FiscalizeXero.Api.Repositories;
using FiscalizeXero.Api.Services.CurrentUser;
using FiscalizeXero.Api.Services.Documents;
using FiscalizeXero.Api.Services.HsCodes;
using FiscalizeXero.Api.Services.XeroAuth;
using FiscalizeXero.Shared.Models;
using FiscalizeXero.Shared.Models.Documents;
using FiscalizeXero.Shared.Models.TaxDetails;
using FiscalizeXero.Xero.Models;
using FiscalizeXero.Xero.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using XeroAuthModel = FiscalizeXero.Xero.XeroAuth;

namespace FiscalizeXero.Api.Controllers.Xero
{
    /// <summary>
    /// Xero items (products and services) with the HS code each one fiscalizes under. Xero has no HS code field and
    /// no custom fields, so codes are kept in Fiscalize against the item code that invoice lines carry.
    /// </summary>
    [Route("api/xero/items")]
    [ApiController]
    [Authorize]
    public class ItemsController : XeroBaseController
    {
        /// <summary>Xero returns every item in one call and cannot page or search them, so the list is reused briefly.</summary>
        private static readonly TimeSpan ItemsLifetime = TimeSpan.FromSeconds(60);

        private readonly IItemHsCodeRepository _itemHsCodeRepository;
        private readonly IMemoryCache _cache;

        public ItemsController(
            ICurrentUserService currentUserService,
            IXeroAuthBuilder authBuilder,
            IItemHsCodeRepository itemHsCodeRepository,
            IMemoryCache cache)
            : base(currentUserService, authBuilder)
        {
            _itemHsCodeRepository = itemHsCodeRepository;
            _cache = cache;
        }

        [HttpGet]
        [ProducesResponseType(typeof(List<ItemHsCodeModel>), StatusCodes.Status200OK)]
        public async Task<ActionResult> ListAsync([FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, [FromQuery] bool refresh = false)
        {
            var (auth, config, error) = await ResolveAuthAsync();
            if (auth == null) return error!;

            pageSize = Math.Clamp(pageSize, 1, 200);
            var items = Filter(await AllItemsAsync(auth, refresh), search)
                .Skip((Math.Max(page, 1) - 1) * pageSize).Take(pageSize).ToList();
            return Ok(await WithHsCodesAsync(items, config!));
        }

        /// <summary>
        /// One page of items by name with their HS codes, plus the total, for the Items page's pager. <c>search</c>
        /// matches part of the item code, name or description. The item list is reused for a minute; <c>refresh</c>
        /// reads Xero again.
        /// </summary>
        [HttpGet("paged")]
        [ProducesResponseType(typeof(DocumentPageModel<ItemHsCodeModel>), StatusCodes.Status200OK)]
        public async Task<ActionResult> PagedAsync([FromQuery] string? search, [FromQuery] int page = 1,
            [FromQuery] int pageSize = DocumentLister.DefaultPageSize, [FromQuery] bool refresh = false)
        {
            var (auth, config, error) = await ResolveAuthAsync();
            if (auth == null) return error!;

            var matching = Filter(await AllItemsAsync(auth, refresh), DocumentLister.NormaliseSearch(search));
            var result = DocumentLister.PageOf(matching, page, pageSize, search);
            return Ok(new DocumentPageModel<ItemHsCodeModel>
            {
                Items = await WithHsCodesAsync(result.Items, config!),
                Total = result.Total,
                Page = result.Page,
                PageSize = result.PageSize,
                TotalPages = result.TotalPages,
                Search = result.Search
            });
        }

        /// <summary>
        /// The HS code each of the given item codes fiscalizes under (saved, or the default), keyed by item code.
        /// Lets a document page show the code per line without loading the item list; it does not call Xero.
        /// Repeat the parameter for several codes: <c>?codes=A&amp;codes=B</c>.
        /// </summary>
        [HttpGet("hs-codes")]
        [ProducesResponseType(typeof(Dictionary<string, ItemHsCodeModel>), StatusCodes.Status200OK)]
        public async Task<ActionResult> HsCodesForAsync([FromQuery] string[] codes)
        {
            var config = await CurrentUserService.GetCurrentClientConfigAsync();
            if (config == null)
                return NotFound(new ErrorModel { Code = 404, Message = "Xero is not connected for this company. Connect Xero first." });

            var itemCodes = (codes ?? Array.Empty<string>())
                .Select(c => c?.Trim()).Where(c => !string.IsNullOrEmpty(c) && c.Length <= 60).Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase).Take(100).ToList();

            var saved = ItemHsCodes.Lookup((await _itemHsCodeRepository.GetManyAsync(config.AppUserCompanyId, itemCodes)).Select(r => (r.ItemCode, r.HsCode)));
            var result = new Dictionary<string, ItemHsCodeModel>(StringComparer.OrdinalIgnoreCase);
            foreach (var code in itemCodes)
                result[code] = ItemHsCodes.Describe(code, null, saved.GetValueOrDefault(code), config.EffectiveDefaultHsCode);
            return Ok(result);
        }

        /// <summary>
        /// Saves the HS code for a Xero item code. It is kept in Fiscalize (Xero has nowhere to hold it). An empty
        /// code goes back to the company's default.
        /// </summary>
        [HttpPut("hs-code")]
        [ProducesResponseType(typeof(ItemHsCodeModel), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorModel), StatusCodes.Status400BadRequest)]
        public async Task<ActionResult> SaveHsCodeAsync([FromBody] SaveItemHsCodeModel model)
        {
            var config = await CurrentUserService.GetCurrentClientConfigAsync();
            if (config == null)
                return NotFound(new ErrorModel { Code = 404, Message = "Xero is not connected for this company. Connect Xero first." });

            var itemCode = model.ItemCode?.Trim();
            if (string.IsNullOrEmpty(itemCode) || itemCode.Length > 60)
                return BadRequest(new ErrorModel { Code = 400, Message = "Choose the Xero item the HS code is for. An item needs an item code in Xero to have its own HS code." });

            string? hsCode = null;
            if (!string.IsNullOrWhiteSpace(model.HsCode))
            {
                hsCode = ItemHsCodes.AsHsCode(model.HsCode);
                if (hsCode == null)
                    return BadRequest(new ErrorModel { Code = 400, Message = "An HS code is 6 to 10 digits, for example 04021099. Pick one from the HS code catalogue." });
            }

            await _itemHsCodeRepository.UpsertAsync(config.AppUserCompanyId, itemCode, hsCode);
            // Documents that failed on an HS code are tried again now rather than at their next scheduled retry.
            var failures = HttpContext?.RequestServices.GetService<FiscalizeXero.Api.Services.Fiscalization.IFiscalFailureService>();
            if (failures != null) await failures.RetrySoonAsync(config.AppUserCompanyId, FiscalizeXero.Api.Data.FiscalFailureCategory.HsCode);

            // Only to give the page the item's name back; the cached list is used when there is one.
            Item? item = null;
            if (config.TenantId.HasValue && _cache.TryGetValue(ItemsKey(config.TenantId.Value), out List<Item>? cached) && cached != null)
                item = cached.FirstOrDefault(i => string.Equals(i.Code?.Trim(), itemCode, StringComparison.OrdinalIgnoreCase));
            return Ok(ItemHsCodes.Describe(itemCode, item, hsCode, config.EffectiveDefaultHsCode));
        }

        private async Task<List<Item>> AllItemsAsync(XeroAuthModel auth, bool refresh)
        {
            var key = ItemsKey(auth.TenantId);
            // A reload asked for within seconds of the last read is served from memory (Xero's daily allowance).
            if (refresh && _cache.TryGetValue(key + ":fresh", out _)) refresh = false;
            if (!refresh && _cache.TryGetValue(key, out List<Item>? cached) && cached != null) return cached;
            var items = (await ItemService.ListAsync(auth)).OrderBy(i => i.Name ?? i.Code, StringComparer.OrdinalIgnoreCase).ToList();
            _cache.Set(key, items, ItemsLifetime);
            _cache.Set(key + ":fresh", true, TimeSpan.FromSeconds(15));
            return items;
        }

        private static string ItemsKey(Guid tenantId) => $"xero-items:{tenantId}";

        /// <summary>Items whose code, name or description contains the search, any case. Items with no code can't appear on a line and are left out.</summary>
        internal static List<Item> Filter(IEnumerable<Item> items, string? search)
        {
            var term = search?.Trim();
            bool Has(string? v) => v != null && v.Contains(term!, StringComparison.OrdinalIgnoreCase);
            return items
                .Where(i => !string.IsNullOrWhiteSpace(i.Code))
                .Where(i => string.IsNullOrEmpty(term) || Has(i.Code) || Has(i.Name) || Has(i.Description))
                .ToList();
        }

        /// <summary>The HS code each item fiscalizes under: saved in Fiscalize for its item code, else the default.</summary>
        private async Task<List<ItemHsCodeModel>> WithHsCodesAsync(IReadOnlyList<Item> items, ClientConfig config)
        {
            var codes = items.Select(i => i.Code!.Trim()).ToList();
            var saved = ItemHsCodes.Lookup((await _itemHsCodeRepository.GetManyAsync(config.AppUserCompanyId, codes)).Select(r => (r.ItemCode, r.HsCode)));
            return items.Select(i => ItemHsCodes.Describe(i.Code!.Trim(), i, saved.GetValueOrDefault(i.Code!.Trim()), config.EffectiveDefaultHsCode)).ToList();
        }
    }
}
