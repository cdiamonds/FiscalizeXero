namespace FiscalizeXero.Shared.Models.Documents
{
    /// <summary>One page of a list (invoices, credit notes, items), with the totals for the whole list.</summary>
    public class DocumentPageModel<T>
    {
        public List<T> Items { get; set; } = new();
        /// <summary>Rows in the whole list, or matches for a search.</summary>
        public int Total { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalPages { get; set; }
        public string? Search { get; set; }
        /// <summary>The Xero statuses the list was limited to (e.g. AUTHORISED, PAID); empty when it was not limited.</summary>
        public List<string> Statuses { get; set; } = new();
    }
}
