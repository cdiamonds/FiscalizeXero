using System.Text.RegularExpressions;

namespace FiscalizeXero.Api.Services.Fiscalization
{
    /// <summary>The four address parts ZIMRA takes for a buyer. Any part may be null when it can't be found.</summary>
    public sealed record BuyerAddress(string? HouseNo, string? Street, string? City, string? Province);

    /// <summary>The raw accounting-system address: a few free-text lines plus the structured fields.</summary>
    public sealed record RawAddress(
        IReadOnlyList<string?> Lines, string? City = null, string? Province = null, string? PostalCode = null, string? Country = null);

    /// <summary>
    /// Turns a free-text address into house number, street, city and province, the way people really enter them:
    /// <list type="bullet">
    /// <item>The house number is found on whichever line holds it: "12 Samora Machel Ave", "12A Main St",
    /// "Stand 4512, Borrowdale", "Plot 7", "Shop 3", "No. 5", "#14", or "Josiah Tongogara Street 45".</item>
    /// <item>Words are never taken as house numbers ("Main Street" has none), and ordinals aren't either
    /// ("2nd Street", "1st Floor").</item>
    /// <item>Lines that only repeat the customer's name, the country, a P.O. Box or a postal code are skipped.</item>
    /// <item>A missing city is found in the lines (a known town, or a known suburb of Harare or Bulawayo) and a
    /// missing province is worked out from the city. ISO codes ("ME", "HA") and short forms ("Mash East") are
    /// written out in full.</item>
    /// <item>The suburb is kept with the street ("12 Main Street, Avondale" gives street "Main Street, Avondale").</item>
    /// </list>
    /// Nothing is invented: a part that can't be read from the address stays null.
    /// </summary>
    public static class BuyerAddressParser
    {
        /// <summary>ZIMRA's house number field is short; the Zoho Books integration caps it at 10 characters too.</summary>
        public const int MaxHouseNoLength = 10;
        private const int MaxStreetLength = 100;

        private const string Number = @"\d+[A-Za-z]?(?:\s*[-/]\s*\d+[A-Za-z]?)?";

        private static readonly Regex Labelled = new(
            @"^(?<label>stand|plot|erf|lot|unit|shop|flat|house|office|suite|block|apt|apartment|no|number|#)\.?\s*(?:no\.?|number|#)?\s*(?<num>" + Number + @")(?![A-Za-z0-9])[\s,:-]*(?<rest>.*)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex Leading = new(
            @"^(?<num>" + Number + @")(?![A-Za-z0-9])[\s,:-]*(?<rest>.*)$",
            RegexOptions.Compiled);

