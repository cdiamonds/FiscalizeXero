using FiscalizeXero.Api.Data;
using FiscalizeXero.Shared.Models.Fiscalize;

namespace FiscalizeXero.Api.Services.Fiscalization
{
    /// <summary>The stored fiscal record as the API presents it (the same shape a fiscalization call returns).</summary>
    public static class FiscalRecordMapping
    {
        public static FiscalizeResultModel ToResult(FiscalRecord record) => new()
        {
            DocumentId = record.DocumentId,
            DocumentNumber = record.DocumentNumber,
            DocumentType = record.DocumentType,
            VerificationCode = record.VerificationCode,
            QrCodeUrl = record.QrCodeUrl,
            ReceiptNumber = record.ReceiptNumber,
            GlobalNumber = record.GlobalNumber,
            DeviceId = record.DeviceId,
            FiscalDayNumber = record.FiscalDayNumber,
            ValidationError = record.ValidationError,
            FiscalizedAt = record.FiscalizedAt
        };
    }
}
