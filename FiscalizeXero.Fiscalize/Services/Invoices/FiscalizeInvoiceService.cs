using FiscalizeXero.Shared.Models.Auths;
using FiscalizeXero.Shared.Models.Fiscalize;

namespace FiscalizeXero.Fiscalize.Services.Invoices
{
    /// <summary>
    /// Service for fiscalizing invoices via the Fiscalize API
    /// </summary>
    public class FiscalizeInvoiceService
    {
        private const string Endpoint = "api/Invoices/Devices";

        /// <summary>
        /// Fiscalize an invoice by sending it to the Fiscalize API
        /// </summary>
        /// <param name="auth">Fiscalize authentication model with API URL and bearer token</param>
        /// <param name="request">Invoice data to fiscalize</param>
        /// <returns>Fiscalized invoice response with fiscal data</returns>
        public static async Task<FiscalizeInvoiceFiscalData> FiscalizeAsync(
            FiscalizeAuthModel auth,
            FiscalizeInvoiceRequest request)
        {
            return await RestClient.PostAsync<FiscalizeInvoiceRequest, FiscalizeInvoiceFiscalData>(
                auth, Endpoint, request);
        }

        /// <summary>
        /// Get a fiscalized invoice by ID
        /// </summary>
        /// <param name="auth">Fiscalize authentication model</param>
        /// <param name="invoiceId">Fiscalize invoice ID</param>
        /// <returns>Invoice details</returns>
        public static async Task<FiscalizeInvoiceResponse> GetByIdAsync(
            FiscalizeAuthModel auth,
            long invoiceId)
        {
            return await RestClient.GetAsync<FiscalizeInvoiceResponse>(
                auth, $"{Endpoint}/{invoiceId}");
        }

        /// <summary>
        /// Re-fiscalize an existing invoice by resubmitting it.
        /// Calls POST api/Invoices/Devices/Resubmit using the device bearer token.
        /// The document number is carried in the request body.
        /// </summary>
        public static async Task<FiscalizeInvoiceFiscalData> RefiscalizeAsync(
            FiscalizeAuthModel auth,
            string docNo,
            FiscalizeInvoiceRequest request)
        {
            return await RestClient.PostAsync<FiscalizeInvoiceRequest, FiscalizeInvoiceFiscalData>(
                auth, $"{Endpoint}/Resubmit", request);
        }

        /// <summary>
        /// Download the PDF for a fiscalized invoice identified by its document number.
        /// Calls GET api/Invoices/Devices/{docNo}/pdf using the device bearer token.
        /// </summary>
        public static async Task<byte[]> GetPdfByDocNoAsync(FiscalizeAuthModel auth, string docNo)
        {
            return await RestClient.GetBytesAsync(auth, $"{Endpoint}/{Uri.EscapeDataString(docNo)}/pdf");
        }

        /// <summary>
        /// Fiscally cancel a fiscalized invoice by document number. The Fiscalize platform submits a credit note
        /// to FDMS to reverse the invoice and frees the document number. Calls
        /// POST api/Invoices/Devices/{docNo}/FiscalCancel using the device bearer token (204 on success).
        /// </summary>
        public static async Task FiscalCancelAsync(FiscalizeAuthModel auth, string docNo)
        {
            await RestClient.PostNoContentAsync(auth, $"{Endpoint}/{Uri.EscapeDataString(docNo)}/FiscalCancel", new { });
        }
    }
}
