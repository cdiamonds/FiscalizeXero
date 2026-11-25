using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using QRCoder;

namespace FiscalizeXero.Api.Services.PdfStamp
{
    /// <summary>
    /// Draws the ZIMRA fiscal stamp: a white card with the QR code on the left and, on the right, a green
    /// FISCALIZED badge, the receipt details in a label/value grid, the verification code and the FDMS host to
    /// verify at. A rejected receipt (validation colour Red) gets the same card in red, reading INVALID, with the
    /// QR washed out. Same look as the desktop plugins' stamp so a document is recognisable whichever product
    /// fiscalised it.
    /// </summary>
    public class PdfStampService : IPdfStampService
    {
        private readonly ILogger<PdfStampService> _logger;

        private const double Mm2Pt = 72.0 / 25.4;

        // Geometry (points)
        private const double Pad   = 9;
        private const double Gap   = 10;
        private const double QrSz  = 72;    // ~25 mm
        private const double TextW = 150;
        private const double LineH = 10;
        private const double W     = Pad + QrSz + Gap + TextW + Pad;  // 250 pt ≈ 88 mm
        private const double H     = 104;                             // ≈ 37 mm

        // Palette
        private static readonly XColor Green     = XColor.FromArgb(22, 128, 61);    // badge + verify line
        private static readonly XColor Red       = XColor.FromArgb(192, 28, 28);
        private static readonly XColor Ink       = XColor.FromArgb(31, 41, 55);     // values
        private static readonly XColor Muted     = XColor.FromArgb(100, 116, 139);  // labels
        private static readonly XColor Border    = XColor.FromArgb(213, 219, 227);
        private static readonly XColor QrFrame   = XColor.FromArgb(226, 232, 240);

        static PdfStampService()
        {
            // Fonts ship inside the assembly (see EmbeddedFontResolver). PDFsharp lets the resolver be set only
            // once per process, hence the static constructor.
            if (GlobalFontSettings.FontResolver is null)
                GlobalFontSettings.FontResolver = new EmbeddedFontResolver();
        }

        public PdfStampService(ILogger<PdfStampService> logger) => _logger = logger;

        public byte[] ApplyFiscalStamp(byte[] pdfBytes, StampOptions opts)
        {
            _logger.LogInformation("Applying fiscal stamp: position={Position} invalid={Invalid}", opts.Position, opts.IsInvalid);

            using var qrGen = new QRCodeGenerator();
            using var qr = qrGen.CreateQrCode(opts.QrlUrl, QRCodeGenerator.ECCLevel.M);

            using var inputStream = new MemoryStream(pdfBytes);
            var source = PdfReader.Open(inputStream, PdfDocumentOpenMode.Import);

            var output = new PdfDocument();
            for (int i = 0; i < source.PageCount; i++)
                output.AddPage(source.Pages[i]);

            bool isBottom = opts.Position.StartsWith("bottom", StringComparison.OrdinalIgnoreCase);
            var page = isBottom ? output.Pages[output.PageCount - 1] : output.Pages[0];

            using var gfx = XGraphics.FromPdfPage(page);
            gfx.SmoothingMode = XSmoothingMode.HighQuality;

            double ml = opts.MarginLeft   * Mm2Pt;
            double mr = opts.MarginRight  * Mm2Pt;
            double mt = opts.MarginTop    * Mm2Pt;
            double mb = opts.MarginBottom * Mm2Pt;

            var (sx, sy) = CalcPosition(opts.Position, page.Width.Point, page.Height.Point, ml, mr, mt, mb);

            DrawStamp(gfx, sx, sy, qr, opts);

            using var ms = new MemoryStream();
            output.Save(ms);
            _logger.LogInformation("Stamp applied at ({X:F0},{Y:F0})", sx, sy);
            return ms.ToArray();
        }

