using Fiscalize.LayoutRenderer;

namespace FiscalizeXero.Api.Services.Layouts
{
    /// <summary>
    /// Draws a layout package with the shared Fiscalize renderer (PdfSharpCore). Its fonts are installed once per process,
    /// before the first drawing, as the renderer requires. This is separate from the stamp (PDFsharp 6), which has its own fonts.
    /// </summary>
    public static class FiscalLayoutRenderer
    {
        private static readonly Lazy<bool> Fonts = new(() => { EmbeddedFontResolver.Install(); return true; });

        public static byte[] Render(LayoutPackage package, LayoutDocument document)
        {
            _ = Fonts.Value;
            return package.Render(document);
        }
    }
}
