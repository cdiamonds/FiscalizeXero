using System.Text.RegularExpressions;
using FiscalizeXero.Api.Data;

namespace FiscalizeXero.Api.Services.Fiscalization
{
    /// <summary>
    /// The short fiscal marker Fiscalize can append to a Xero document's Reference, e.g.
    /// <c>PO-4471 [ZIMRA 1024/A1B2-C3D4-E5F6-G7H8]</c> (receipt number / verification code). Whatever the customer
    /// had in the Reference is always kept in full, a re-fiscalisation replaces the marker instead of stacking
    /// another, and the result never exceeds Xero's 255 characters.
    /// </summary>
    public static partial class FiscalReferenceMarker
    {
        /// <summary>Xero's limit for Reference on invoices and credit notes.</summary>
        public const int MaxReferenceLength = 255;

        [GeneratedRegex(@"\s*\[ZIMRA [^\]\r\n]*\]")]
        private static partial Regex MarkerPattern();

        /// <summary>The marker for a fiscal record, or null when it has no receipt number to show.</summary>
        public static string? For(FiscalRecord record, bool withVerificationCode = true)
        {
            if (!record.ReceiptNumber.HasValue) return null;
            var code = record.VerificationCode?.Trim();
            return withVerificationCode && !string.IsNullOrEmpty(code) && !code.Contains(']')
                ? $"[ZIMRA {record.ReceiptNumber}/{code}]"
                : $"[ZIMRA {record.ReceiptNumber}]";
        }

        public static bool HasMarker(string? reference) => !string.IsNullOrEmpty(reference) && MarkerPattern().IsMatch(reference);

        /// <summary>The Reference without our marker (the customer's own text), or null when nothing is left.</summary>
        public static string? Strip(string? reference)
        {
            if (string.IsNullOrWhiteSpace(reference)) return null;
            var text = MarkerPattern().Replace(reference, string.Empty).Trim();
            return text.Length == 0 ? null : text;
        }

        /// <summary>
        /// The Reference to write after fiscalising: the customer's text followed by the marker. Null when nothing
        /// needs writing: there is no receipt number, the Reference already ends up the same, or the customer's own
        /// text leaves no room even for the short marker (their text is never cut).
        /// </summary>
        public static string? Add(string? existingReference, FiscalRecord record)
        {
            var own = Strip(existingReference);
            foreach (var marker in new[] { For(record), For(record, withVerificationCode: false) })
            {
                if (marker == null) return null;
                var combined = own == null ? marker : $"{own} {marker}";
                if (combined.Length > MaxReferenceLength) continue;
                return string.Equals(combined, existingReference, StringComparison.Ordinal) ? null : combined;
            }
            return null;
        }

        /// <summary>
        /// The Reference to write after a fiscal cancellation: the customer's text with our marker taken out ("" when
        /// the marker was all there was). Null when there is no marker, so nothing needs writing.
        /// </summary>
        public static string? Remove(string? existingReference) =>
            HasMarker(existingReference) ? Strip(existingReference) ?? string.Empty : null;
    }
}
