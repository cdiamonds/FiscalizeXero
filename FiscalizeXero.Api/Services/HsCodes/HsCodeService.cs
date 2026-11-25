using FiscalizeXero.Api.Exceptions;
using FiscalizeXero.Api.Services.FiscalizeSession;
using FiscalizeXero.Fiscalize.Exceptions;
using FiscalizeXero.Shared.Models.HsCodes;
using System.Net;

namespace FiscalizeXero.Api.Services.HsCodes
{
    public class HsCodeService : IHsCodeService
    {
        private readonly IFiscalizeSessionService _fiscalizeSessionService;

        public HsCodeService(IFiscalizeSessionService fiscalizeSessionService)
        {
            _fiscalizeSessionService = fiscalizeSessionService;
        }

        public async Task<PagedHsCodes?> SearchAsync(string? search, int page, int pageSize)
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 5, 50);

            // Renewed and retried on a platform 401; a session that can't be renewed asks the user to sign in.
            var response = await _fiscalizeSessionService.RunAsync(a => FiscalizeXero.Fiscalize.Services.HsCodes.HsCodeService.SearchAsync(a, search, page, pageSize));
            return new PagedHsCodes
                {
                    Items = (response.Value ?? []).Select(x => new HsCodeLookupItem
                    {
                        Id = x.Id,
                        Code = x.Code,
                        Description = string.IsNullOrWhiteSpace(x.Description) ? null : x.Description
                    }).ToList(),
                    TotalCount = response.TotalCount,
                    Page = response.Page > 0 ? response.Page : page,
                    PageSize = response.PageSize > 0 ? response.PageSize : pageSize
                };
        }

        public async Task<HsCodeLookupItem?> ValidateAsync(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
                return null;

            var result = await SearchAsync(code.Trim(), 1, 10);
            return result?.Items.FirstOrDefault(x =>
                string.Equals(x.Code, code.Trim(), StringComparison.OrdinalIgnoreCase));
        }
    }
}
