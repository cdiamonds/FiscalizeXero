namespace FiscalizeXero.Shared.Models.HsCodes
{
    public class HsCodeLookupItem
    {
        public int Id { get; set; }
        public string Code { get; set; } = string.Empty;
        public string? Description { get; set; }
    }

    public class PagedHsCodes
    {
        public IReadOnlyList<HsCodeLookupItem> Items { get; set; } = Array.Empty<HsCodeLookupItem>();
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
    }
}
