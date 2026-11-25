namespace FiscalizeXero.Api.Services.Fiscalization
{
    /// <summary>A line's effective tax as the Fiscalize platform wants it.</summary>
    public sealed record LineTax(decimal Percent, string Category);

    public static class TaxCategory
    {
        public const string Rated = "Rated";
        public const string ZeroRated = "ZeroRated";
        public const string Exempt = "Exempt";

        /// <summary>Exempt when the tax rate's name says so; otherwise by rate.</summary>
        public static string For(decimal percent, string? taxRateName)
        {
            if (!string.IsNullOrEmpty(taxRateName) && taxRateName.Contains("exempt", StringComparison.OrdinalIgnoreCase))
                return Exempt;
            return percent > 0 ? Rated : ZeroRated;
        }
    }

    /// <summary>ZIMRA's money types, as the FDMS receipt payment schema names them.</summary>
    public static class MoneyType
    {
        public const string Cash = "Cash";
        public const string Card = "Card";
        public const string MobileWallet = "MobileWallet";
        public const string Coupon = "Coupon";
        public const string Credit = "Credit";
        public const string BankTransfer = "BankTransfer";
        public const string Other = "Other";
    }
}
