using System.Globalization;
using Fiscalize.LayoutRenderer;
using FiscalizeXero.Api.Data;
using FiscalizeXero.Api.Services.Fiscalization;
using FiscalizeXero.Xero.Models;

namespace FiscalizeXero.Api.Services.Layouts
{
    /// <summary>Everything a layout can print about one fiscalised Xero document. Gathered by the caller; nothing here calls Xero.</summary>
    public sealed class XeroLayoutSource
    {
        public required SalesDocument Document { get; init; }
        /// <summary>The contact as Xero's single-contact read returns it (addresses, phones, groups, balance), when it could be read.</summary>
        public Contact? Contact { get; init; }
        /// <summary>The buyer as it went to ZIMRA: what the user saved in Fiscalize, else Xero's (see <see cref="BuyerResolver"/>).</summary>
        public ContactVatRecord? Buyer { get; init; }
        /// <summary>Each tax type's rate, when the organisation's rates could be read.</summary>
        public IReadOnlyDictionary<string, LineTax>? TaxByType { get; init; }
        public FiscalRecord? Record { get; init; }
        /// <summary>For a credit note: the invoice it was fiscalised against.</summary>
        public string? BaseInvoiceNumber { get; init; }
        /// <summary>Read from Xero only when the design prints it.</summary>
        public string? OnlineInvoiceUrl { get; init; }
    }

    /// <summary>
    /// Turns a fiscalised Xero document into the neutral document the layout renderer draws. Pure: no network, no settings,
    /// so it is tested away from Xero.
    ///
    /// The field KEYS are a contract with the platform (PluginBuiltInFields, plugin type "Xero") and with every published
    /// layout: a key is never renamed once released. Xero's tracking categories are offered as <c>Doc.&lt;category&gt;</c>.
    /// </summary>
    public static class XeroLayoutMapper
    {
        public const string PluginType = "Xero";
        public const string TrackingPrefix = "Doc.";

        /// <summary>Every built-in key this app fills, with the label the platform shows (it keeps its own labels for built-ins).</summary>
        public static readonly (string Key, string Label)[] Fields =
        {
            ("Terms", "Payment terms"),
            ("ExchangeRate", "Exchange rate"),
            ("CustomerAccount", "Customer account"),
            ("CustomerFax", "Customer fax"),
            ("CustomerMobile", "Customer mobile"),
            ("ExtOrderNum", "Reference"),
            ("BaseInvoiceNumber", "Against invoice"),
            ("Currency", "Currency"),
            ("TaxInclusive", "Prices include VAT"),
            ("OnlineInvoiceUrl", "Online invoice link"),
            ("BalanceDue", "Amount due"),
            ("AmountApplied", "Amount paid"),
            ("AmountCredited", "Credits applied"),
            ("DocumentDiscount", "Total discount"),
            ("CreditRemaining", "Remaining credit"),
            ("DeliveryAddress", "Delivery address"),
            ("ShipToName", "Delivery attention"),
            ("ContactPerson", "Contact person"),
            ("CustomerAddress", "Postal address"),
            ("CustomerPostalCode", "Postal code"),
            ("CustomerRegistration", "Company number"),
            ("CustomerTelephone2", "Customer direct dial"),
            ("CustomerWebsite", "Customer website"),
            ("CustomerGroup", "Contact group"),
            ("CustomerBalance", "Customer outstanding balance"),
        };

        /// <summary>Keys that would show an amount owed by the customer to anyone who sees the document; the platform marks them restricted.</summary>
        public static readonly string[] Restricted = { "CustomerBalance" };

        /// <summary>The key a tracking category is offered under, e.g. <c>Doc.Region</c>.</summary>
        public static string TrackingKey(string categoryName) => TrackingPrefix + categoryName.Trim();

