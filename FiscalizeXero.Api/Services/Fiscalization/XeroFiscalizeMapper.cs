using FiscalizeXero.Shared.Models.Fiscalize;
using FiscalizeXero.Xero.Models;
using System.Globalization;

namespace FiscalizeXero.Api.Services.Fiscalization
{
    /// <summary>
    /// Each Xero tax rate (by its <see cref="TaxRate.TaxType"/> code) as the rate and ZIMRA category the platform
    /// wants. A Xero rate can be made of several components (4% + 4.25%); ZIMRA knows one rate per line, so the
    /// effective rate, all components together, is what is reported.
    /// </summary>
    public static class XeroTaxResolver
    {
        public static IReadOnlyDictionary<string, LineTax> Resolve(IEnumerable<TaxRate> taxRates)
        {
            var result = new Dictionary<string, LineTax>(StringComparer.OrdinalIgnoreCase);
            foreach (var rate in taxRates)
            {
                if (string.IsNullOrWhiteSpace(rate.TaxType)) continue;
                var percent = rate.EffectiveRate ?? rate.DisplayTaxRate ?? (rate.TaxComponents ?? new()).Sum(c => c.Rate ?? 0m);
                result[rate.TaxType] = new LineTax(percent, TaxCategory.For(percent, rate.Name));
            }
            return result;
        }
    }

    /// <summary>
    /// Pure mapping from a Xero sales invoice or customer credit note to the Fiscalize platform's request.
    /// Stateless and dependency-free so it is easy to unit test.
    /// </summary>
    public static class XeroFiscalizeMapper
    {
        /// <summary>Used only if Xero gives no currency on a document, which it always does.</summary>
        public const string DefaultCurrency = "USD";

        public static FiscalizeInvoiceRequest MapInvoice(
            Invoice invoice,
            int deviceId,
            FiscalizeInvoiceBuyerData? buyerData,
            IDictionary<string, string?> hsCodesByItemCode,
            IReadOnlyDictionary<string, LineTax>? taxByType = null)
        {
            var request = Map(invoice, deviceId, buyerData, hsCodesByItemCode, taxByType);
            request.DueDate = ParseDate(invoice.DueDateString);
            return request;
        }

        /// <summary>A customer credit note as a platform credit note against <paramref name="originalInvoiceDocNumber"/>.</summary>
        public static FiscalizeInvoiceRequest MapCreditNote(
            CreditNote creditNote,
            int deviceId,
            FiscalizeInvoiceBuyerData? buyerData,
            IDictionary<string, string?> hsCodesByItemCode,
            string? originalInvoiceDocNumber,
            IReadOnlyDictionary<string, LineTax>? taxByType = null)
        {
            var request = Map(creditNote, deviceId, buyerData, hsCodesByItemCode, taxByType);
            request.BaseDocNo = originalInvoiceDocNumber;
            return request;
        }

        private static FiscalizeInvoiceRequest Map(
            SalesDocument doc,
            int deviceId,
            FiscalizeInvoiceBuyerData? buyerData,
            IDictionary<string, string?> hsCodesByItemCode,
            IReadOnlyDictionary<string, LineTax>? taxByType)
        {
            var total = doc.Total ?? 0m;
            return new FiscalizeInvoiceRequest
            {
                DiscountPercentage = 0m, // Xero discounts are per line and already inside each line's amount
                DocNo = string.IsNullOrWhiteSpace(doc.DocumentNumber) ? doc.DocumentId.ToString() : doc.DocumentNumber!,
                Reference = doc.Reference,
                CustomerCode = doc.Contact?.ContactID.ToString() ?? string.Empty,
                CustomerName = doc.Contact?.Name ?? string.Empty,
                Currency = string.IsNullOrWhiteSpace(doc.CurrencyCode) ? DefaultCurrency : doc.CurrencyCode!,
                DeviceId = deviceId,
                IsDraft = false,
                Fiscalize = true,
                DocDate = ParseDate(doc.DateString) ?? DateTime.UtcNow.Date,
                Total = total,
                BuyerData = buyerData, // built and checked by BuyerCompliance; null for a cash sale
                Payments = new List<FiscalizeInvoicePayment>
                {
                    // The platform requires at least one payment line. Xero does not say how a document will be
                    // paid when it is approved, so its total is recorded as Cash, as the other integrations do.
                    new() { MoneyTypeCode = MoneyType.Cash, PaymentAmount = total }
                },
                Lines = MapLines(doc, hsCodesByItemCode, taxByType)
            };
        }

