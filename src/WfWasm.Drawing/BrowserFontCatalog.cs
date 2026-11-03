using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;

namespace System.Drawing;

// InstalledFontCollection and FontFamily share the browser's available-family
// snapshot. No native handles, filesystem font scan or permission prompt.
internal static partial class BrowserFontCatalog
{
    [JSImport("globalThis.wfwasmFontFamilies")]
    private static partial string ReadFamilies();

    private static FontFamily[]? s_families;
    internal static FontFamily[] Families
    {
        get
        {
            if (s_families is null)
            {
                string[] names = [];
                if (OperatingSystem.IsBrowser())
                    try { names = JsonSerializer.Deserialize<string[]>(ReadFamilies()) ?? []; }
                    catch { }
                s_families = Normalize(names);
            }
            // Callers own the collection array, as on the desktop.
            return (FontFamily[])s_families.Clone();
        }
    }

    internal static FontFamily[] Normalize(IEnumerable<string> names) => names
        .Where(n => !string.IsNullOrWhiteSpace(n) && n.Length <= 128)
        .Select(n => n.Trim())
        .Concat(["Microsoft Sans Serif", "Times New Roman", "Courier New"])
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
        .Select(n => new FontFamily(n)).ToArray();
}
