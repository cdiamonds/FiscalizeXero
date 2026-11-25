namespace FiscalizeXero.Fiscalize.Models.PluginLayouts
{
    /// <summary>
    /// The platform's answer to <c>GET api/plugin-layouts/devices/current</c>: the layout package for one plugin, document and
    /// paper, or nothing new when the copy held is current (304). See Fiscalyze docs/PLUGIN_LAYOUTS.md.
    /// </summary>
    public class PluginLayoutCurrentModel
    {
        public long PluginLayoutId { get; set; }
        public int Version { get; set; }
        public string Hash { get; set; } = "";
        /// <summary>The package JSON (design, logo, letterhead, fields used, checksum).</summary>
        public string? Package { get; set; }
        /// <summary>True when the company has no live layout and this is Fiscalize's standard design.</summary>
        public bool IsStandardDesign { get; set; }
    }

    /// <summary>What a layout download came to.</summary>
    public enum PluginLayoutFetchOutcome
    {
        /// <summary>A new or changed package arrived.</summary>
        Updated,
        /// <summary>The copy held is current (304).</summary>
        NotModified,
        /// <summary>Nothing is live for this plugin, document and paper (404).</summary>
        None,
        /// <summary>The platform could not be asked or answered something unexpected: keep what is held.</summary>
        Failed
    }

    public sealed record PluginLayoutFetch(PluginLayoutFetchOutcome Outcome, PluginLayoutCurrentModel? Current, string? Problem = null);

    /// <summary>One ERP field the app can supply to a layout. Names only: no values leave the app to be listed.</summary>
    public class PluginFieldUploadModel
    {
        public string Key { get; set; } = "";
        public string Label { get; set; } = "";
        public string Scope { get; set; } = "header";
        public bool Restricted { get; set; }
        public string? Group { get; set; }
    }

    public class UploadPluginFieldsModel
    {
        public string PluginType { get; set; } = "";
        public List<PluginFieldUploadModel> Fields { get; set; } = new();
    }
}
