using FiscalizeXero.Api.Data;
using FiscalizeXero.Api.Repositories;
using FiscalizeXero.Api.Services.Fiscalization;
using FiscalizeXero.Xero.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FiscalizeXero.Api.Tests
{
    /// <summary>A Xero credit note names its original invoice only through its allocations.</summary>
    public class CreditNoteOriginalInvoiceTests
    {
        private static readonly Guid Inv1 = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
        private static readonly Guid Inv2 = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");
        private static readonly Guid Inv3 = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000003");
        private const int Company = 7;

        private static Allocation To(Guid invoice, string? number, decimal amount) =>
            new() { Amount = amount, Invoice = new AllocationInvoice { InvoiceID = invoice, InvoiceNumber = number } };

        private static CreditNote Note(params Allocation[] allocations) => new()
        {
            CreditNoteID = Guid.NewGuid(), CreditNoteNumber = "CN-7", Type = "ACCRECCREDIT", Status = "AUTHORISED", Allocations = allocations.ToList()
        };

        [Fact]
        public void One_allocated_invoice_is_the_original()
        {
            var r = CreditNoteOriginalInvoice.Resolve(Note(To(Inv1, "INV-1", 50m)))!;
            Assert.Equal((Inv1, "INV-1", 50m), (r.InvoiceId, r.InvoiceNumber, r.Allocated));
        }

        [Fact]
        public void Several_allocations_to_one_invoice_are_still_one_invoice()
        {
            var candidates = CreditNoteOriginalInvoice.Candidates(Note(To(Inv1, "INV-1", 20m), To(Inv1, "INV-1", 30m)));
            Assert.Single(candidates);
            Assert.Equal(50m, candidates[0].Allocated);
        }

        [Fact]
        public void Several_invoices_the_largest_total_allocation_wins()
        {
            // INV-2 has the single largest allocation, but INV-1's two add up to more.
            var note = Note(To(Inv1, "INV-1", 30m), To(Inv2, "INV-2", 40m), To(Inv1, "INV-1", 25m), To(Inv3, "INV-3", 5m));
            Assert.Equal("INV-1", CreditNoteOriginalInvoice.Resolve(note)!.InvoiceNumber);
            Assert.Equal(new[] { "INV-1", "INV-2", "INV-3" }, CreditNoteOriginalInvoice.Candidates(note).Select(c => c.InvoiceNumber));
        }

        [Fact]
        public void A_tie_goes_to_the_invoice_Xero_lists_first()
            => Assert.Equal("INV-2", CreditNoteOriginalInvoice.Resolve(Note(To(Inv2, "INV-2", 10m), To(Inv1, "INV-1", 10m)))!.InvoiceNumber);

        [Fact]
        public void Unallocated_has_no_original_and_the_message_says_what_to_do()
        {
            Assert.Null(CreditNoteOriginalInvoice.Resolve(Note()));
            Assert.Null(CreditNoteOriginalInvoice.Resolve(new CreditNote { Allocations = null }));
            Assert.Null(CreditNoteOriginalInvoice.Resolve(Note(new Allocation { Amount = 5m, Invoice = null })));
            var message = CreditNoteOriginalInvoice.UnknownMessage("CN-7");
            Assert.Contains("CN-7", message);
            Assert.Contains("Allocate the credit to the invoice in Xero", message);
            Assert.Contains("choose the invoice", message);
        }

        // ---------- with the fiscal records: the original must be an invoice fiscalised here ----------

        private static DataContext NewDb() =>
            new(new DbContextOptionsBuilder<DataContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        private static FiscalizationService Service(DataContext db) => new(
            null!, null!, null!, null!, null!, null!, null!, null!, new FiscalRecordRepository(db), null!, null!, NullLogger<FiscalizationService>.Instance);

        private static void Fiscalised(DataContext db, Guid id, string number, string type = FiscalDocumentType.Invoice, long? receipt = 100, int company = Company)
        {
            db.FiscalRecords.Add(new FiscalRecord { AppUserCompanyId = company, DocumentId = FiscalDocumentType.Key(id), DocumentType = type, DocumentNumber = number, ReceiptNumber = receipt });
            db.SaveChanges();
        }

        [Fact]
        public async Task The_number_the_caller_chose_wins_over_the_allocations()
        {
            using var db = NewDb();
            Fiscalised(db, Inv1, "INV-1");
            Fiscalised(db, Inv2, "INV-2");
            Assert.Equal("INV-2", await Service(db).ResolveBaseDocNoAsync(Company, Note(To(Inv1, "INV-1", 50m)), " INV-2 "));
        }

        [Fact]
        public async Task The_allocated_invoice_is_used_when_nothing_was_chosen()
        {
            using var db = NewDb();
            Fiscalised(db, Inv1, "INV-1");
            Assert.Equal("INV-1", await Service(db).ResolveBaseDocNoAsync(Company, Note(To(Inv1, "INV-1", 50m)), null));
        }

        [Fact]
        public async Task The_allocated_invoice_is_found_by_its_id_even_if_it_was_renumbered_in_Xero()
        {
            using var db = NewDb();
            Fiscalised(db, Inv1, "INV-1");   // fiscalised as INV-1; Xero now calls it INV-1A
            Assert.Equal("INV-1", await Service(db).ResolveBaseDocNoAsync(Company, Note(To(Inv1, "INV-1A", 50m)), null));
        }

        [Fact]
        public async Task Unallocated_and_nothing_chosen_is_a_clear_error()
        {
            using var db = NewDb();
            var ex = await Assert.ThrowsAsync<FiscalizeXero.Api.Exceptions.UserFacingException>(() => Service(db).ResolveBaseDocNoAsync(Company, Note(), null));
            Assert.Contains("has not been allocated to an invoice in Xero", ex.Message);
            Assert.Equal(FiscalFailureCategory.OriginalSale, FiscalFailureService.Classify(ex).Category);
        }

        [Fact]
        public async Task The_original_must_be_fiscalised_here()
        {
            using var db = NewDb();
            Fiscalised(db, Inv2, "INV-2", receipt: null);                     // a record without a receipt is not fiscalised
            Fiscalised(db, Inv3, "INV-3", company: 99);                       // another company's record never counts
            Fiscalised(db, Guid.NewGuid(), "CN-1", FiscalDocumentType.CreditNote); // a credit note is not an invoice
            var service = Service(db);

            foreach (var (note, chosen) in new (CreditNote, string?)[]
                     {
                         (Note(To(Inv1, "INV-1", 50m)), null), (Note(To(Inv2, "INV-2", 50m)), null), (Note(To(Inv3, "INV-3", 50m)), null),
                         (Note(), "INV-3"), (Note(), "CN-1"), (Note(), "NOPE")
                     })
            {
                var ex = await Assert.ThrowsAsync<FiscalizeXero.Api.Exceptions.UserFacingException>(() => service.ResolveBaseDocNoAsync(Company, note, chosen));
                Assert.Contains("has not been fiscalized here", ex.Message);
            }
        }
    }

    /// <summary>Only approved ACCREC invoices and ACCRECCREDIT credit notes are sales.</summary>
    public class DocumentGateTests
    {
        private static Invoice Inv(string? type, string? status) => new() { InvoiceID = Guid.NewGuid(), InvoiceNumber = "INV-9", Type = type, Status = status };
        private static CreditNote Cn(string? type, string? status) => new() { CreditNoteID = Guid.NewGuid(), CreditNoteNumber = "CN-9", Type = type, Status = status };

        [Theory]
        [InlineData("AUTHORISED")]
        [InlineData("PAID")]
        [InlineData("paid")]
        public void Approved_sales_documents_pass(string status)
        {
            Assert.Null(DocumentGate.WhyNot(Inv("ACCREC", status)));
            Assert.Null(DocumentGate.WhyNot(Cn("ACCRECCREDIT", status)));
            DocumentGate.Ensure(Inv("ACCREC", status));
        }

        [Theory]
        [InlineData("DRAFT", "Approve it in Xero")]
        [InlineData("SUBMITTED", "Approve it in Xero")]
        [InlineData("VOIDED", "voided in Xero")]
        [InlineData("DELETED", "deleted in Xero")]
        [InlineData("SOMETHING", "approve it in Xero first")]
        [InlineData(null, "approve it in Xero first")]
        public void Anything_not_approved_is_refused_naming_the_document_and_what_to_do(string? status, string advice)
        {
            var invoice = DocumentGate.WhyNot(Inv("ACCREC", status))!;
            Assert.Contains("INV-9", invoice);
            Assert.Contains(advice, invoice);
            var credit = DocumentGate.WhyNot(Cn("ACCRECCREDIT", status))!;
            Assert.Contains("Credit note CN-9", credit);
            Assert.Throws<DocumentNotFiscalisableException>(() => DocumentGate.Ensure(Inv("ACCREC", status)));
            Assert.Throws<DocumentNotFiscalisableException>(() => DocumentGate.Ensure(Cn("ACCRECCREDIT", status)));
        }

        [Theory]
        [InlineData("ACCPAY", "purchase bill")]
        [InlineData("ACCPAYCREDIT", "not a sales invoice")]
        [InlineData(null, "not a sales invoice")]
        public void Purchase_bills_are_always_refused_even_when_approved(string? type, string words)
            => Assert.Contains(words, DocumentGate.WhyNot(Inv(type, "AUTHORISED")));

        [Theory]
        [InlineData("ACCPAYCREDIT", "supplier credit note")]
        [InlineData("ACCREC", "not a customer credit note")]
        public void Supplier_credits_are_always_refused_even_when_approved(string type, string words)
            => Assert.Contains(words, DocumentGate.WhyNot(Cn(type, "PAID")));

        [Fact]
        public void A_refusal_is_a_bad_request_for_a_person_and_never_a_recorded_failure()
        {
            var ex = Assert.Throws<DocumentNotFiscalisableException>(() => DocumentGate.Ensure(Inv("ACCREC", "DRAFT")));
            Assert.IsAssignableFrom<InvalidOperationException>(ex);
        }
    }

    /// <summary>The fiscal marker in a Xero document's Reference.</summary>
    public class FiscalReferenceMarkerTests
    {
        private static FiscalRecord Record(long? receipt = 1024, string? code = "A1B2-C3D4-E5F6-G7H8") => new() { ReceiptNumber = receipt, VerificationCode = code };

        [Fact]
        public void Appended_after_what_the_customer_had()
            => Assert.Equal("PO-4471 [ZIMRA 1024/A1B2-C3D4-E5F6-G7H8]", FiscalReferenceMarker.Add("PO-4471", Record()));

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void An_empty_reference_becomes_just_the_marker(string? existing)
            => Assert.Equal("[ZIMRA 1024/A1B2-C3D4-E5F6-G7H8]", FiscalReferenceMarker.Add(existing, Record()));

        [Fact]
        public void A_refiscalisation_replaces_the_marker_instead_of_stacking()
        {
            var first = FiscalReferenceMarker.Add("PO-4471", Record())!;
            var second = FiscalReferenceMarker.Add(first, Record(2048, "ZZZZ-1111"))!;
            Assert.Equal("PO-4471 [ZIMRA 2048/ZZZZ-1111]", second);
        }

        [Fact]
        public void Nothing_to_write_when_it_is_already_there_or_there_is_no_receipt()
        {
            var first = FiscalReferenceMarker.Add("PO-4471", Record())!;
            Assert.Null(FiscalReferenceMarker.Add(first, Record()));
            Assert.Null(FiscalReferenceMarker.Add("PO-4471", Record(receipt: null)));
        }

        [Fact]
        public void Never_longer_than_255_and_the_customers_text_is_never_cut()
        {
            var own = new string('x', 235);                       // room for the short marker only
            var shortened = FiscalReferenceMarker.Add(own, Record())!;
            Assert.Equal(own + " [ZIMRA 1024]", shortened);
            Assert.True(shortened.Length <= FiscalReferenceMarker.MaxReferenceLength);

            Assert.Null(FiscalReferenceMarker.Add(new string('x', 250), Record())); // no room at all: left as it is

            var exact = new string('y', 255 - " [ZIMRA 1024/A1B2-C3D4-E5F6-G7H8]".Length);
            Assert.Equal(255, FiscalReferenceMarker.Add(exact, Record())!.Length);
        }

        [Fact]
        public void Removed_on_fiscal_cancel_leaving_the_customers_text()
        {
            Assert.Equal("PO-4471", FiscalReferenceMarker.Remove("PO-4471 [ZIMRA 1024/A1B2-C3D4-E5F6-G7H8]"));
            Assert.Equal("", FiscalReferenceMarker.Remove("[ZIMRA 1024]"));          // the marker was all there was
            Assert.Equal("PO [x] 1", FiscalReferenceMarker.Remove("PO [x] 1 [ZIMRA 7]")); // other brackets are the customer's
        }

        [Fact]
        public void Nothing_to_remove_when_we_never_added_one()
        {
            Assert.Null(FiscalReferenceMarker.Remove("PO-4471"));
            Assert.Null(FiscalReferenceMarker.Remove(null));
            Assert.Null(FiscalReferenceMarker.Remove("ZIMRA 1024"));
        }

        [Fact]
        public void The_marker_never_reaches_the_platform_as_the_customers_reference()
            => Assert.Equal("PO-4471", FiscalReferenceMarker.Strip("PO-4471 [ZIMRA 1024/A1B2]"));
    }

    public class FiscalHistoryNoteTests
    {
        private static readonly FiscalRecord Record = new()
        {
            DocumentType = FiscalDocumentType.Invoice, DocumentNumber = "INV-0042", ReceiptNumber = 1024, GlobalNumber = 55001, DeviceId = 36822,
            FiscalDayNumber = 91, VerificationCode = "A1B2-C3D4", QrCodeUrl = "https://fdms.zimra.co.zw/v/abc", FiscalizedAt = new DateTime(2026, 11, 23, 9, 30, 0, DateTimeKind.Utc)
        };

        [Fact]
        public void Carries_every_part_of_the_fiscal_result()
        {
            var note = FiscalHistoryNote.Fiscalised(Record);
            foreach (var part in new[] { "INV-0042", "Receipt no. 1024", "Global no. 55001", "Device 36822", "Fiscal day 91", "Verification code A1B2-C3D4", "https://fdms.zimra.co.zw/v/abc", "2026-11-23 09:30" })
                Assert.Contains(part, note);
            Assert.True(note.Length < 2500);
        }

        [Fact]
        public void Cancellation_says_the_receipt_was_cancelled()
        {
            var note = FiscalHistoryNote.Cancelled(Record, new DateTime(2026, 11, 24, 8, 0, 0, DateTimeKind.Utc));
            Assert.Contains("receipt no. 1024", note);
            Assert.Contains("was cancelled", note);
            Assert.Contains("INV-0042", note);
        }
    }
}
