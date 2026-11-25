using QRCoder;

namespace FiscalizeXero.Api.Services.PdfStamp
{
    /// <summary>
    /// The on-screen QR code, drawn here with the same library and error-correction level as the QR on the stamped
    /// PDF, so the two always match and no outside QR service is involved.
    /// </summary>
    public static class QrImage
    {
        /// <summary>Longest text accepted; an FDMS verification link is about 150 characters.</summary>
        public const int MaxLength = 2048;

        /// <summary>True for an absolute http(s) link of reasonable length: the only thing the app encodes.</summary>
        public static bool IsEncodable(string? data)
            => !string.IsNullOrWhiteSpace(data)
               && data.Length <= MaxLength
               && Uri.TryCreate(data.Trim(), UriKind.Absolute, out var uri)
               && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);

        /// <summary>A PNG data URI (black on white, 4-module quiet zone) for <paramref name="data"/>.</summary>
        public static string PngDataUri(string data, int pixelsPerModule = 8)
        {
            using var generator = new QRCodeGenerator();
            using var qr = generator.CreateQrCode(data.Trim(), QRCodeGenerator.ECCLevel.M);
            var png = new PngByteQRCode(qr).GetGraphic(Math.Clamp(pixelsPerModule, 2, 20));
            return "data:image/png;base64," + Convert.ToBase64String(png);
        }
    }
}
