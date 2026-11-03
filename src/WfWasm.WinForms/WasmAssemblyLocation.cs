namespace System.Windows.Forms;

// Browser-side Assembly.Location fallback (the rewriter retargets the
// Syncfusion SR LocalizationResourceAccessor.GetLocalizedResourceManager
// probe here): single-file/wasm loads report "" (no file path), which
// makes Path.GetDirectoryName answer null and kills the downstream
// Path.Combine with ArgumentNullException("path1") — escaping to the
// ctor-guard before the method can reach its embedded-resource fallback.
// Passes real locations through untouched (desktop truth, nothing
// virtualized); only an empty/missing location answers the virtual app
// dir, so the satellite probe path composes and File.Exists skips it
// cleanly instead of throwing.
public static class WasmAssemblyLocation
{
    public static string SafeLocation(System.Reflection.Assembly? assembly)
    {
        string? location = null;
        try
        {
            location = assembly?.Location;
        }
        catch
        {
        }
        if (!string.IsNullOrEmpty(location))
            return location;
        if (OperatingSystem.IsBrowser())
            return "/vfs/app";
        return location ?? "";
    }
}
