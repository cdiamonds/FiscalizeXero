using FiscalizeXero.Api.Services.PdfStamp;
using Xunit;

namespace FiscalizeXero.Api.Tests
{
    public class QrImageTests
    {
        private const string FdmsLink = "https://fdms.zimra.co.zw/28994/18112026/0000000553/EE73BE077A39EAB3";

        [Fact]
        public void DrawsAPngDataUri()
        {
            var uri = QrImage.PngDataUri(FdmsLink);
            Assert.StartsWith("data:image/png;base64,", uri);
            var png = Convert.FromBase64String(uri["data:image/png;base64,".Length..]);
            Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, png[..4]); // PNG signature
        }

        [Fact]
        public void SameLinkGivesSameImage() => Assert.Equal(QrImage.PngDataUri(FdmsLink), QrImage.PngDataUri(FdmsLink));

        [Theory]
        [InlineData(FdmsLink, true)]
        [InlineData("http://fdmstest.zimra.co.zw/x", true)]
        [InlineData("", false)]
        [InlineData(null, false)]
        [InlineData("not a link", false)]
        [InlineData("javascript:alert(1)", false)]
        [InlineData("ftp://example.com/x", false)]
        public void OnlyHttpLinksAreEncoded(string? data, bool expected) => Assert.Equal(expected, QrImage.IsEncodable(data));

        [Fact]
        public void OverlongTextRejected() => Assert.False(QrImage.IsEncodable("https://x.zw/" + new string('a', 2100)));
    }
}