        /// <summary>The lines that are sales: Xero also allows lines that are only a description, which carry no amount.</summary>
        public static IEnumerable<LineItem> SalesLines(SalesDocument doc) => doc.LineItems.Where(l => !l.IsDescriptionOnly);

        /// <summary>
        /// A line's amount including its tax. Xero gives each line its amount after discount and its tax, so nothing
        /// is recalculated: on a tax-exclusive document the tax is added, on a tax-inclusive or no-tax one the amount
        /// already is the final figure.
        /// </summary>
        public static decimal GrossOf(SalesDocument doc, LineItem line) =>
            (line.LineAmount ?? 0m) + (doc.IsTaxInclusive || doc.IsNoTax ? 0m : line.TaxAmount ?? 0m);

        /// <summary>
        /// Prices and tax, line by line. Every line goes to the platform at its final, tax-inclusive price
        /// (IsFinalPrice = true), so the platform never adds or strips tax and the ZIMRA receipt total is the Xero
        /// total to the cent. The lines are checked against the document total before anything is sent: a document
        /// whose lines don't add up is refused with a clear reason instead of reaching ZIMRA with the wrong amount.
        /// </summary>
        private static List<FiscalizeInvoiceLine> MapLines(
            SalesDocument doc,
            IDictionary<string, string?> hsCodesByItemCode,
            IReadOnlyDictionary<string, LineTax>? taxByType)
        {
            var lines = SalesLines(doc).ToList();
            var total = doc.Total ?? 0m;
            if (lines.Count == 0)
            {
                if (total == 0m) return new List<FiscalizeInvoiceLine>();
                throw TotalMismatch(doc, 0m);
            }

            var gross = lines.Select(l => GrossOf(doc, l)).ToArray();
            var subtotal = gross.Sum();
            // Xero rounds tax per line, so the lines normally add up exactly; a cent per line covers odd rounding.
            if (Math.Abs(subtotal - total) > 0.01m * (lines.Count + 1))
                throw TotalMismatch(doc, subtotal);

            // Put any leftover cent on the largest line so the lines sum to the document total exactly.
            var lineTotals = gross.Select(g => Math.Round(g, 2, MidpointRounding.AwayFromZero)).ToArray();
            var leftover = total - lineTotals.Sum();
            if (leftover != 0m)
            {
                var largest = Array.IndexOf(lineTotals, lineTotals.OrderByDescending(Math.Abs).First());
                lineTotals[largest] += leftover;
            }

            var result = new List<FiscalizeInvoiceLine>();
            for (var idx = 0; idx < lines.Count; idx++)
            {
                var line = lines[idx];
                var tax = LineTaxFor(doc, line, taxByType);
                var itemCode = line.ItemCode?.Trim() ?? string.Empty;
                string? hsCode = null;
                if (itemCode.Length > 0) hsCodesByItemCode.TryGetValue(itemCode, out hsCode);
                var qty = line.Quantity is > 0 ? line.Quantity.Value : 1m;

                foreach (var (partQty, price) in PricesInCents(lineTotals[idx], qty))
                {
                    result.Add(new FiscalizeInvoiceLine
                    {
                        LineNo = result.Count + 1,
                        ItemCode = itemCode,
                        ItemName = ItemNameFor(line),
                        ItemHsCode = hsCode ?? string.Empty,
                        Price = price,
                        IsFinalPrice = true,
                        Quantity = partQty,
                        DiscountPercentage = 0m,
                        LineTotal = partQty == qty ? lineTotals[idx] : Math.Round(partQty * price, 2),
                        TaxPercent = tax.Percent,
                        TaxCategory = tax.Category
                    });
                }
            }
            return result;
        }

