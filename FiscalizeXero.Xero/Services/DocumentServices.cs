using FiscalizeXero.Xero.Models;

namespace FiscalizeXero.Xero.Services
{
    /// <summary>Sales invoices (Xero type ACCREC). Purchase bills share the endpoint and are always filtered out.</summary>
    public static class InvoiceService
    {
        public const string SalesType = "ACCREC";

        /// <summary>One invoice with its lines, or null when Xero has none with that id.</summary>
        public static async Task<Invoice?> GetByIdAsync(XeroAuth auth, Guid id)
        {
            var envelope = await RestClient.GetAsync<InvoicesEnvelope>(auth, $"Invoices/{id}");
            return envelope?.Invoices?.FirstOrDefault();
        }

        /// <summary>A page of sales invoices, lines included.</summary>
        public static async Task<XeroPage<Invoice>> ListAsync(XeroAuth auth, DocumentQuery query)
        {
            var envelope = await RestClient.GetAsync<InvoicesEnvelope>(auth,
                "Invoices" + XeroQuery.DocumentQueryString(SalesType, query, "InvoiceNumber"), query.ModifiedSince);
            return Paging.ToPage(envelope?.Invoices, envelope?.Pagination, query);
        }

        /// <summary>Several invoices by id in one call (Xero's <c>IDs</c> filter), lines included.</summary>
        public static async Task<List<Invoice>> GetByIdsAsync(XeroAuth auth, IReadOnlyCollection<Guid> ids)
        {
            var result = new List<Invoice>();
            foreach (var chunk in ids.Distinct().Chunk(40))
            {
                var envelope = await RestClient.GetAsync<InvoicesEnvelope>(auth,
                    $"Invoices?IDs={string.Join(",", chunk)}&page=1&pageSize=100");
                if (envelope?.Invoices != null) result.AddRange(envelope.Invoices);
            }
            return result;
        }

        /// <summary>The invoice as Xero renders it (the organisation's own branding theme), as PDF bytes.</summary>
        public static Task<byte[]> GetPdfAsync(XeroAuth auth, Guid id) =>
            RestClient.GetBytesAsync(auth, $"Invoices/{id}");

        /// <summary>The address of the invoice's online view, or null when Xero has none (drafts, or online invoicing off).</summary>
        public static async Task<string?> GetOnlineUrlAsync(XeroAuth auth, Guid id)
        {
            var envelope = await RestClient.GetAsync<OnlineInvoicesEnvelope>(auth, $"Invoices/{id}/OnlineInvoice");
            return envelope?.OnlineInvoices?.FirstOrDefault()?.OnlineInvoiceUrl;
        }

        /// <summary>
        /// Sets the invoice's Reference and nothing else. Xero allows this on approved and on paid invoices, where
        /// most other fields are locked.
        /// </summary>
        public static async Task<Invoice?> UpdateReferenceAsync(XeroAuth auth, Guid id, string reference)
        {
            var body = new { Invoices = new[] { new { InvoiceID = id, Reference = reference } } };
            var envelope = await RestClient.PostAsync<object, InvoicesEnvelope>(auth, $"Invoices/{id}", body);
            return envelope?.Invoices?.FirstOrDefault();
        }
    }

    /// <summary>Customer credit notes (Xero type ACCRECCREDIT). Supplier credits share the endpoint and are filtered out.</summary>
    public static class CreditNoteService
    {
        public const string CustomerCreditType = "ACCRECCREDIT";

        public static async Task<CreditNote?> GetByIdAsync(XeroAuth auth, Guid id)
        {
            var envelope = await RestClient.GetAsync<CreditNotesEnvelope>(auth, $"CreditNotes/{id}");
            return envelope?.CreditNotes?.FirstOrDefault();
        }

        public static async Task<XeroPage<CreditNote>> ListAsync(XeroAuth auth, DocumentQuery query)
        {
            var envelope = await RestClient.GetAsync<CreditNotesEnvelope>(auth,
                "CreditNotes" + XeroQuery.DocumentQueryString(CustomerCreditType, query, "CreditNoteNumber"), query.ModifiedSince);
            return Paging.ToPage(envelope?.CreditNotes, envelope?.Pagination, query);
        }

