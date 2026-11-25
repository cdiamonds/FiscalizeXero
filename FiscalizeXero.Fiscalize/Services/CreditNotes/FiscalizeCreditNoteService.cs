using FiscalizeXero.Shared.Models.Auths;
using FiscalizeXero.Shared.Models.Fiscalize;

namespace FiscalizeXero.Fiscalize.Services.CreditNotes
{
    public class FiscalizeCreditNoteService
    {
        private const string Endpoint = "api/CreditNotes/Devices";

        public static async Task<FiscalizeInvoiceFiscalData> FiscalizeAsync(
            FiscalizeAuthModel auth,
            FiscalizeInvoiceRequest request)
        {
            return await RestClient.PostAsync<FiscalizeInvoiceRequest, FiscalizeInvoiceFiscalData>(
                auth, Endpoint, request);
        }

        /// <summary>
        /// Re-fiscalize an existing credit note by its document number.
        /// Calls PUT api/CreditNotes/Devices/{docNo} using the device bearer token.
        /// </summary>
        public static async Task<FiscalizeInvoiceFiscalData> RefiscalizeAsync(
            FiscalizeAuthModel auth,
            string docNo,
            FiscalizeInvoiceRequest request)
        {
            return await RestClient.PutAsync<FiscalizeInvoiceRequest, FiscalizeInvoiceFiscalData>(
                auth, $"{Endpoint}/{Uri.EscapeDataString(docNo)}", request);
        }

        public static async Task<FiscalizeInvoiceResponse> GetByIdAsync(
            FiscalizeAuthModel auth,
            long creditNoteId)
        {
            return await RestClient.GetAsync<FiscalizeInvoiceResponse>(
                auth, $"{Endpoint}/{creditNoteId}");
        }

        /// <summary>
        /// Download the PDF for a fiscalized credit note identified by its document number.
        /// Calls GET api/CreditNotes/Devices/{docNo}/pdf using the device bearer token.
        /// </summary>
        public static async Task<byte[]> GetPdfByDocNoAsync(FiscalizeAuthModel auth, string docNo)
        {
            return await RestClient.GetBytesAsync(auth, $"{Endpoint}/{Uri.EscapeDataString(docNo)}/pdf");
        }
    }
}
