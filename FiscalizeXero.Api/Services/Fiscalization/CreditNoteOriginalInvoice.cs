using FiscalizeXero.Xero.Models;

namespace FiscalizeXero.Api.Services.Fiscalization
{
    /// <summary>
    /// Which invoice a Xero credit note reverses. Xero has no "original invoice" field: the only link is the credit
    /// note's allocations, each naming an invoice and an amount. There are none until the user allocates the credit,
    /// and there can be several.
    /// </summary>
    public static class CreditNoteOriginalInvoice
    {
        /// <summary>An invoice the credit was allocated to, with everything allocated to it added up.</summary>
        public sealed record Candidate(Guid InvoiceId, string? InvoiceNumber, decimal Allocated);

        /// <summary>Every invoice the credit note is allocated to, the largest total allocation first (Xero's order breaks ties).</summary>
        public static IReadOnlyList<Candidate> Candidates(CreditNote creditNote) =>
            (creditNote.Allocations ?? new List<Allocation>())
                .Where(a => a.Invoice != null && a.Invoice.InvoiceID != Guid.Empty)
                .GroupBy(a => a.Invoice!.InvoiceID)
                .Select((g, index) => (
                    Candidate: new Candidate(
                        g.Key,
                        g.Select(a => a.Invoice!.InvoiceNumber).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n))?.Trim(),
                        g.Sum(a => a.Amount ?? 0m)),
                    Index: index))
                .OrderByDescending(x => x.Candidate.Allocated)
                .ThenBy(x => x.Index)
                .Select(x => x.Candidate)
                .ToList();

        /// <summary>
        /// The original invoice: the only one the credit is allocated to, or, when it is spread over several, the one
        /// with the largest total allocated amount. Null while the credit note is unallocated.
        /// </summary>
        public static Candidate? Resolve(CreditNote creditNote) => Candidates(creditNote).FirstOrDefault();

        /// <summary>What to tell the user when a credit note has no allocation and no invoice was chosen.</summary>
        public static string UnknownMessage(string creditNoteNumber) =>
            $"Credit note {creditNoteNumber} has not been allocated to an invoice in Xero, so the original invoice is unknown. " +
            "Allocate the credit to the invoice in Xero, or open the credit note in Fiscalize and choose the invoice it reverses.";

        /// <summary>What to tell the user when the original invoice has no fiscal receipt here.</summary>
        public static string NotFiscalizedMessage(string invoiceNumber) =>
            $"Invoice {invoiceNumber} has not been fiscalized here. ZIMRA needs the original receipt, so fiscalize invoice {invoiceNumber} first, then the credit note.";
    }
}