        public static LayoutDocument Map(XeroLayoutSource source, IReadOnlyCollection<string>? fieldsUsed)
        {
            var doc = source.Document;
            var isCredit = doc is CreditNote;
            var fiscal = Fiscal(source.Record);
            var contact = source.Contact;
            var buyer = source.Buyer;

            var result = new LayoutDocument
            {
                Title = isCredit ? "CREDIT NOTE" : fiscal is { IsInvalid: false } ? "FISCAL TAX INVOICE" : "TAX INVOICE",
                Number = doc.DocumentNumber ?? source.Record?.DocumentNumber ?? "",
                Currency = Blank(doc.CurrencyCode),
                TaxLabel = "VAT",
                Subtotal = doc.SubTotal ?? 0m,
                TaxTotal = doc.TotalTax ?? 0m,
                Total = doc.Total ?? 0m,
                AmountPaid = doc is Invoice paid ? paid.AmountPaid ?? 0m : 0m,
                Fiscal = fiscal,
                Party = Party(doc, contact, buyer)
            };

            if (Date(doc.Date) is { } date) result.Meta.Add(new LayoutPair("Date", date));
            if (doc is Invoice inv && Date(inv.DueDateString) is { } due) result.Meta.Add(new LayoutPair("Due", due));
            if (isCredit && !string.IsNullOrWhiteSpace(source.BaseInvoiceNumber))
                result.Meta.Add(new LayoutPair("Against invoice", source.BaseInvoiceNumber));

            var delivery = contact?.Address("STREET");
            var postal = contact?.Address("POBOX");
            if (delivery != null && !SameAddress(delivery, postal ?? delivery, postal == null))
                result.ShipTo = new LayoutShipTo { Name = Blank(delivery.AttentionTo), AddressLines = AddressLines(delivery) };

            var notes = new List<string>();
            foreach (var line in doc.LineItems)
            {
                if (line.IsDescriptionOnly)
                {
                    if (!string.IsNullOrWhiteSpace(line.Description)) notes.Add(line.Description.Trim());
                    continue;
                }
                result.Lines.Add(Line(doc, line, source.TaxByType));
            }
            result.DiscountTotal = result.Lines.Sum(l => l.DiscountAmount);
            if (notes.Count > 0) result.Notes = string.Join("\n", notes);

            // The ERP's own fields, supplied only when the published layout asks for them.
            var wanted = fieldsUsed == null ? null : new HashSet<string>(fieldsUsed, StringComparer.OrdinalIgnoreCase);
            if (wanted == null || wanted.Count == 0) return result;
            void Field(string key, string? value)
            {
                if (wanted.Contains(key) && !string.IsNullOrWhiteSpace(value)) result.Fields[key] = value.Trim();
            }

            Field("Terms", Terms(contact?.PaymentTerms?.Sales));
            if (doc.CurrencyRate is { } rate && rate != 1m) Field("ExchangeRate", rate.ToString("0.######", CultureInfo.InvariantCulture));
            Field("CustomerAccount", contact?.AccountNumber);
            Field("CustomerFax", contact?.Phone("FAX")?.Full);
            Field("CustomerMobile", contact?.Phone("MOBILE")?.Full);

            Field("ExtOrderNum", FiscalReferenceMarker.Strip(doc.Reference));
            Field("BaseInvoiceNumber", source.BaseInvoiceNumber);
            Field("Currency", doc.CurrencyCode);
            Field("TaxInclusive", doc.IsTaxInclusive ? "Yes" : "No");
            Field("OnlineInvoiceUrl", source.OnlineInvoiceUrl);
            if (doc is Invoice i)
            {
                Field("BalanceDue", Money(i.AmountDue));
                Field("AmountApplied", Money(i.AmountPaid, skipZero: true));
                Field("AmountCredited", Money(i.AmountCredited, skipZero: true));
                Field("DocumentDiscount", Money(i.TotalDiscount, skipZero: true));
            }
            if (doc is CreditNote c) Field("CreditRemaining", Money(c.RemainingCredit));

            Field("DeliveryAddress", delivery == null ? null : string.Join(", ", AddressLines(delivery)));
            Field("ShipToName", delivery?.AttentionTo);
            Field("ContactPerson", string.Join(" ", new[] { contact?.FirstName, contact?.LastName }.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n!.Trim())));
            Field("CustomerAddress", postal == null ? null : string.Join(", ", AddressLines(postal)));
            Field("CustomerPostalCode", postal?.PostalCode);
            // The company number box often holds a VAT number in Xero (see XeroTaxIdentifiers); only a real registration number prints here.
            if (!XeroTaxIdentifiers.IsTin(contact?.CompanyNumber) && !XeroTaxIdentifiers.IsVatNumber(contact?.CompanyNumber))
                Field("CustomerRegistration", contact?.CompanyNumber);
            Field("CustomerTelephone2", contact?.Phone("DDI")?.Full);
            Field("CustomerWebsite", contact?.Website);
            Field("CustomerGroup", string.Join(", ", (contact?.ContactGroups ?? new())
                .Where(g => !string.IsNullOrWhiteSpace(g.Name) && !string.Equals(g.Status, "DELETED", StringComparison.OrdinalIgnoreCase))
                .Select(g => g.Name!.Trim())));
            Field("CustomerBalance", Money(contact?.Balances?.AccountsReceivable?.Outstanding));