        private static readonly Regex Trailing = new(
            @"^(?<rest>.*?\b(?:street|st|road|rd|avenue|ave|av|drive|dr|way|lane|ln|close|crescent|cres|place|pl|boulevard|blvd|highway|hwy|court|ct|terrace|walk|rise|grove|square|sq)\.?)[\s,]+(?:no\.?\s*)?(?<num>" + Number + @")$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex StreetWord = new(
            @"\b(?:street|st|road|rd|avenue|ave|av|drive|dr|way|lane|ln|close|crescent|cres|place|pl|boulevard|blvd|highway|hwy|court|ct|terrace|walk|rise|grove|square|sq|cnr|corner)\b\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex PoBox = new(@"^\s*(?:p\.?\s*o\.?\s*box|private\s+bag|p\.?\s*bag|box)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex OnlyDigits = new(@"^\d{3,6}$", RegexOptions.Compiled);

        public static BuyerAddress Parse(RawAddress raw, params string?[] customerNames)
        {
            var countryIsZimbabwe = IsZimbabweOrBlank(raw.Country);
            var names = customerNames.Where(n => !string.IsNullOrWhiteSpace(n)).Select(Key).ToHashSet();

            // Every comma-separated piece of every line, in order, minus the noise.
            var segments = raw.Lines
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .SelectMany(l => l!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .Select(s => Regex.Replace(s, @"\s+", " ").Trim())
                .Where(s => s.Length > 0)
                .Where(s => !names.Contains(Key(s)))
                .Where(s => !IsCountry(s))
                .Where(s => !PoBox.IsMatch(s))
                .Where(s => !(OnlyDigits.IsMatch(s) && s == raw.PostalCode?.Trim()))
                .ToList();

            var city = Clean(raw.City);
            var province = ZimbabweGeography.CanonicalProvince(raw.Province, countryIsZimbabwe) ?? Clean(raw.Province);

            // Drop pieces that only repeat the city or province already given.
            segments.RemoveAll(s => (city != null && Key(s) == Key(city)) || (province != null && Key(s) == Key(province)));

            if (city == null)
            {
                var index = segments.FindLastIndex(s => ZimbabweGeography.IsCity(s));
                if (index >= 0)
                {
                    city = ZimbabweGeography.CanonicalCity(segments[index]);
                    segments.RemoveAt(index);
                }
                else
                {
                    // "Msasa Harare" (no comma): the town is the last word(s) of a piece.
                    for (var i = segments.Count - 1; i >= 0 && city == null; i--)
                    {
                        var (before, found) = ZimbabweGeography.SplitTrailingCity(segments[i]);
                        if (found == null) continue;
                        city = found;
                        if (string.IsNullOrWhiteSpace(before)) segments.RemoveAt(i);
                        else segments[i] = before;
                    }
                }
            }
            if (province == null)
            {
                var index = segments.FindLastIndex(s => ZimbabweGeography.CanonicalProvince(s, countryIsZimbabwe: true) != null && !ZimbabweGeography.IsCity(s));
                if (index >= 0)
                {
                    province = ZimbabweGeography.CanonicalProvince(segments[index], countryIsZimbabwe: true);
                    segments.RemoveAt(index);
                }
            }

            // A city written in the province field ("Harare" in both, or only there) still counts.
            if (city == null && province != null && ZimbabweGeography.IsCity(province))
                city = ZimbabweGeography.CanonicalCity(province);
            // A known suburb names its city ("Borrowdale" is in Harare).
            if (city == null)
                city = segments.Select(ZimbabweGeography.CityForSuburb).LastOrDefault(c => c != null);
            if (province == null && city != null)
                province = ZimbabweGeography.ProvinceForCity(city);

            var (houseNo, street) = HouseAndStreet(segments);
            return new BuyerAddress(houseNo, street, city, province);
        }

        /// <summary>The first piece holding a house number wins; the street is the rest of it, plus the suburb after it.</summary>
        private static (string? HouseNo, string? Street) HouseAndStreet(List<string> segments)
        {
            for (var i = 0; i < segments.Count; i++)
            {
                if (!TryHouseNumber(segments[i], out var house, out var rest)) continue;

                var parts = new List<string>();
                if (!string.IsNullOrWhiteSpace(rest)) parts.Add(rest);
                // "12" on its own line, street on the next: take the next piece as the street.
                var next = i + 1;
                if (parts.Count == 0 && next < segments.Count) parts.Add(segments[next++]);
                // Keep one following piece (the suburb) when it isn't another numbered line.
                if (next < segments.Count && !TryHouseNumber(segments[next], out _, out _)) parts.Add(segments[next]);
                return (house, Limit(string.Join(", ", parts), MaxStreetLength));
            }

            // No house number anywhere: the street is the piece that reads like one, else the first piece.
            if (segments.Count == 0) return (null, null);
            var streetIndex = segments.FindIndex(s => StreetWord.IsMatch(s));
            if (streetIndex < 0) streetIndex = 0;
            var streetParts = new List<string> { segments[streetIndex] };
            if (streetIndex + 1 < segments.Count) streetParts.Add(segments[streetIndex + 1]);
            return (null, Limit(string.Join(", ", streetParts), MaxStreetLength));
        }

        /// <summary>Reads a house/building number off the front (or, after a street word, the end) of one piece.</summary>
        internal static bool TryHouseNumber(string segment, out string? houseNo, out string? rest)
        {
            houseNo = null;
            rest = null;
            var text = segment.Trim();

            var m = Labelled.Match(text);
            if (m.Success)
            {
                var label = m.Groups["label"].Value;
                var num = Compact(m.Groups["num"].Value);
                var keepLabel = !(label.Equals("no", StringComparison.OrdinalIgnoreCase)
                                  || label.Equals("number", StringComparison.OrdinalIgnoreCase)
                                  || label == "#"
                                  || label.Equals("house", StringComparison.OrdinalIgnoreCase));
                var labelled = keepLabel ? $"{Capitalise(label)} {num}" : num;
                houseNo = labelled.Length <= MaxHouseNoLength ? labelled : Limit(num, MaxHouseNoLength);
                rest = Clean(m.Groups["rest"].Value);
                return true;
            }

            m = Leading.Match(text);
            if (m.Success)
            {
                houseNo = Limit(Compact(m.Groups["num"].Value), MaxHouseNoLength);
                rest = Clean(m.Groups["rest"].Value);
                return true;
            }

            m = Trailing.Match(text);
            if (m.Success)
            {
                houseNo = Limit(Compact(m.Groups["num"].Value), MaxHouseNoLength);
                rest = Clean(m.Groups["rest"].Value);
                return true;
            }
            return false;
        }

        private static bool IsZimbabweOrBlank(string? country)
        {
            if (string.IsNullOrWhiteSpace(country)) return true;
            var c = Key(country);
            return c is "zimbabwe" or "zw" or "zwe" or "rhodesia";
        }

        private static bool IsCountry(string s)
        {
            var k = Key(s);
            return k is "zimbabwe" or "zw" or "zwe" or "southafrica" or "rsa" or "za" or "zambia" or "botswana" or "mozambique";
        }

        private static string Compact(string value) => Regex.Replace(value, @"\s+", "").ToUpperInvariant();

        private static string Capitalise(string label)
        {
            var l = label.ToLowerInvariant();
            return l switch
            {
                "apt" or "apartment" => "Apt",
                _ => char.ToUpperInvariant(l[0]) + l[1..]
            };
        }

        private static string? Clean(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var v = Regex.Replace(value, @"\s+", " ").Trim().Trim(',', '-', ':').Trim();
            return v.Length == 0 ? null : v;
        }

        private static string Limit(string value, int max) => value.Length <= max ? value : value[..max].TrimEnd(' ', ',');

        /// <summary>Comparison key: letters and digits only, lower case.</summary>
        internal static string Key(string? value)
            => new string((value ?? string.Empty).Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    }

    /// <summary>Zimbabwe's provinces, main towns and the best-known suburbs, for filling in what an address leaves out.</summary>
    public static class ZimbabweGeography
    {
        private static readonly Dictionary<string, string> Provinces = new()
        {
            ["harare"] = "Harare", ["hararemetropolitan"] = "Harare", ["hararemetropolitanprovince"] = "Harare", ["harareprovince"] = "Harare",
            ["bulawayo"] = "Bulawayo", ["bulawayometropolitan"] = "Bulawayo", ["bulawayometropolitanprovince"] = "Bulawayo", ["bulawayoprovince"] = "Bulawayo",
            ["manicaland"] = "Manicaland",
            ["mashonalandcentral"] = "Mashonaland Central", ["mashcentral"] = "Mashonaland Central", ["mashonalandc"] = "Mashonaland Central",
            ["mashonalandeast"] = "Mashonaland East", ["masheast"] = "Mashonaland East", ["mashonalande"] = "Mashonaland East",
            ["mashonalandwest"] = "Mashonaland West", ["mashwest"] = "Mashonaland West", ["mashonalandw"] = "Mashonaland West",
            ["masvingo"] = "Masvingo",
            ["matabelelandnorth"] = "Matabeleland North", ["matnorth"] = "Matabeleland North", ["matabelelandn"] = "Matabeleland North", ["matebelelandnorth"] = "Matabeleland North",
            ["matabelelandsouth"] = "Matabeleland South", ["matsouth"] = "Matabeleland South", ["matabelelands"] = "Matabeleland South", ["matebelelandsouth"] = "Matabeleland South",
            ["midlands"] = "Midlands",
        };

        // ISO 3166-2:ZW codes, only trusted when the address is in Zimbabwe ("MA" is also Massachusetts).
        private static readonly Dictionary<string, string> ProvinceCodes = new()
        {
            ["ha"] = "Harare", ["bu"] = "Bulawayo", ["ma"] = "Manicaland", ["mc"] = "Mashonaland Central", ["me"] = "Mashonaland East",
            ["mw"] = "Mashonaland West", ["mv"] = "Masvingo", ["mn"] = "Matabeleland North", ["ms"] = "Matabeleland South", ["mi"] = "Midlands",
        };

        private static readonly Dictionary<string, (string City, string Province)> Cities = new()
        {
            ["harare"] = ("Harare", "Harare"), ["chitungwiza"] = ("Chitungwiza", "Harare"), ["epworth"] = ("Epworth", "Harare"),
            ["bulawayo"] = ("Bulawayo", "Bulawayo"),
            ["mutare"] = ("Mutare", "Manicaland"), ["rusape"] = ("Rusape", "Manicaland"), ["chipinge"] = ("Chipinge", "Manicaland"), ["nyanga"] = ("Nyanga", "Manicaland"), ["chimanimani"] = ("Chimanimani", "Manicaland"),
            ["gweru"] = ("Gweru", "Midlands"), ["kwekwe"] = ("Kwekwe", "Midlands"), ["redcliff"] = ("Redcliff", "Midlands"), ["shurugwi"] = ("Shurugwi", "Midlands"), ["zvishavane"] = ("Zvishavane", "Midlands"), ["gokwe"] = ("Gokwe", "Midlands"), ["mvuma"] = ("Mvuma", "Midlands"),
            ["kadoma"] = ("Kadoma", "Mashonaland West"), ["chegutu"] = ("Chegutu", "Mashonaland West"), ["chinhoyi"] = ("Chinhoyi", "Mashonaland West"), ["karoi"] = ("Karoi", "Mashonaland West"), ["kariba"] = ("Kariba", "Mashonaland West"), ["norton"] = ("Norton", "Mashonaland West"), ["banket"] = ("Banket", "Mashonaland West"),
            ["marondera"] = ("Marondera", "Mashonaland East"), ["ruwa"] = ("Ruwa", "Mashonaland East"), ["murehwa"] = ("Murehwa", "Mashonaland East"), ["goromonzi"] = ("Goromonzi", "Mashonaland East"), ["mutoko"] = ("Mutoko", "Mashonaland East"),
            ["bindura"] = ("Bindura", "Mashonaland Central"), ["mvurwi"] = ("Mvurwi", "Mashonaland Central"), ["mountdarwin"] = ("Mount Darwin", "Mashonaland Central"), ["shamva"] = ("Shamva", "Mashonaland Central"), ["glendale"] = ("Glendale", "Mashonaland Central"), ["concession"] = ("Concession", "Mashonaland Central"),
            ["masvingo"] = ("Masvingo", "Masvingo"), ["chiredzi"] = ("Chiredzi", "Masvingo"), ["triangle"] = ("Triangle", "Masvingo"), ["gutu"] = ("Gutu", "Masvingo"),
            ["hwange"] = ("Hwange", "Matabeleland North"), ["victoriafalls"] = ("Victoria Falls", "Matabeleland North"), ["vicfalls"] = ("Victoria Falls", "Matabeleland North"), ["lupane"] = ("Lupane", "Matabeleland North"), ["binga"] = ("Binga", "Matabeleland North"),
            ["gwanda"] = ("Gwanda", "Matabeleland South"), ["beitbridge"] = ("Beitbridge", "Matabeleland South"), ["plumtree"] = ("Plumtree", "Matabeleland South"), ["esigodini"] = ("Esigodini", "Matabeleland South"),
        };

        private static readonly Dictionary<string, string> Suburbs = BuildSuburbs();

        private static Dictionary<string, string> BuildSuburbs()
        {
            var map = new Dictionary<string, string>();
            foreach (var s in new[]
            {
                "Avondale", "Borrowdale", "Borrowdale Brooke", "Msasa", "Workington", "Southerton", "Graniteside", "Belvedere", "Eastlea",
                "Mount Pleasant", "Mt Pleasant", "Highlands", "Greendale", "Marlborough", "Belgravia", "Milton Park", "Waterfalls", "Mabelreign",
                "Hatfield", "Arcadia", "Westgate", "Bluff Hill", "Ardbennie", "Willowvale", "Kuwadzana", "Budiriro", "Glen View", "Glen Norah",
                "Mbare", "Highfield", "Chisipite", "Newlands", "Emerald Hill", "Alexandra Park", "Vainona", "Mandara", "Greystone Park",
                "Gun Hill", "Pomona", "Tynwald", "Kambuzuma", "Warren Park", "Dzivarasekwa", "Mufakose", "Marimba Park", "Hillside Harare",
                "Harare CBD", "Mabvuku", "Tafara", "Greencroft", "Strathaven", "Logan Park", "Ballantyne Park", "Colne Valley", "Ashdown Park",
                "Sunningdale", "Prospect", "Lochinvar", "Aspindale", "Amby", "Ruwa Road", "Chikurubi", "Kensington", "Belvedere North",
            }) map[BuyerAddressParser.Key(s)] = "Harare";
            foreach (var s in new[]
            {
                "Belmont", "Kelvin", "Famona", "Raylton", "Khumalo", "Nkulumane", "Entumbane", "Makokoba", "Steeldale", "Suburbs Bulawayo",
                "Bulawayo CBD", "Hillside Bulawayo", "Burnside", "Bradfield", "Morningside Bulawayo", "Parklands", "Northend", "Luveve",
                "Pumula", "Cowdray Park", "Emganwini", "Magwegwe", "Mpopoma", "Lobengula", "Sauerstown", "Kumalo", "Donnington", "Thorngrove",
            }) map[BuyerAddressParser.Key(s)] = "Bulawayo";
            return map;
        }

        public static bool IsCity(string? value) => value != null && Cities.ContainsKey(BuyerAddressParser.Key(value));

        public static string? CanonicalCity(string? value)
            => value != null && Cities.TryGetValue(BuyerAddressParser.Key(value), out var c) ? c.City : null;

        public static string? ProvinceForCity(string? city)
            => city != null && Cities.TryGetValue(BuyerAddressParser.Key(city), out var c) ? c.Province : null;

        /// <summary>"Msasa Harare" gives ("Msasa", "Harare"); a piece not ending in a known town gives (piece, null).</summary>
        public static (string Before, string? City) SplitTrailingCity(string segment)
        {
            var words = segment.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            // Two-word towns first ("Victoria Falls", "Mount Darwin"); never the whole piece (that case is handled already).
            for (var take = Math.Min(2, words.Length - 1); take >= 1; take--)
            {
                var tail = string.Join(' ', words[^take..]);
                var city = CanonicalCity(tail);
                if (city != null) return (string.Join(' ', words[..^take]).Trim(' ', ','), city);
            }
            return (segment, null);
        }

        public static string? CityForSuburb(string? value)
            => value != null && Suburbs.TryGetValue(BuyerAddressParser.Key(value), out var city) ? city : null;

        /// <summary>The province's full name for a name, short form or (in Zimbabwe) ISO code; null when not recognised.</summary>
        public static string? CanonicalProvince(string? value, bool countryIsZimbabwe)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var key = BuyerAddressParser.Key(value);
            if (key.StartsWith("zw") && key.Length == 4) key = key[2..]; // "ZW-HA"
            if (Provinces.TryGetValue(key, out var province)) return province;
            if (countryIsZimbabwe && key.Length == 2 && ProvinceCodes.TryGetValue(key, out province)) return province;
            return null;
        }
    }
}
