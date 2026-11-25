using System.Text.RegularExpressions;

namespace FiscalizeXero.Api.Services.Fiscalization
{
    /// <summary>
    /// Picks a usable phone number and email address out of what people actually type into Xero: several
    /// numbers in one field, spaces and brackets, "ext 12", "n/a", or a list of emails separated by commas.
    /// </summary>
    public static class BuyerContactResolver
    {
        private static readonly Regex SplitPhones = new(@"\s*(?:/|,|;|\||\bor\b|&|\band\b)\s*", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex Extension = new(@"\s*(?:ext\.?|extension|x)\s*\d+\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex EmailShape = new(@"^[^@\s,;<>()]+@[^@\s,;<>()]+\.[A-Za-z]{2,}$", RegexOptions.Compiled);

        /// <summary>Longest phone number sent to the platform (E.164 allows 15 digits, plus a leading "+").</summary>
        public const int MaxPhoneLength = 16;

        /// <summary>
        /// The first usable number among <paramref name="candidates"/>, in order (Xero Phone, then Mobile,
        /// then Direct dial), compacted to digits with an optional leading "+". Null when none is usable.
        /// </summary>
        public static string? FirstPhone(params string?[] candidates)
        {
            foreach (var candidate in candidates)
            {
                if (string.IsNullOrWhiteSpace(candidate)) continue;
                foreach (var part in SplitPhones.Split(candidate))
                {
                    var phone = NormalisePhone(part);
                    if (phone != null) return phone;
                }
            }
            return null;
        }

        /// <summary>
        /// "+263 77 123 4567" becomes "+263771234567", "(024) 270-1234" becomes "0242701234", "00263…" becomes
        /// "+263…". Anything with fewer than 7 or more than 15 digits ("n/a", "0", a pasted ID number) is rejected.
        /// </summary>
        public static string? NormalisePhone(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            var text = Extension.Replace(raw.Trim(), string.Empty);
            var plus = text.TrimStart().StartsWith('+');
            var digits = new string(text.Where(char.IsDigit).ToArray());
            if (!plus && digits.StartsWith("00") && digits.Length > 9)
            {
                plus = true;
                digits = digits[2..];
            }
            if (digits.Length is < 7 or > 15) return null;
            if (digits.All(d => d == '0')) return null;
            return plus ? "+" + digits : digits;
        }

        /// <summary>The first well-formed address in a field that may hold several ("a@x.co.zw, b@y.com").</summary>
        public static string? FirstEmail(params string?[] candidates)
        {
            foreach (var candidate in candidates)
            {
                if (string.IsNullOrWhiteSpace(candidate)) continue;
                foreach (var part in candidate.Split(new[] { ',', ';', ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var email = part.Trim().Trim('<', '>', '"', '\'');
                    if (email.Length <= 254 && EmailShape.IsMatch(email)) return email;
                }
            }
            return null;
        }
    }
}