        private static void DrawStamp(XGraphics g, double x, double y, QRCodeData qr, StampOptions opts)
        {
            var accent = opts.IsInvalid ? Red : Green;
            var accentBrush = new XSolidBrush(accent);
            var inkBrush    = new XSolidBrush(Ink);
            var mutedBrush  = new XSolidBrush(Muted);

            var badgeFont  = new XFont("Arial", 6.5, XFontStyleEx.Bold);
            var labelFont  = new XFont("Arial", 6.5, XFontStyleEx.Regular);
            var valueFont  = new XFont("Arial", 6.5, XFontStyleEx.Bold);
            var codeFont   = new XFont("Courier New", 7, XFontStyleEx.Regular);
            var verifyFont = new XFont("Arial", 5.5, XFontStyleEx.Regular);

            // Card: white, hairline border, soft corners. Opaque so it reads cleanly over anything underneath.
            g.DrawRoundedRectangle(new XPen(Border, 0.6), XBrushes.White, x, y, W, H, 5, 5);

            // QR code, left, vertically centred, inside a light frame.
            double qx = x + Pad, qy = y + (H - QrSz) / 2.0;
            g.DrawRoundedRectangle(new XPen(QrFrame, 0.6), XBrushes.White, qx - 2.5, qy - 2.5, QrSz + 5, QrSz + 5, 3, 3);
            DrawQr(g, qr, qx, qy, QrSz);
            if (opts.IsInvalid) DrawInvalidOverlay(g, qx, qy, QrSz, accentBrush);

            // Right column
            double tx = qx + QrSz + Gap;
            double ty = y + Pad;

            // Badge
            var badgeText = opts.IsInvalid ? "INVALID" : "FISCALIZED";
            var badgeSize = g.MeasureString(badgeText, badgeFont);
            double badgeW = badgeSize.Width + 14, badgeH = 12;
            g.DrawRoundedRectangle(accentBrush, tx, ty, badgeW, badgeH, badgeH, badgeH);
            g.DrawString(badgeText, badgeFont, XBrushes.White, new XRect(tx, ty, badgeW, badgeH - 0.5), XStringFormats.Center);
            ty += badgeH + 7;

            // Label / value grid. Values line up in one column so the block reads like a table.
            var rows = new List<(string Label, string Value)>();
            void Add(string label, string? value) { if (!string.IsNullOrWhiteSpace(value)) rows.Add((label, value!)); }
            Add("Receipt No", opts.ReceiptNumber);
            Add("Global No", opts.GlobalNumber);
            Add("Device ID", opts.DeviceId);
            Add("Fiscal Day", opts.DayNumber);
            Add("Date", opts.FiscalizedAt?.ToString("dd MMM yyyy HH:mm"));

            double labelCol = rows.Count == 0 ? 0 : rows.Max(r => g.MeasureString(r.Label, labelFont).Width) + 8;
            foreach (var (label, value) in rows)
            {
                g.DrawString(label, labelFont, mutedBrush, tx, ty + 6);
                g.DrawString(value, valueFont, inkBrush, tx + labelCol, ty + 6);
                ty += LineH;
            }

            // Verification code in monospace, then where to check it.
            ty += 2;
            if (!string.IsNullOrWhiteSpace(opts.VerificationCode))
            {
                g.DrawString(opts.VerificationCode, codeFont, inkBrush, tx, ty + 6);
                ty += LineH;
            }
            var host = ExtractDomain(opts.QrlUrl);
            if (!string.IsNullOrEmpty(host))
                g.DrawString(opts.IsInvalid ? "Rejected by ZIMRA. Not a fiscal document." : $"Verify at {host}",
                    verifyFont, accentBrush, tx, ty + 6);
        }

        /// <summary>
        /// Translucent red wash with a rotated INVALID band across the QR, so a reprint of a rejected receipt
        /// is unmistakable to the recipient.
        /// </summary>
        private static void DrawInvalidOverlay(XGraphics g, double qx, double qy, double size, XBrush redBrush)
        {
            var state = g.Save();
            try
            {
                g.DrawRectangle(new XSolidBrush(XColor.FromArgb(110, 192, 28, 28)), qx, qy, size, size);

                g.TranslateTransform(qx + size / 2, qy + size / 2);
                g.RotateTransform(-30);

                var font = new XFont("Arial", 13, XFontStyleEx.Bold);
                const string text = "INVALID";
                var sz = g.MeasureString(text, font);
                double bw = sz.Width + 10, bh = sz.Height + 2;
                g.DrawRectangle(new XPen(Red, 1), new XSolidBrush(XColor.FromArgb(225, 255, 255, 255)), -bw / 2, -bh / 2, bw, bh);
                g.DrawString(text, font, redBrush, new XRect(-bw / 2, -bh / 2, bw, bh), XStringFormats.Center);
            }
            finally { g.Restore(state); }
        }

        private static (double X, double Y) CalcPosition(
            string pos, double pw, double ph, double ml, double mr, double mt, double mb)
        {
            return pos switch
            {
                "top-left"      => (ml, mt),
                "top-center"    => ((pw - W) / 2, mt),
                "top-right"     => (pw - W - mr, mt),
                "bottom-left"   => (ml, ph - H - mb),
                "bottom-center" => ((pw - W) / 2, ph - H - mb),
                _               => (pw - W - mr, ph - H - mb),
            };
        }

        private static string ExtractDomain(string url)
        {
            try { return new Uri(url).Host; }
            catch { return url; }
        }

        /// <summary>
        /// Draws the QR code as vector rectangles straight from the module matrix. No raster image is involved, so
        /// the result is crisp at any zoom and does not depend on an image decoder (PDFsharp's core build has none
        /// for the PNGs QRCoder writes).
        /// </summary>
        private static void DrawQr(XGraphics g, QRCodeData qr, double x, double y, double size)
        {
            var modules = qr.ModuleMatrix; // includes QRCoder's 4-module quiet zone
            int n = modules.Count;
            if (n == 0) return;
            double cell = size / n;

            g.DrawRectangle(XBrushes.White, x, y, size, size);
            for (int row = 0; row < n; row++)
            {
                var bits = modules[row];
                int col = 0;
                while (col < n)
                {
                    if (!bits[col]) { col++; continue; }
                    // Merge consecutive dark modules into one rectangle: fewer PDF ops and no hairline seams.
                    int start = col;
                    while (col < n && bits[col]) col++;
                    g.DrawRectangle(XBrushes.Black, x + start * cell, y + row * cell, (col - start) * cell, cell);
                }
            }
        }
    }
}
