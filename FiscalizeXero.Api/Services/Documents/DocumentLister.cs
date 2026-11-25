using FiscalizeXero.Shared.Models.Documents;
using FiscalizeXero.Xero.Models;
using FiscalizeXero.Xero.Services;

namespace FiscalizeXero.Api.Services.Documents
{
    /// <summary>
    /// Pages through a Xero document type newest first. Xero does the paging, the status filter and the search
    /// (document number, reference or contact name) itself, so every page is one call to Xero, however many
    /// documents the organisation has.
    /// </summary>
    public static class DocumentLister
    {
        public const int DefaultPageSize = 25;
        public const int MaxPageSize = 100;
        public const int MaxSearchLength = 100;

        /// <summary>The statuses that are real sales, and the default for every list.</summary>
        public static readonly IReadOnlyList<string> ApprovedStatuses = new[] { XeroStatus.Authorised, XeroStatus.Paid };

        private static readonly string[] KnownStatuses =
            { XeroStatus.Draft, XeroStatus.Submitted, XeroStatus.Authorised, XeroStatus.Paid, XeroStatus.Voided, XeroStatus.Deleted };

        public static int ClampPageSize(int pageSize) => pageSize <= 0 ? DefaultPageSize : Math.Min(pageSize, MaxPageSize);

        /// <summary>Trimmed search text, or null for none. Long input is cut to <see cref="MaxSearchLength"/>.</summary>
        public static string? NormaliseSearch(string? search)
        {
            var t = search?.Trim();
            if (string.IsNullOrEmpty(t)) return null;
            return t.Length > MaxSearchLength ? t[..MaxSearchLength] : t;
        }

        /// <summary>
        /// The statuses a list asks Xero for. Nothing given: approved documents only (AUTHORISED and PAID).
        /// "all": no status filter. Otherwise a comma-separated list of Xero statuses; names Xero does not have are
        /// dropped, and if none is left the default applies, so a typo can never widen the list.
        /// </summary>
        public static IReadOnlyList<string> ParseStatuses(string? status)
        {
            if (string.IsNullOrWhiteSpace(status)) return ApprovedStatuses;
            if (string.Equals(status.Trim(), "all", StringComparison.OrdinalIgnoreCase)) return Array.Empty<string>();
            var wanted = status.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => s.ToUpperInvariant())
                .Where(s => KnownStatuses.Contains(s))
                .Distinct()
                .ToList();
            return wanted.Count == 0 ? ApprovedStatuses : wanted;
        }

        /// <summary>What to ask Xero for one page: newest document date first.</summary>
        public static DocumentQuery BuildQuery(int page, int pageSize, string? search, string? status) => new()
        {
            Page = Math.Max(1, page),
            PageSize = ClampPageSize(pageSize),
            Search = NormaliseSearch(search),
            Statuses = ParseStatuses(status).ToArray(),
            Order = "Date DESC"
        };

        /// <summary>Xero's page as the shape every list in the app uses.</summary>
        public static DocumentPageModel<T> ToPage<T>(XeroPage<T> page, DocumentQuery query) => new()
        {
            Items = page.Items,
            Total = page.ItemCount,
            Page = query.Page,
            PageSize = query.PageSize,
            TotalPages = Math.Max(1, (int)Math.Ceiling(page.ItemCount / (double)query.PageSize)),
            Search = query.Search,
            Statuses = query.Statuses.ToList()
        };

        /// <summary>A page cut out of a list that is already in memory (items, which Xero does not page).</summary>
        public static DocumentPageModel<T> PageOf<T>(IReadOnlyList<T> all, int page, int pageSize, string? search)
        {
            pageSize = ClampPageSize(pageSize);
            page = Math.Max(1, page);
            return new DocumentPageModel<T>
            {
                Items = all.Skip((page - 1) * pageSize).Take(pageSize).ToList(),
                Total = all.Count,
                Page = page,
                PageSize = pageSize,
                TotalPages = Math.Max(1, (int)Math.Ceiling(all.Count / (double)pageSize)),
                Search = NormaliseSearch(search)
            };
        }
    }
}
