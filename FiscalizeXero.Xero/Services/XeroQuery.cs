using System.Globalization;
using System.Text;

namespace FiscalizeXero.Xero.Services
{
    /// <summary>What a document list asks Xero for. Everything is optional.</summary>
    public class DocumentQuery
    {
        /// <summary>1-based page. Xero only returns line items on paged requests.</summary>
        public int Page { get; set; } = 1;
        /// <summary>Rows per page, 1 to 1000 (Xero's default is 100).</summary>
        public int PageSize { get; set; } = 50;
        /// <summary>Statuses to include, e.g. AUTHORISED and PAID. Empty = all.</summary>
        public IReadOnlyCollection<string> Statuses { get; set; } = Array.Empty<string>();
        /// <summary>Only documents for this contact.</summary>
        public Guid? ContactId { get; set; }
        /// <summary>Text to find in the document number or reference.</summary>
        public string? Search { get; set; }
        /// <summary>Only documents dated on or after this day.</summary>
        public DateTime? DateFrom { get; set; }
        /// <summary>Only documents dated on or before this day.</summary>
        public DateTime? DateTo { get; set; }
        /// <summary>Only documents created or changed after this instant (UTC); sent as If-Modified-Since.</summary>
        public DateTime? ModifiedSince { get; set; }
        /// <summary>Xero order clause, e.g. "Date DESC".</summary>
        public string Order { get; set; } = "Date DESC";
    }

    /// <summary>Builds Xero's <c>where</c> filters and query strings. Pure, so it is unit tested without a network.</summary>
    public static class XeroQuery
    {
        /// <summary>A string literal for a <c>where</c> clause: Xero uses double quotes and doubles a quote inside one.</summary>
        public static string Quote(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";

        public static string Guid(Guid id) => $"guid(\"{id}\")";

        public static string Date(DateTime day) =>
            string.Create(CultureInfo.InvariantCulture, $"DateTime({day.Year}, {day.Month:00}, {day.Day:00})");

        /// <summary>
        /// The <c>where</c> clause for a list of one document type. <paramref name="numberField"/> is the field the
        /// search text is matched against besides Reference ("InvoiceNumber" or "CreditNoteNumber").
        /// </summary>
        public static string Where(string type, DocumentQuery query, string numberField)
        {
            var parts = new List<string> { $"Type=={Quote(type)}" };

            if (query.Statuses.Count == 1)
                parts.Add($"Status=={Quote(query.Statuses.First())}");
            else if (query.Statuses.Count > 1)
                parts.Add("(" + string.Join("||", query.Statuses.Select(s => $"Status=={Quote(s)}")) + ")");

            if (query.ContactId.HasValue)
                parts.Add($"Contact.ContactID=={Guid(query.ContactId.Value)}");
            if (query.DateFrom.HasValue)
                parts.Add($"Date>={Date(query.DateFrom.Value)}");
            if (query.DateTo.HasValue)
                parts.Add($"Date<={Date(query.DateTo.Value)}");

            var search = query.Search?.Trim();
            if (!string.IsNullOrEmpty(search))
            {
                // Null-guarded: Contains on an empty Reference makes Xero fail the whole query.
                var text = Quote(search.ToLowerInvariant());
                parts.Add($"(({numberField}!=null&&{numberField}.ToLower().Contains({text}))" +
                          $"||(Reference!=null&&Reference.ToLower().Contains({text}))" +
                          $"||(Contact.Name!=null&&Contact.Name.ToLower().Contains({text})))");
            }

            return string.Join("&&", parts);
        }

        /// <summary>The query string (with leading '?') for a document list.</summary>
        public static string DocumentQueryString(string type, DocumentQuery query, string numberField)
        {
            var sb = new StringBuilder();
            sb.Append("?where=").Append(Uri.EscapeDataString(Where(type, query, numberField)));
            if (!string.IsNullOrWhiteSpace(query.Order))
                sb.Append("&order=").Append(Uri.EscapeDataString(query.Order));
            sb.Append("&page=").Append(Math.Max(1, query.Page).ToString(CultureInfo.InvariantCulture));
            sb.Append("&pageSize=").Append(Math.Clamp(query.PageSize, 1, 1000).ToString(CultureInfo.InvariantCulture));
            return sb.ToString();
        }
    }
}