            // Tracking categories: every option used on the document's lines, in order, once each.
            foreach (var key in wanted.Where(k => k.StartsWith(TrackingPrefix, StringComparison.OrdinalIgnoreCase)))
            {
                var category = key[TrackingPrefix.Length..];
                var options = doc.LineItems
                    .SelectMany(l => l.Tracking ?? new())
                    .Where(t => string.Equals(t.Name?.Trim(), category, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(t.Option))
                    .Select(t => t.Option!.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                Field(key, string.Join(", ", options));
            }
            return result;
        }

        private static LayoutParty Party(SalesDocument doc, Contact? contact, ContactVatRecord? buyer)
        {
            var name = Blank(buyer?.RegisteredName) ?? Blank(contact?.Name) ?? Blank(doc.Contact?.Name);
            var trade = Blank(buyer?.TradeName);
            var party = new LayoutParty
            {
                Name = name,
                TradeName = trade != null && !string.Equals(trade, name, StringComparison.OrdinalIgnoreCase) ? trade : null,
                Tin = Blank(buyer?.Tin),
                Vat = Blank(buyer?.VatNumber),
                Email = Blank(buyer?.Email) ?? Blank(contact?.EmailAddress),
                Phone = Blank(buyer?.Phone) ?? Blank(contact?.Phone("DEFAULT")?.Full) ?? Blank(contact?.Phone("MOBILE")?.Full) ?? Blank(contact?.Phone("DDI")?.Full)
            };

            // An address saved in Fiscalize is what went to ZIMRA; otherwise the postal (billing) address, else the street one.
            var saved = new[] { JoinNonEmpty(" ", buyer?.HouseNo, buyer?.Street), buyer?.City, buyer?.Province }
                .Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => l!.Trim()).ToList();
            if (saved.Count > 0) party.AddressLines.AddRange(saved);
            else if ((contact?.Address("POBOX") ?? contact?.Address("STREET")) is { } address) party.AddressLines.AddRange(AddressLines(address));
            return party;
        }

        /// <summary>
        /// One line, tax-exclusive, as the renderer expects (it prints net amounts and adds tax in the totals). Xero gives each
        /// line's amount after discount and its tax, so the net amount is exact; the unit price and discount are worked back from it.
        /// </summary>
        private static LayoutLine Line(SalesDocument doc, LineItem line, IReadOnlyDictionary<string, LineTax>? taxByType)
        {
            var quantity = line.Quantity ?? 1m;
            var amount = line.LineAmount ?? quantity * (line.UnitAmount ?? 0m);
            var tax = line.TaxAmount ?? 0m;
            var net = doc.IsTaxInclusive ? amount - tax : amount;

            decimal percent;
            if (!string.IsNullOrWhiteSpace(line.TaxType) && taxByType != null && taxByType.TryGetValue(line.TaxType, out var known)) percent = known.Percent;
            else percent = net != 0m ? Math.Round(tax / net * 100m, 2) : 0m;
            if (doc.IsNoTax) percent = 0m;

            var unit = line.UnitAmount ?? (quantity != 0m ? amount / quantity : 0m);
            var unitNet = doc.IsTaxInclusive && percent > 0m ? unit / (1m + percent / 100m) : unit;
            var discounted = (line.DiscountRate ?? 0m) > 0m || (line.DiscountAmount ?? 0m) > 0m;
            var discount = discounted ? Math.Max(0m, Math.Round(quantity * unitNet - net, 2)) : 0m;

            return new LayoutLine
            {
                Code = Blank(line.ItemCode),
                Description = Blank(line.Description) ?? Blank(line.Item?.Name) ?? Blank(line.ItemCode) ?? "",
                Quantity = quantity,
                UnitPrice = Math.Round(unitNet, 4),
                DiscountAmount = discount,
                TaxPercent = percent,
                Amount = Math.Round(net, 2)
            };
        }

