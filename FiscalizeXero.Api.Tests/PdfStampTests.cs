using FiscalizeXero.Api.Services.PdfStamp;
using Microsoft.Extensions.Logging.Abstractions;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using Xunit;

namespace FiscalizeXero.Api.Tests
{
    public class PdfStampTests
    {
        private static readonly StampOptions Valid = new(
            Position: "bottom-right", MarginTop: 10, MarginBottom: 10, MarginLeft: 10, MarginRight: 10,
            QrlUrl: "https://fdmstest.zimra.co.zw/36822000000000000000003119674584B9A6B9596E1AAE9",
            ReceiptNumber: "11967458", DayNumber: "3", DeviceId: "36822", VerificationCode: "4B9A-6B95-96E1-AAE9",
            GlobalNumber: "11967458", FiscalizedAt: new DateTime(2026, 11, 17, 9, 42, 0, DateTimeKind.Utc));

        [Theory]
        [InlineData("bottom-right", false)]
        [InlineData("top-left", false)]
        [InlineData("bottom-center", true)]
        public void StampsEveryPositionAndState(string position, bool invalid)
        {
            var svc = new PdfStampService(NullLogger<PdfStampService>.Instance);
            var stamped = svc.ApplyFiscalStamp(SampleInvoice(), Valid with { Position = position, IsInvalid = invalid });

            using var doc = PdfReader.Open(new MemoryStream(stamped), PdfDocumentOpenMode.Import);
            Assert.Equal(1, doc.PageCount);
            Assert.True(stamped.Length > SampleInvoice().Length);

            // Lets a reviewer look at the result: STAMP_OUT=/some/dir dotnet test
            var outDir = Environment.GetEnvironmentVariable("STAMP_OUT");
            if (!string.IsNullOrEmpty(outDir))
                File.WriteAllBytes(Path.Combine(outDir, $"stamp-{position}-{(invalid ? "invalid" : "valid")}.pdf"), stamped);
        }

        [Fact]
        public void BottomPositionsStampTheLastPage()
        {
            var svc = new PdfStampService(NullLogger<PdfStampService>.Instance);
            var stamped = svc.ApplyFiscalStamp(SampleInvoice(pages: 2), Valid);
            using var doc = PdfReader.Open(new MemoryStream(stamped), PdfDocumentOpenMode.Import);
            Assert.Equal(2, doc.PageCount);
            Assert.True(doc.Pages[1].Contents.Elements.Count > doc.Pages[0].Contents.Elements.Count);
        }

        /// <summary>A plain invoice: enough to see the stamp sitting among real content.</summary>
        private static byte[] SampleInvoice(int pages = 1)
        {
            _ = new PdfStampService(NullLogger<PdfStampService>.Instance); // installs the embedded font resolver
            var doc = new PdfDocument();
            for (int p = 0; p < pages; p++)
            {
                var page = doc.AddPage();
                page.Width = XUnit.FromPoint(612); page.Height = XUnit.FromPoint(792);
                using var g = XGraphics.FromPdfPage(page);
                var h1 = new XFont("Arial", 20, XFontStyleEx.Bold);
                var body = new XFont("Arial", 9, XFontStyleEx.Regular);
                var bold = new XFont("Arial", 9, XFontStyleEx.Bold);
                g.DrawRectangle(new XSolidBrush(XColor.FromArgb(232, 240, 248)), 0, 0, 612, 150);
                g.DrawString("INVOICE", h1, XBrushes.Black, 40, 50);
                g.DrawString("Craig's Design and Landscaping Services", bold, XBrushes.Black, 40, 75);
                g.DrawString("Invoice no.: 1040    Terms: Net 30    Invoice date: 11/17/2026    Due date: 12/17/2026", body, XBrushes.Black, 40, 120);
                double y = 190;
                g.DrawString("#   Product or service        Description                                 Qty      Rate        Amount", bold, XBrushes.Black, 40, y);
                g.DrawLine(new XPen(XColors.Gray, 0.5), 40, y + 5, 572, y + 5);
                y += 22; g.DrawString("1.  Concrete                     Concrete for fountain installation        10     $45.00       $450.00", body, XBrushes.Black, 40, y);
                y += 18; g.DrawString("2.  Hours                                                                          4     $75.00       $300.00", body, XBrushes.Black, 40, y);
                g.DrawString("Subtotal   $750.00", body, XBrushes.Black, 420, 280);
                g.DrawString("Sales tax   $69.75", body, XBrushes.Black, 420, 296);
                g.DrawString("Total   $819.75", new XFont("Arial", 13, XFontStyleEx.Bold), XBrushes.Black, 420, 320);
                g.DrawString("Thank you for your business and have a great day!", body, XBrushes.Black, 40, 360);
            }
            using var ms = new MemoryStream();
            doc.Save(ms);
            return ms.ToArray();
        }
    }
}
