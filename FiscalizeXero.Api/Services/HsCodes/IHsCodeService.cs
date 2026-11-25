using FiscalizeXero.Shared.Models.HsCodes;

namespace FiscalizeXero.Api.Services.HsCodes
{
    public interface IHsCodeService
    {
        Task<PagedHsCodes?> SearchAsync(string? search, int page, int pageSize);
        Task<HsCodeLookupItem?> ValidateAsync(string code);
    }
}