        private static LayoutFiscal? Fiscal(FiscalRecord? r) => r == null ? null : new LayoutFiscal
        {
            VerificationCode = r.VerificationCode,
            QrUrl = r.QrCodeUrl,
            ReceiptNumber = r.ReceiptNumber?.ToString(CultureInfo.InvariantCulture),
            GlobalNumber = r.GlobalNumber?.ToString(CultureInfo.InvariantCulture),
            DayNumber = r.FiscalDayNumber?.ToString(CultureInfo.InvariantCulture),
            DeviceId = r.DeviceId.ToString(CultureInfo.InvariantCulture),
            IsInvalid = string.Equals(r.ValidationError, "Red", StringComparison.OrdinalIgnoreCase),
            ValidationError = string.Equals(r.ValidationError, "Red", StringComparison.OrdinalIgnoreCase) ? "Rejected by ZIMRA" : null
        };

        /// <summary>"30 days after the invoice date", "20th of the following month".</summary>
        public static string? Terms(PaymentTerm? term)
        {
            if (term?.Day is not { } day || day <= 0 || string.IsNullOrWhiteSpace(term.Type)) return null;
            return term.Type.ToUpperInvariant() switch
            {
                "DAYSAFTERBILLDATE" => $"{day} {(day == 1 ? "day" : "days")} after the invoice date",
                "DAYSAFTERBILLMONTH" => $"{day} {(day == 1 ? "day" : "days")} after the end of the invoice month",
                "OFCURRENTMONTH" => $"{Ordinal(day)} of the current month",
                "OFFOLLOWINGMONTH" => $"{Ordinal(day)} of the following month",
                _ => null
            };
        }

        private static string Ordinal(int n)
        {
            var suffix = (n % 100) is 11 or 12 or 13 ? "th" : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };
            return n + suffix;
        }

        public static List<string> AddressLines(ContactAddress a)
        {
            var lines = new[] { a.AddressLine1, a.AddressLine2, a.AddressLine3, a.AddressLine4 }
                .Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => l!.Trim()).ToList();
            var place = JoinNonEmpty(" ", JoinNonEmpty(", ", a.City, a.Region), a.PostalCode);
            if (!string.IsNullOrWhiteSpace(place)) lines.Add(place);
            if (!string.IsNullOrWhiteSpace(a.Country)) lines.Add(a.Country.Trim());
            return lines;
        }

        /// <summary>True when the ship-to would only repeat the bill-to (or when there is no other address to compare with and it is the bill-to).</summary>
        private static bool SameAddress(ContactAddress a, ContactAddress b, bool deliveryIsBillTo) =>
            deliveryIsBillTo || AddressLines(a).SequenceEqual(AddressLines(b), StringComparer.OrdinalIgnoreCase);

        private static string? Date(string? xeroDate) =>
            !string.IsNullOrWhiteSpace(xeroDate) && DateTime.TryParse(xeroDate.Length >= 10 ? xeroDate[..10] : xeroDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
                ? d.ToString("d MMM yyyy", CultureInfo.InvariantCulture)
                : null;

        private static string? Money(decimal? value, bool skipZero = false) =>
            value is not { } v || (skipZero && v == 0m) ? null : v.ToString("#,##0.00", CultureInfo.InvariantCulture);

        private static string? JoinNonEmpty(string separator, params string?[] parts)
        {
            var joined = string.Join(separator, parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim()));
            return joined.Length == 0 ? null : joined;
        }

        private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
