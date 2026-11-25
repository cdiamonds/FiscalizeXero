namespace FiscalizeXero.Shared.Models.Fiscalize
{
    /// <summary>A QR code image the browser can show directly (<c>data:image/png;base64,...</c>).</summary>
    public class QrImageModel
    {
        public string DataUri { get; set; } = string.Empty;
    }
}
