using FiscalizeXero.Api.Services.PdfStamp;
using Microsoft.Extensions.Logging.Abstractions;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using Xunit;

namespace FiscalizeXero.Api.Tests
{
    /// <summary>
    /// Guards the PDF stamping path end to end: PDFsharp opens the PDF, decodes the QR PNG, resolves the embedded
    /// Liberation fonts through EmbeddedFontResolver and writes the stamped document.
    /// </summary>
    public class PdfStampServiceTests
    {
        [Theory]
        [InlineData("bottom-right")]
        [InlineData("top-left")]
        public void StampsAQrCodeAndTextOntoThePdf(string position)
        {
            using var src = new MemoryStream();
            var doc = new PdfDocument();
            doc.AddPage();
            doc.AddPage();
            doc.Save(src);

            var svc = new PdfStampService(NullLogger<PdfStampService>.Instance);
            var stamped = svc.ApplyFiscalStamp(src.ToArray(),
                new StampOptions(position, 10, 10, 10, 10, "https://fdms.zimra.co.zw/verify?x=1", "42", "3", "1", "ABCD-1234-EFGH"));

            using var result = PdfReader.Open(new MemoryStream(stamped), PdfDocumentOpenMode.Import);
            Assert.Equal(2, result.PageCount);
            Assert.True(stamped.Length > src.Length, "the stamp (QR image + text) should add content");
        }
    }
}
