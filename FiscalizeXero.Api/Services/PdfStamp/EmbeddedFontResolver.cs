using PdfSharp.Fonts;

namespace FiscalizeXero.Api.Services.PdfStamp
{
    /// <summary>
    /// Serves the Liberation fonts embedded in this assembly (Fonts/*.ttf, SIL Open Font License) to PDFsharp.
    /// Liberation Sans and Mono are metric-compatible with Arial and Courier New, so the stamp renders the same on
    /// the Windows server, on Linux and on macOS, and never depends on fonts installed on the host.
    /// </summary>
    internal sealed class EmbeddedFontResolver : IFontResolver
    {
        private const string Sans = "LiberationSans-Regular";
        private const string SansBold = "LiberationSans-Bold";
        private const string Mono = "LiberationMono-Regular";

        private static readonly Dictionary<string, byte[]> Cache = new();

        public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic)
        {
            var mono = familyName.Contains("Courier", StringComparison.OrdinalIgnoreCase)
                    || familyName.Contains("Mono", StringComparison.OrdinalIgnoreCase);
            var face = mono ? Mono : bold ? SansBold : Sans;
            // Only Sans ships a real bold face; anything else bold/italic is simulated by PDFsharp.
            return new FontResolverInfo(face, mustSimulateBold: bold && face != SansBold, mustSimulateItalic: italic);
        }

        public byte[]? GetFont(string faceName)
        {
            lock (Cache)
            {
                if (Cache.TryGetValue(faceName, out var cached)) return cached;

                var asm = typeof(EmbeddedFontResolver).Assembly;
                var resource = asm.GetManifestResourceNames()
                    .FirstOrDefault(n => n.EndsWith($".{faceName}.ttf", StringComparison.Ordinal));
                if (resource == null) return null;

                using var stream = asm.GetManifestResourceStream(resource)!;
                using var ms = new MemoryStream();
                stream.CopyTo(ms);
                var bytes = ms.ToArray();
                Cache[faceName] = bytes;
                return bytes;
            }
        }
    }
}
