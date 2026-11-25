namespace FiscalizeXero.Api.Services.PdfStamp
{
    public record StampOptions(
        string Position,
        int MarginTop,
        int MarginBottom,
        int MarginLeft,
        int MarginRight,
        string QrlUrl,
        string? ReceiptNumber,
        string? DayNumber,
        string? DeviceId,
        string? VerificationCode,
        string? GlobalNumber = null,
        DateTime? FiscalizedAt = null,
        // IsInvalid: ZIMRA rejected the receipt (validation colour Red). The stamp turns red, reads INVALID and
        // the QR is washed out so a recipient can tell at a glance that the copy is not a fiscal document. Gray
        // and Yellow remarks leave the document valid and are not shown on the stamp.
        bool IsInvalid = false);

    public interface IPdfStampService
    {
        /// <summary>
        /// Overlays a fiscal verification stamp on the appropriate page of the PDF.
        /// Top positions → first page. Bottom positions → last page.
        /// </summary>
        byte[] ApplyFiscalStamp(byte[] pdfBytes, StampOptions opts);
    }
}
