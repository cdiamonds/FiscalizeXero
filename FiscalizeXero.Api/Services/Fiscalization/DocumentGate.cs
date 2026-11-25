using FiscalizeXero.Xero.Models;

namespace FiscalizeXero.Api.Services.Fiscalization
{
    /// <summary>
    /// The document is not something to fiscalise at all (a draft, a purchase bill, a voided invoice), as opposed to
    /// a sale that failed to fiscalise. It is an <see cref="InvalidOperationException"/> so a person who asked for it
    /// gets a 400 with the message; automatic triggers record it as Skipped, never as a failure.
    /// </summary>
    public sealed class DocumentNotFiscalisableException : FiscalizeXero.Api.Exceptions.UserFacingException
    {
        public DocumentNotFiscalisableException(string message) : base(message) { }
    }

    /// <summary>
    /// Decides whether a Xero document is a real sale. Only approved sales invoices (type ACCREC) and approved
    /// customer credit notes (type ACCRECCREDIT) are; "approved" means status AUTHORISED or PAID. Purchase bills
    /// share Xero's invoice endpoints and webhook category and are always refused.
    /// </summary>
    public static class DocumentGate
    {
        /// <summary>Null when the invoice may be fiscalised; otherwise why not, and what to do in Xero.</summary>
        public static string? WhyNot(Invoice invoice)
        {
            var number = Name(invoice);
            if (!invoice.IsSale)
                return string.Equals(invoice.Type, "ACCPAY", StringComparison.OrdinalIgnoreCase)
                    ? $"{number} is a purchase bill in Xero, not a sales invoice. Only sales invoices are fiscalised."
                    : $"{number} is not a sales invoice in Xero (type {invoice.Type ?? "unknown"}). Only sales invoices are fiscalised.";
            return WhyNotApproved(invoice.Status, "Invoice", number);
        }

        /// <summary>Null when the credit note may be fiscalised; otherwise why not, and what to do in Xero.</summary>
        public static string? WhyNot(CreditNote creditNote)
        {
            var number = Name(creditNote);
            if (!creditNote.IsCustomerCredit)
                return string.Equals(creditNote.Type, "ACCPAYCREDIT", StringComparison.OrdinalIgnoreCase)
                    ? $"{number} is a supplier credit note in Xero, not a customer credit note. Only customer credit notes are fiscalised."
                    : $"{number} is not a customer credit note in Xero (type {creditNote.Type ?? "unknown"}). Only customer credit notes are fiscalised.";
            return WhyNotApproved(creditNote.Status, "Credit note", number);
        }

        public static void Ensure(Invoice invoice)
        {
            if (WhyNot(invoice) is { } reason) throw new DocumentNotFiscalisableException(reason);
        }

        public static void Ensure(CreditNote creditNote)
        {
            if (WhyNot(creditNote) is { } reason) throw new DocumentNotFiscalisableException(reason);
        }

        private static string? WhyNotApproved(string? status, string what, string number)
        {
            if (XeroStatus.IsApproved(status)) return null;
            var s = (status ?? string.Empty).Trim().ToUpperInvariant();
            return s switch
            {
                XeroStatus.Draft => $"{what} {number} is still a draft in Xero. Approve it in Xero, then fiscalise it.",
                XeroStatus.Submitted => $"{what} {number} is awaiting approval in Xero. Approve it in Xero, then fiscalise it.",
                XeroStatus.Voided => $"{what} {number} has been voided in Xero, so it is not fiscalised. Create and approve a new one in Xero if the sale stands.",
                XeroStatus.Deleted => $"{what} {number} has been deleted in Xero, so it is not fiscalised.",
                _ => $"{what} {number} has status {(s.Length == 0 ? "unknown" : s)} in Xero. Only approved documents (Awaiting Payment or Paid) are fiscalised; approve it in Xero first."
            };
        }

        private static string Name(SalesDocument document) =>
            string.IsNullOrWhiteSpace(document.DocumentNumber) ? document.DocumentId.ToString("D") : document.DocumentNumber!.Trim();
    }
}
