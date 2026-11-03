extern alias WfDraw;
using Catalog = WfDraw::System.Drawing.BrowserFontCatalog;
using Family = WfDraw::System.Drawing.FontFamily;

public sealed class BrowserFontCatalogTests
{
    [Fact]
    public void Available_fonts_are_trimmed_deduplicated_and_keep_fallbacks()
    {
        var fonts = Catalog.Normalize([" Arial ", "arial", "Segoe UI", "", " ", new string('x', 129)]);
        Assert.Equal(5, fonts.Length);
        Assert.Single(fonts.Where(f => f.Name.Equals("Arial", StringComparison.OrdinalIgnoreCase)));
        Assert.Contains(fonts, f => f.Name == "Segoe UI");
        Assert.Contains(fonts, f => f.Name == "Microsoft Sans Serif");
        Assert.Contains(fonts, f => f.Name == "Times New Roman");
        Assert.Contains(fonts, f => f.Name == "Courier New");
    }

    [Fact]
    public void Caller_cannot_replace_a_cached_family_through_the_returned_array()
    {
        var first = Family.Families;
        string original = first[0].Name;
        first[0] = new Family("not-an-installed-font");
        Assert.Equal(original, Family.Families[0].Name);
    }
}
