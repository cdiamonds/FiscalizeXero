using FiscalizeXero.Api.Data;
using FiscalizeXero.Api.Services.FiscalDocuments;
using Xunit;

namespace FiscalizeXero.Api.Tests
{
    public class FiscalDocumentNamingTests
    {
        [Theory]
        [InlineData("INV-1001", "invoice", "INV-1001-fiscal.pdf")]
        [InlineData("1001/A B", "invoice", "1001-A-B-fiscal.pdf")]
        [InlineData("", "invoice", "invoice-fiscal.pdf")]
        [InlineData("", "creditnote", "credit-note-fiscal.pdf")]
        [InlineData("CN-77", "creditnote", "CN-77-fiscal.pdf")]
        [InlineData("../../etc", "invoice", "etc-fiscal.pdf")]
        public void FileNamesAreSafeAndRecognisable(string docNumber, string type, string expected)
        {
            var name = FiscalDocumentNaming.FileNameFor(new FiscalRecord { DocumentNumber = docNumber, DocumentType = type });
            Assert.Equal(expected, name);
        }

        [Fact]
        public void TheSameDocumentAlwaysGetsTheSameNameSoARefiscalisationReplacesTheFile()
        {
            var first = new FiscalRecord { DocumentNumber = "INV-1001", DocumentType = "invoice", ReceiptNumber = 1 };
            var again = new FiscalRecord { DocumentNumber = "INV-1001", DocumentType = "invoice", ReceiptNumber = 2, FiscalizedAt = DateTime.UtcNow.AddDays(3) };
            Assert.Equal(FiscalDocumentNaming.FileNameFor(first), FiscalDocumentNaming.FileNameFor(again));
            Assert.True(FiscalDocumentNaming.FileNameFor(new FiscalRecord { DocumentNumber = new string('9', 255), DocumentType = "invoice" }).Length <= 100);
        }
    }
}
