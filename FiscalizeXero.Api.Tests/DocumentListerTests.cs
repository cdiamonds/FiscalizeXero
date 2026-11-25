using FiscalizeXero.Api.Services.Documents;
using FiscalizeXero.Xero.Models;
using FiscalizeXero.Xero.Services;
using Xunit;

namespace FiscalizeXero.Api.Tests
{
    public class DocumentListerTests
    {
        [Fact]
        public void Without_search_asks_Xero_for_one_page_newest_first_of_approved_documents()
        {
            var q = DocumentLister.BuildQuery(page: 3, pageSize: 25, search: null, status: null);
            Assert.Equal(3, q.Page);
            Assert.Equal(25, q.PageSize);
            Assert.Null(q.Search);
            Assert.Equal("Date DESC", q.Order);
            Assert.Equal(new[] { "AUTHORISED", "PAID" }, q.Statuses);

            var where = XeroQuery.Where(InvoiceService.SalesType, q, "InvoiceNumber");
            Assert.Equal("Type==\"ACCREC\"&&(Status==\"AUTHORISED\"||Status==\"PAID\")", where);
        }

        [Fact]
        public void Totals_come_from_Xero()
        {
            var q = DocumentLister.BuildQuery(3, 25, null, null);
            var page = DocumentLister.ToPage(new XeroPage<Invoice> { Items = { new Invoice(), new Invoice() }, ItemCount = 53 }, q);
            Assert.Equal(53, page.Total);
            Assert.Equal(3, page.TotalPages);
            Assert.Equal(3, page.Page);
            Assert.Equal(2, page.Items.Count);
            Assert.Equal(new[] { "AUTHORISED", "PAID" }, page.Statuses);
        }

        [Theory]
        [InlineData(0, 25)]
        [InlineData(-5, 25)]
        [InlineData(10, 10)]
        [InlineData(5000, 100)]
        public void Page_size_is_clamped(int asked, int expected) => Assert.Equal(expected, DocumentLister.ClampPageSize(asked));

        [Fact]
        public void Page_below_one_is_page_one()
        {
            var q = DocumentLister.BuildQuery(page: -2, pageSize: 10, search: "  ", status: null);
            Assert.Equal(1, q.Page);
            Assert.Null(q.Search);
            var page = DocumentLister.ToPage(new XeroPage<CreditNote>(), q);
            Assert.Equal(1, page.Page);
            Assert.Equal(1, page.TotalPages);
        }

        [Fact]
        public void Search_goes_to_Xero_on_number_reference_and_contact_with_quotes_escaped()
        {
            var q = DocumentLister.BuildQuery(1, 25, "  O\"Neil-1 ", null);
            Assert.Equal("O\"Neil-1", q.Search);
            var where = XeroQuery.Where(CreditNoteService.CustomerCreditType, q, "CreditNoteNumber");
            Assert.StartsWith("Type==\"ACCRECCREDIT\"&&", where);
            Assert.Contains("CreditNoteNumber.ToLower().Contains(\"o\"\"neil-1\")", where);
            Assert.Contains("Reference.ToLower().Contains(", where);
            Assert.Contains("Contact.Name.ToLower().Contains(", where);
        }

        [Theory]
        [InlineData(null, "AUTHORISED,PAID")]
        [InlineData("", "AUTHORISED,PAID")]
        [InlineData("all", "")]
        [InlineData("draft", "DRAFT")]
        [InlineData("paid, voided", "PAID,VOIDED")]
        [InlineData("nonsense", "AUTHORISED,PAID")]   // a typo never widens the list
        [InlineData("paid,\"||Type==\"ACCPAY", "PAID")] // nothing but known status names reaches the filter
        public void Statuses_default_to_approved(string? status, string expected)
            => Assert.Equal(expected, string.Join(",", DocumentLister.ParseStatuses(status)));

        [Fact]
        public void Purchase_bills_can_never_be_listed()
        {
            foreach (var status in new[] { null, "all", "draft" })
                Assert.StartsWith("Type==\"ACCREC\"", XeroQuery.Where(InvoiceService.SalesType, DocumentLister.BuildQuery(1, 25, "x", status), "InvoiceNumber"));
        }

        [Fact]
        public void In_memory_lists_are_paged_the_same_way()
        {
            var all = Enumerable.Range(1, 53).ToList();
            var page = DocumentLister.PageOf(all, 3, 25, " milk ");
            Assert.Equal(new[] { 51, 52, 53 }, page.Items);
            Assert.Equal((53, 3, 3, "milk"), (page.Total, page.TotalPages, page.Page, page.Search));
            Assert.Equal(1, DocumentLister.PageOf(new List<int>(), 0, 0, null).TotalPages);
        }

        [Fact]
        public void Search_text_is_trimmed_and_capped()
        {
            Assert.Null(DocumentLister.NormaliseSearch("   "));
            Assert.Equal("abc", DocumentLister.NormaliseSearch("  abc "));
            Assert.Equal(DocumentLister.MaxSearchLength, DocumentLister.NormaliseSearch(new string('x', 500))!.Length);
        }
    }
}
