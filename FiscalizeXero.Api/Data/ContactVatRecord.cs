using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FiscalizeXero.Api.Data
{
    public class ContactVatRecord
    {
        [Key]
        public long Id { get; set; }

        [ForeignKey("AppUserCompanyId")]
        public AppUserCompany AppUserCompany { get; set; } = null!;
        public int AppUserCompanyId { get; set; }

        /// <summary>The Xero ContactID (a GUID, lower case).</summary>
        [MaxLength(50)]
        public string ContactId { get; set; } = string.Empty;
        public string? VatNumber { get; set; }
        public string? Tin { get; set; }
        public string? RegisteredName { get; set; }
        public string? TradeName { get; set; }

        // Contact details typed in Fiscalize. Each one, when set, wins over the Xero contact, so users never
        // have to leave Fiscalize to make a customer ZIMRA-ready.
        // For every buyer value here: null = not set in Fiscalize (use Xero); "" = deliberately cleared (send
        // nothing, even if Xero still has a value).
        [MaxLength(30)] public string? Phone { get; set; }
        [MaxLength(200)] public string? Email { get; set; }
        [MaxLength(50)] public string? HouseNo { get; set; }
        [MaxLength(100)] public string? Street { get; set; }
        [MaxLength(50)] public string? City { get; set; }
        [MaxLength(50)] public string? Province { get; set; }

        /// <summary>Copies every buyer value (names, TIN, VAT, contact details) from <paramref name="values"/>.</summary>
        public void CopyBuyerValuesFrom(ContactVatRecord values)
        {
            VatNumber = values.VatNumber;
            Tin = values.Tin;
            RegisteredName = values.RegisteredName;
            TradeName = values.TradeName;
            Phone = values.Phone;
            Email = values.Email;
            HouseNo = values.HouseNo;
            Street = values.Street;
            City = values.City;
            Province = values.Province;
        }

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