        /// <summary>The item's name where the line has one, else the first line of its description, else its code.</summary>
        private static string ItemNameFor(LineItem line)
        {
            if (!string.IsNullOrWhiteSpace(line.Item?.Name)) return line.Item!.Name!.Trim();
            var firstLine = line.Description?.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(firstLine)) return firstLine!.Length > 200 ? firstLine[..200] : firstLine;
            return line.ItemCode?.Trim() ?? string.Empty;
        }

        /// <summary>
        /// The line's tax rate from the organisation's tax rates. If the rate cannot be found (a rate deleted since),
        /// it is worked out from the line's own tax and amount, so a document never goes out with the wrong rate.
        /// </summary>
        private static LineTax LineTaxFor(SalesDocument doc, LineItem line, IReadOnlyDictionary<string, LineTax>? taxByType)
        {
            if (doc.IsNoTax) return new LineTax(0m, TaxCategory.ZeroRated);
            if (!string.IsNullOrWhiteSpace(line.TaxType) && taxByType != null && taxByType.TryGetValue(line.TaxType!, out var resolved))
                return resolved;

            var tax = line.TaxAmount ?? 0m;
            var net = (line.LineAmount ?? 0m) - (doc.IsTaxInclusive ? tax : 0m);
            if (tax == 0m || net == 0m) return new LineTax(0m, TaxCategory.ZeroRated);
            var percent = Math.Round(tax / net * 100m, 2, MidpointRounding.AwayFromZero);
            return new LineTax(percent, TaxCategory.For(percent, null));
        }

        /// <summary>
        /// Unit prices in whole cents whose quantities add back to the line total exactly. 3 units for 115.49 can't
        /// share one 2-decimal price (38.50 x 3 = 115.50), so the line is sent as 1 x 38.49 + 2 x 38.50; ZIMRA then
        /// shows 115.49, the Xero amount, whatever rounding the platform applies. A fractional quantity (2.5 kg)
        /// can't be split into units and keeps one price to 4 decimals.
        /// </summary>
        internal static IEnumerable<(decimal Qty, decimal Price)> PricesInCents(decimal lineTotal, decimal qty)
        {
            var cents = lineTotal * 100m;
            if (qty != decimal.Truncate(qty) || cents != decimal.Truncate(cents))
            {
                yield return (qty, Math.Round(lineTotal / qty, 4, MidpointRounding.AwayFromZero));
                yield break;
            }
            var basePrice = Math.Floor(cents / qty);
            var extra = cents - basePrice * qty; // units that carry one more cent: 0 .. qty-1
            if (extra == 0m)
            {
                yield return (qty, basePrice / 100m);
                yield break;
            }
            yield return (qty - extra, basePrice / 100m);
            yield return (extra, (basePrice + 1m) / 100m);
        }

        private static InvalidOperationException TotalMismatch(SalesDocument doc, decimal linesTotal) => new(
            $"Not fiscalised: Xero {doc.DocumentNumber ?? doc.DocumentId.ToString()} has a total of {doc.Total ?? 0m:N2}, but its lines with tax " +
            $"come to {linesTotal:N2}. Sending it would put a different amount on the ZIMRA receipt. " +
            "Open the document in Xero, check each line's amount and tax rate, then fiscalise again.");

        /// <summary>Xero's document dates are calendar dates written as <c>2026-11-23T00:00:00</c>.</summary>
        private static DateTime? ParseDate(string? raw) =>
            DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d.Date : null;
    }
}
