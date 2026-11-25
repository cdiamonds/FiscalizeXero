namespace FiscalizeXero.Fiscalize.Models.HsCodes
{
    public class HsCodeSearchResponse
    {
        public List<HsCodeItem> Value { get; set; } = new();
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
    }

    public class HsCodeItem
    {
        public int Id { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }
}
