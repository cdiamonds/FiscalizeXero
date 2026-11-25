using System.Text.Json.Serialization;

namespace FiscalizeXero.Shared.Models.Fiscalize
{
    /// <summary>
    /// How strictly buyer details are checked when a customer has a TIN (a B2B sale). Customers without a TIN are
    /// fiscalised as cash sales either way. Sent over the API as a string, as in the Zoho Books integration.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum FiscalizationValidationMode
    {
        /// <summary>
        /// The default. Every buyer detail ZIMRA asks for with a TIN (phone, email, house/building number, street,
        /// city, province) must be found on the customer, or fiscalisation stops and says exactly what to add.
        /// </summary>
        Strict = 0,

        /// <summary>
        /// Missing buyer details are filled with standard placeholders so the document still goes through.
        /// An invalid TIN or VAT number is never patched over, in either mode.
        /// </summary>
        Lenient = 1
    }
}
