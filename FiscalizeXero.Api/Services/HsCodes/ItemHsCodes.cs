using FiscalizeXero.Shared.Models.Fiscalize;
using FiscalizeXero.Shared.Models.TaxDetails;
using FiscalizeXero.Xero.Models;

namespace FiscalizeXero.Api.Services.HsCodes
{
    /// <summary>
    /// Where a line's HS code comes from. Xero has no HS code field and no custom fields on items, so codes are kept
    /// in Fiscalize against the Xero item code. In use, in order: the code saved for the item code, else the
    /// company's default, else the system default. A line with no item code takes the default.
    /// </summary>
    public static class ItemHsCodes
    {
        /// <summary>
        /// The digits of an HS code ("8421.23.00" → "84212300"), or null when the text isn't one (a stock code, a
        /// sentence), so nothing that isn't a code is ever sent to ZIMRA as one.
        /// </summary>
        public static string? AsHsCode(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            var stripped = new string(raw.Where(c => c is not (' ' or '.' or '-')).ToArray());
            return stripped.Length is >= 6 and <= 10 && stripped.All(char.IsAsciiDigit) ? stripped : null;
        }

        /// <summary>The saved codes keyed by item code, ignoring case and surrounding spaces as Xero does.</summary>
        public static Dictionary<string, string?> Lookup(IEnumerable<(string ItemCode, string? HsCode)> saved)
        {
            var codes = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (var (itemCode, hsCode) in saved)
                if (!string.IsNullOrWhiteSpace(itemCode) && !string.IsNullOrWhiteSpace(hsCode))
                    codes[itemCode.Trim()] = hsCode.Trim();
            return codes;
        }

        /// <summary>
        /// Gives every line that has no HS code yet the default one, so a missing code never blocks fiscalisation.
        /// Throws only if there is no default at all, naming the lines to fix.
        /// </summary>
        public static void ApplyDefault(IEnumerable<FiscalizeInvoiceLine> lines, string? defaultHsCode)
        {
            var missing = lines.Where(l => string.IsNullOrWhiteSpace(l.ItemHsCode)).ToList();
            if (missing.Count == 0) return;

            if (string.IsNullOrWhiteSpace(defaultHsCode))
                throw new FiscalizeXero.Api.Exceptions.UserFacingException(
                    $"HS code missing for: {string.Join(", ", missing.Select(l => string.IsNullOrWhiteSpace(l.ItemName) ? $"line {l.LineNo}" : l.ItemName).Distinct())}. " +
                    "Set the HS code on each item, or set a default HS code under Fiscalization settings, before fiscalizing.");

            foreach (var line in missing) line.ItemHsCode = defaultHsCode.Trim();
        }

        /// <summary>The code an item fiscalizes under and where it comes from.</summary>
        public static ItemHsCodeModel Describe(string itemCode, Item? item, string? saved, string? defaultCode)
        {
            var savedCode = string.IsNullOrWhiteSpace(saved) ? null : saved.Trim();
            var fallback = string.IsNullOrWhiteSpace(defaultCode) ? null : defaultCode.Trim();
            return new ItemHsCodeModel
            {
                ItemCode = itemCode,
                ItemId = item?.ItemID.ToString("D"),
                Name = item?.Name,
                Description = item?.Description,
                IsSold = item?.IsSold,
                HsCode = savedCode ?? fallback,
                HsCodeSource = savedCode != null ? "Saved" : fallback != null ? "Default" : null
            };
        }
    }
}
