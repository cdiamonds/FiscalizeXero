using FiscalizeXero.Api.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FiscalizeXero.Api.Services.Webhooks
{
    /// <summary>
    /// Xero signs every webhook POST: the <c>x-xero-signature</c> header is the base64 HMAC-SHA256 of the raw request
    /// body, keyed with the webhook key shown in the developer portal once the webhook is saved.
    ///
    /// Xero also uses the signature to decide whether to switch the webhook on at all ("intent to receive"): it sends
    /// a run of test deliveries, some signed correctly and some not, and expects 200 for the good ones and 401 for the
    /// bad ones, each within 5 seconds. So a wrong signature must always answer 401, never 200 and never 400.
    /// </summary>
    public static class XeroWebhookSignature
    {
        public const string HeaderName = "x-xero-signature";

        public static string Compute(byte[] body, string webhookKey)
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(webhookKey));
            return Convert.ToBase64String(hmac.ComputeHash(body));
        }

        /// <summary>
        /// True when the header matches the body. Compared in constant time, on the exact bytes Xero sent: parsing
        /// and re-serialising the JSON first would change the bytes and fail every delivery.
        /// </summary>
        public static bool Verify(byte[] body, string? signatureHeader, string? webhookKey)
        {
            if (string.IsNullOrWhiteSpace(webhookKey) || string.IsNullOrWhiteSpace(signatureHeader))
                return false;

            byte[] presented;
            try
            {
                presented = Convert.FromBase64String(signatureHeader.Trim());
            }
            catch (FormatException)
            {
                return false;
            }

            var expected = Convert.FromBase64String(Compute(body, webhookKey));
            return CryptographicOperations.FixedTimeEquals(expected, presented);
        }
    }

    /// <summary>One change out of a Xero webhook delivery, already narrowed to a document type we fiscalise.</summary>
    public record XeroDocumentChange(Guid TenantId, string DocumentType, Guid DocumentId, string Operation, string? EventDateUtc);

    /// <summary>
    /// Parses a Xero webhook delivery:
    /// <code>{"events":[{"resourceId":"717f…","tenantId":"c2cc…","tenantType":"ORGANISATION","eventCategory":"INVOICE","eventType":"UPDATE","eventDateUtc":"2026-11-23T01:15:39.902"}],"firstEventSequence":1,"lastEventSequence":1,"entropy":"…"}</code>
    /// The delivery says only that something changed; the document itself is fetched afterwards. Xero's INVOICE
    /// category covers purchase bills too, which are told apart only once the document is read.
    /// </summary>
    public static class XeroWebhookPayload
    {
        /// <summary>The <see cref="FiscalDocumentType"/> for a Xero event category, or null when it is not one we fiscalise.</summary>
        public static string? DocumentTypeFor(string? eventCategory)
        {
            if (string.Equals(eventCategory, "INVOICE", StringComparison.OrdinalIgnoreCase)) return FiscalDocumentType.Invoice;
            if (string.Equals(eventCategory, "CREDITNOTE", StringComparison.OrdinalIgnoreCase)) return FiscalDocumentType.CreditNote;
            return null;
        }

        /// <summary>"Create" or "Update", the wording the rest of the pipeline uses; anything else is passed through.</summary>
        public static string OperationFor(string? eventType)
        {
            if (string.Equals(eventType, "CREATE", StringComparison.OrdinalIgnoreCase)) return "Create";
            if (string.Equals(eventType, "UPDATE", StringComparison.OrdinalIgnoreCase)) return "Update";
            return string.IsNullOrWhiteSpace(eventType) ? "Unknown" : eventType!;
        }

        /// <summary>
        /// The changes in a delivery that concern invoices and credit notes of an organisation. An empty list is
        /// normal: the "intent to receive" test deliveries carry no events. Throws <see cref="JsonException"/> on a
        /// body that is not JSON.
        /// </summary>
        public static IReadOnlyList<XeroDocumentChange> Parse(string body)
        {
            var changes = new List<XeroDocumentChange>();
            if (string.IsNullOrWhiteSpace(body)) return changes;

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("events", out var events) || events.ValueKind != JsonValueKind.Array)
                return changes;

            foreach (var evt in events.EnumerateArray())
            {
                if (evt.ValueKind != JsonValueKind.Object) continue;
                var tenantType = Text(evt, "tenantType");
                if (tenantType != null && !string.Equals(tenantType, "ORGANISATION", StringComparison.OrdinalIgnoreCase)) continue;

                var documentType = DocumentTypeFor(Text(evt, "eventCategory"));
                if (documentType == null) continue;
                if (!Guid.TryParse(Text(evt, "tenantId"), out var tenantId) || tenantId == Guid.Empty) continue;
                if (!Guid.TryParse(Text(evt, "resourceId"), out var resourceId) || resourceId == Guid.Empty) continue;

                changes.Add(new XeroDocumentChange(tenantId, documentType, resourceId,
                    OperationFor(Text(evt, "eventType")), Text(evt, "eventDateUtc")));
            }
            return changes;
        }

        private static string? Text(JsonElement obj, string name) =>
            obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    }
}
