using FiscalizeXero.Api.Data;

namespace FiscalizeXero.Api.Services.Fiscalization
{
    /// <summary>
    /// The text Fiscalize adds to a Xero document's "History and notes". Xero has no custom fields and no private
    /// note on a document, and history notes can be added even when the document itself can no longer be edited, so
    /// this is where the fiscal result is recorded inside Xero.
    /// </summary>
    public static class FiscalHistoryNote
    {
        /// <summary>The note written after a fiscalisation: receipt, global number, device, fiscal day, verification code and link.</summary>
        public static string Fiscalised(FiscalRecord record)
        {
            var parts = new List<string>();
            if (record.ReceiptNumber.HasValue) parts.Add($"Receipt no. {record.ReceiptNumber}");
            if (record.GlobalNumber.HasValue) parts.Add($"Global no. {record.GlobalNumber}");
            if (record.DeviceId > 0) parts.Add($"Device {record.DeviceId}");
            if (record.FiscalDayNumber.HasValue) parts.Add($"Fiscal day {record.FiscalDayNumber}");
            if (!string.IsNullOrWhiteSpace(record.VerificationCode)) parts.Add($"Verification code {record.VerificationCode.Trim()}");
            if (!string.IsNullOrWhiteSpace(record.QrCodeUrl)) parts.Add($"Verify at {record.QrCodeUrl.Trim()}");

            var what = record.DocumentType == FiscalDocumentType.CreditNote ? "credit note" : "invoice";
            var note = $"ZIMRA fiscalised by Fiscalize on {record.FiscalizedAt:yyyy-MM-dd HH:mm} UTC ({what} {record.DocumentNumber}).";
            return parts.Count == 0 ? note : $"{note} {string.Join(". ", parts)}.";
        }

        /// <summary>The note written after a fiscal cancellation.</summary>
        public static string Cancelled(FiscalRecord record, DateTime cancelledAtUtc)
        {
            var receipt = record.ReceiptNumber.HasValue ? $" (receipt no. {record.ReceiptNumber})" : string.Empty;
            return $"ZIMRA fiscal receipt{receipt} for invoice {record.DocumentNumber} was cancelled in Fiscalize on {cancelledAtUtc:yyyy-MM-dd HH:mm} UTC. " +
                   "A credit note was submitted to ZIMRA to reverse it; the invoice is no longer fiscalised.";
        }
    }
}