        public static Task<byte[]> GetPdfAsync(XeroAuth auth, Guid id) =>
            RestClient.GetBytesAsync(auth, $"CreditNotes/{id}");

        /// <summary>Sets the credit note's Reference and nothing else. Xero refuses once the credit is allocated or refunded.</summary>
        public static async Task<CreditNote?> UpdateReferenceAsync(XeroAuth auth, Guid id, string reference)
        {
            var body = new { CreditNotes = new[] { new { CreditNoteID = id, Reference = reference } } };
            var envelope = await RestClient.PostAsync<object, CreditNotesEnvelope>(auth, $"CreditNotes/{id}", body);
            return envelope?.CreditNotes?.FirstOrDefault();
        }
    }

    /// <summary>
    /// "History and notes" and file attachments, which work the same way on invoices and credit notes. The
    /// <c>endpoint</c> argument is "Invoices" or "CreditNotes" (<see cref="For"/>).
    /// </summary>
    public static class DocumentExtrasService
    {
        public const string Invoices = "Invoices";
        public const string CreditNotes = "CreditNotes";

        public static string For(SalesDocument document) => document is CreditNote ? CreditNotes : Invoices;

        public static async Task<List<HistoryRecord>> GetHistoryAsync(XeroAuth auth, string endpoint, Guid id)
        {
            var envelope = await RestClient.GetAsync<HistoryRecordsEnvelope>(auth, $"{endpoint}/{id}/History");
            return envelope?.HistoryRecords ?? new List<HistoryRecord>();
        }

        /// <summary>Adds a note to the document's history. Notes cannot be edited or removed afterwards. Xero's limit is 2,500 characters.</summary>
        public static Task AddNoteAsync(XeroAuth auth, string endpoint, Guid id, string details)
        {
            var text = details.Length > 2500 ? details[..2500] : details;
            var body = new HistoryRecordsEnvelope { HistoryRecords = new List<HistoryRecord> { new() { Details = text } } };
            return RestClient.PutAsync<HistoryRecordsEnvelope, HistoryRecordsEnvelope>(auth, $"{endpoint}/{id}/History", body);
        }

        public static async Task<List<Attachment>> GetAttachmentsAsync(XeroAuth auth, string endpoint, Guid id)
        {
            var envelope = await RestClient.GetAsync<AttachmentsEnvelope>(auth, $"{endpoint}/{id}/Attachments");
            return envelope?.Attachments ?? new List<Attachment>();
        }

        /// <summary>
        /// Attaches a file to the document; a file of the same name is replaced. With <paramref name="includeOnline"/>
        /// the customer sees it on the online invoice.
        /// </summary>
        public static async Task<Attachment?> UploadAttachmentAsync(XeroAuth auth, string endpoint, Guid id, string fileName,
            byte[] content, string contentType = "application/pdf", bool includeOnline = false)
        {
            var url = $"{endpoint}/{id}/Attachments/{Uri.EscapeDataString(fileName)}" + (includeOnline ? "?IncludeOnline=true" : "");
            var envelope = await RestClient.PostBytesAsync<AttachmentsEnvelope>(auth, url, content, contentType);
            return envelope?.Attachments?.FirstOrDefault();
        }
    }

    internal static class Paging
    {
        public static XeroPage<T> ToPage<T>(List<T>? items, XeroPagination? pagination, DocumentQuery query)
        {
            var list = items ?? new List<T>();
            var pageSize = pagination?.PageSize > 0 ? pagination.PageSize : query.PageSize;
            // Older responses carry no pagination block: a full page means there may be more.
            var itemCount = pagination?.ItemCount ?? ((query.Page - 1) * pageSize + list.Count + (list.Count >= pageSize ? 1 : 0));
            return new XeroPage<T>
            {
                Items = list,
                Page = pagination?.Page > 0 ? pagination.Page : query.Page,
                PageSize = pageSize,
                PageCount = pagination?.PageCount > 0 ? pagination.PageCount : (int)Math.Ceiling(itemCount / (double)Math.Max(1, pageSize)),
                ItemCount = itemCount
            };
        }
    }
}
