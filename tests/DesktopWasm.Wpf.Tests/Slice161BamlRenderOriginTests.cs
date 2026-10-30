// Copyright (c) 2026 The WPF on WASM Contributors.
// Licensed under the MIT License. See LICENSE in the project root.

using System.Reflection;
using System.Resources;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DesktopWasm.Wpf;

namespace DesktopWasm.Wpf.Tests;

/// <summary>Slice-161: the keyed 'RightPathStyle' Style entry at BAML+0x1B219
/// routes its qualified RenderTransformOrigin setter (PropertyCustom 16521 at
/// BAML+0x1B22E over the UIElement-owned row plus the "0.5, 0.5" PointConverter
/// PWC), so the staged boot advances past BAML+0x1B22E and stops precise at the
/// RenderTransform short-form PropertyCustom at BAML+0x1B260 (member -134 with
/// the TransformGroup complex, the next slice's work).
/// Live counts verified first: handycontrol conversion-failed/6 staged/2 gaps
/// (both declared absent binaries System.Security.Permissions +
/// System.Windows.Extensions with no browser path; Convert map untouched,
/// all 6 staged images sha256-verified, manifest hand-untouched).
/// Triangulation three ways: staged framing spans against the live 466350-byte
/// theme.baml (sha256 D305CC52C4F8A6EB077D1CC79F556BD343830E30C7C67FC73D3988C63AAF65D0,
/// verified live) - the 172-byte 'RightPathStyle' entry at 0x1B219 (Style head
/// 03 94 FD 00, BasedOn 0x38 over per-file row 92 with static id 0, TargetType
/// 22 00 00 46 FE, Setters 2E 12 FF, the qualified RenderTransformOrigin
/// PropertyCustom 06 1D 01 00 89 40 4A FD over the UIElement-owned (-694) row
/// with the "0.5, 0.5" PointConverter (-474) PWC, the setter close 04, the
/// RenderTransform setter head with short-form member -134 plus the
/// TransformGroup complex, the Data StaticResource setter, closed by 04 04);
/// the live upstream HandyControl Themes/Basic/Paths.xaml block at pinned
/// commit e9ad3d70 (RightPathStyle with RenderTransformOrigin "0.5, 0.5" plus
/// the TransformGroup (ScaleTransform ScaleY=1 ScaleX=-1, SkewTransform 0/0,
/// RotateTransform 0, TranslateTransform) plus Data="{StaticResource
/// LeftGeometry}", fetched live); a genuine net10.0-windows console probe
/// (UIElement.RenderTransformOriginProperty is a DependencyProperty owned by
/// UIElement typed Point defaulting 0,0, with a writable Point CLR property;
/// Path.DataProperty owned by Path typed Geometry; probe retained at
/// E:/tmp/bamlfix161 outside the tree) plus a genuine net10.0-windows
/// ResourceDictionary fixture compiling the upstream PathBaseStyle/
/// SearchPathStyle/RightPathStyle block (Paths.baml: qualified PropertyCustom
/// payload 89 40 4A FD over the UIElement-owned row, "0.5, 0.5" PointConverter
/// PWC 26 FE, RenderTransform short-form member 89 00 7A FF, TransformGroup
/// 56 FD - byte-identical in every staged record shape modulo per-file
/// numbering and the 0x36 line markers the staged file omits; fixture retained
/// at E:/tmp/bamlfix161 outside the tree).
/// Real-behavior engine work, never fabricated: UIElement gains the genuine
/// RenderTransformOriginProperty DP (Point, default 0,0, AffectsRender like the
/// Visibility/Opacity chrome DPs; the CLR property is untouched), so the
/// qualified setter arm resolves through DependencyProperty.FromName like WPF
/// and the value converts through the DP type (Point.Parse takes the staged
/// "0.5, 0.5" spacing); style apply lands it through the Setter CLR-first path
/// like the XAML loader text path; unknown qualified properties and non-Point
/// values stay precise.
/// Re-package skipped by evidence (Convert map untouched, staged bins
/// byte-identical). Tests: 9 new Slice161BamlRenderOriginTests (see below)
/// plus the stop-triple flip with new-stop triples, the Slice160 shape-test
/// flip, and the BamlReader class-doc frontier rewrite.
/// application-compatibility-status.md handycontrol row verified current
/// against the live manifest (conversion-failed/6/2; no regen, nothing
/// re-packaged). No browser proof - package stays conversion-failed.
/// Precise next blockers: the 'RightPathStyle' RenderTransform setter
/// (short-form member -134 PropertyCustom 0x06 at BAML+0x1B260 holding the
/// TransformGroup complex with Scale/Skew/Rotate/Translate children), then the
/// CarouselPateButton/implicit Carousel styles, the GroupBox styles with
/// BasedOn/DynamicResource setters and TitleElement triggers, EventTriggers,
/// the line-info trailer, the per-file (Type,Name) x:Name rows and the
/// converted-lib elements behind the line-23 theme stop.</summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class Slice161BamlRenderOriginTests : IDisposable
{
    private readonly Application? _previous = Application.Current;

    public Slice161BamlRenderOriginTests()
    {
        XamlLoader.CustomElementSource = null;
    }

    public void Dispose()
    {
        XamlLoader.CustomElementSource = null;
        Application.Current = _previous;
    }

    [Fact]
    public void Theme_baml_right_path_entry_bytes()
    {
        LoadStagedHandyControlEntry();
        var theme = ReadStagedGResource("HandyControl", "themes/theme.baml");
        Assert.Equal(466350, theme.Length);
        Assert.Equal(
            "D305CC52C4F8A6EB077D1CC79F556BD343830E30C7C67FC73D3988C63AAF65D0",
            Convert.ToHexString(SHA256.HashData(theme)));

        // The keyed 'RightPathStyle' entry is 172 bytes at BAML+0x1B219: the
        // Style head (well-known type id -620) with the BasedOn StaticResource
        // (attr 92, static id 0) right behind it at BAML+0x1B21D.
        Assert.Equal(172, 0x1B2C5 - 0x1B219);
        Assert.Equal(new byte[] { 0x03, 0x94, 0xFD, 0x00 }, theme[0x1B219..0x1B21D]);
        Assert.Equal(new byte[] { 0x38, 0x5C, 0x00, 0x00, 0x00 }, theme[0x1B21D..0x1B222]);
        // The TargetType PropertyTypeReference (attr 0, Path -442) plus the
        // Style.Setters content member (-238), then the first Setter element.
        Assert.Equal(new byte[] { 0x22, 0x00, 0x00, 0x46, 0xFE }, theme[0x1B222..0x1B227]);
        Assert.Equal(new byte[] { 0x2E, 0x12, 0xFF }, theme[0x1B227..0x1B22A]);
        Assert.Equal(new byte[] { 0x03, 0xD4, 0xFD, 0x00 }, theme[0x1B22A..0x1B22E]);
        // The qualified RenderTransformOrigin PropertyCustom (attr 1,
        // serializer 16521, owner UIElement -694) at BAML+0x1B22E.
        Assert.Equal(
            new byte[] { 0x06, 0x1D, 0x01, 0x00, 0x89, 0x40, 0x4A, 0xFD },
            theme[0x1B22E..0x1B236]);
        Assert.Equal(Encoding.ASCII.GetBytes("RenderTransformOrigin"), theme[0x1B237..0x1B24C]);
        // The "0.5, 0.5" PointConverter PWC (attr 2, converter -474) plus the
        // setter close, then the RenderTransform setter head with the
        // short-form member -134.
        Assert.Equal(new byte[] { 0x24, 0x0E, 0x02, 0x00, 0x08 }, theme[0x1B24C..0x1B251]);
        Assert.Equal(Encoding.ASCII.GetBytes("0.5, 0.5"), theme[0x1B251..0x1B259]);
        Assert.Equal(new byte[] { 0x26, 0xFE, 0x04 }, theme[0x1B259..0x1B25C]);
        Assert.Equal(new byte[] { 0x03, 0xD4, 0xFD, 0x00 }, theme[0x1B25C..0x1B260]);
        Assert.Equal(new byte[] { 0x06, 0x07, 0x01, 0x00, 0x89, 0x00, 0x7A, 0xFF }, theme[0x1B260..0x1B268]);
        // The entry closes with the same Data StaticResource setter shape as
        // the fifteen-entry run (member -87, attr 2, static id 1).
        Assert.Equal(new byte[] { 0x03, 0xD4, 0xFD, 0x00 }, theme[0x1B2B2..0x1B2B6]);
        Assert.Equal(new byte[] { 0x06, 0x07, 0x01, 0x00, 0x89, 0x00, 0xA9, 0xFF }, theme[0x1B2B6..0x1B2BE]);
        Assert.Equal(new byte[] { 0x38, 0x02, 0x00, 0x01, 0x00 }, theme[0x1B2BE..0x1B2C3]);
        Assert.Equal(new byte[] { 0x04, 0x04 }, theme[0x1B2C3..0x1B2C5]);
    }

    [Fact]
    public void Staged_right_path_origin_setter_lands_live_point()
    {
        // The staged entry prefix (head through the RenderTransformOrigin
        // setter close, plus the entry close) parses its BasedOn plus the
        // qualified origin setter with the staged numbering (attr 92 for the
        // base; attr 1 plus serializer 16521 over the UIElement-owned row for
        // the property; attr 2 with the PointConverter for the value). The
        // origin lands eagerly as a live Point (not deferred): the style keeps
        // the base key for apply time like the SearchPathStyle run.
        LoadStagedHandyControlEntry();
        var theme = ReadStagedGResource("HandyControl", "themes/theme.baml");
        var prefix = theme[0x1B219..0x1B25C].Concat(new byte[] { 0x04 }).ToArray();
        var dictionary = BamlReader.LoadDictionary(StagedRightPathDictionary(prefix));
        var style = Assert.IsType<Style>(dictionary["RightPathStyle"]);
        Assert.Same(typeof(Path), style.TargetType);
        Assert.Equal("PathBaseStyle", style.BasedOnKey);
        var setter = Assert.IsType<Setter>(Assert.Single(style.Setters));
        Assert.Same(UIElement.RenderTransformOriginProperty, setter.Property);
        Assert.Equal("UIElement.RenderTransformOrigin", setter.Property.OwnerType.Name + "." + setter.Property.Name);
        Assert.Equal(new Point(0.5, 0.5), setter.Value);
    }

    [Fact]
    public void Staged_right_path_origin_applies_to_path()
    {
        // The staged entries are live, not retained bytes: with the staged
        // PathBaseStyle entry in Application resources, applying the staged
        // RightPathStyle prefix lands the (0.5, 0.5) origin on the CLR slot
        // through the Setter CLR-first path (like the XAML loader text path)
        // plus the Uniform stretch inherited through the deferred BasedOn.
        var app = new Application();
        try
        {
            LoadStagedHandyControlEntry();
            var theme = ReadStagedGResource("HandyControl", "themes/theme.baml");
            var prefix = theme[0x1B219..0x1B25C].Concat(new byte[] { 0x04 }).ToArray();
            var dictionary = BamlReader.LoadDictionary(StagedRightPathDictionary(
                prefix,
                ("PathBaseStyle", theme[0x166FF..0x1677A])));
            var style = Assert.IsType<Style>(dictionary["RightPathStyle"]);
            app.Resources.Add("PathBaseStyle", dictionary["PathBaseStyle"]);
            app.Resources.Add("RightPathStyle", style);
            var path = new Path();
            Assert.Equal(new Point(0, 0), path.RenderTransformOrigin);
            style.ApplyTo(path);
            Assert.Equal(new Point(0.5, 0.5), path.RenderTransformOrigin);
            Assert.Equal(Stretch.Uniform, path.Stretch);
        }
        finally
        {
            Application.Current = _previous;
        }
    }

    [Fact]
    public void Staged_boot_advances_past_render_origin_to_render_transform()
    {
        var entry = LoadStagedHandyControlEntry();
        var error = Assert.Throws<InvalidOperationException>(() => WpfAppBoot.Boot(entry));
        Assert.Contains("startup failed in entry point", error.Message);
        Assert.Contains("line 23", error.Message);
        Assert.Contains("position 26", error.Message);
        var inner = Assert.IsType<XamlLoadException>(error.InnerException);
        Assert.DoesNotContain("TabItemMenu", inner.Message);
        Assert.DoesNotContain("BAML+0x19D99", inner.Message);
        Assert.DoesNotContain("BAML+0x19DCE", inner.Message);
        Assert.DoesNotContain("well-known type id -105", inner.Message);
        Assert.DoesNotContain("Lang.Close", inner.Message);
        Assert.DoesNotContain("throws reading the static property", inner.Message);
        Assert.DoesNotContain("XmlnsDefinitionAttribute", inner.Message);
        Assert.DoesNotContain("ToolBarTrayBaseStyle", inner.Message);
        Assert.DoesNotContain("BAML+0x19E4F", inner.Message);
        Assert.DoesNotContain("well-known type id -672", inner.Message);
        Assert.DoesNotContain("TreeArrow", inner.Message);
        Assert.DoesNotContain("BAML+0x19E7B", inner.Message);
        Assert.DoesNotContain("well-known type id -446", inner.Message);
        Assert.DoesNotContain("RowHeaderGripperStyle", inner.Message);
        Assert.DoesNotContain("BAML+0x1A034", inner.Message);
        Assert.DoesNotContain("'Cursor' is not a dependency property of FrameworkElement", inner.Message);
        Assert.DoesNotContain("DataGridComboBoxColumnStyle", inner.Message);
        Assert.DoesNotContain("BAML+0x1A113", inner.Message);
        Assert.DoesNotContain("well-known type id -92", inner.Message);
        Assert.DoesNotContain("DataGridTextColumnStyle", inner.Message);
        Assert.DoesNotContain("CarouselItemBaseStyle", inner.Message);
        Assert.DoesNotContain("BAML+0x1A186", inner.Message);
        Assert.DoesNotContain("BAML+0x1A21B", inner.Message);
        Assert.DoesNotContain("targets TextBlock", inner.Message);
        Assert.DoesNotContain("template property id -21", inner.Message);
        Assert.DoesNotContain("GrowlWindow", inner.Message);
        Assert.DoesNotContain("BAML+0x1A2CA", inner.Message);
        Assert.DoesNotContain("well-known member id -51", inner.Message);
        Assert.DoesNotContain("ListBoxItemCustom", inner.Message);
        Assert.DoesNotContain("BAML+0x1A368", inner.Message);
        Assert.DoesNotContain("well-known type id -374", inner.Message);
        Assert.DoesNotContain("BAML+0x1A92D", inner.Message);
        Assert.DoesNotContain("well-known member id -81", inner.Message);
        Assert.DoesNotContain("RibbonTab", inner.Message);
        Assert.DoesNotContain("BAML+0x1A990", inner.Message);
        Assert.DoesNotContain("template root panel property 'Visibility' with converter -195", inner.Message);
        Assert.DoesNotContain("on setter 'Template'", inner.Message);
        Assert.DoesNotContain("implicit style key 'HandyControl.Controls.RibbonTab'", inner.Message);
        Assert.DoesNotContain("root panels take the Name string plus Visibility", inner.Message);
        Assert.DoesNotContain("BAML+0x1A9DD", inner.Message);
        Assert.DoesNotContain("element well-known type id -550 on the template root panel", inner.Message);
        Assert.DoesNotContain("root panels take ContentPresenter, Border, converted Shape and TextBlock children", inner.Message);
        Assert.DoesNotContain("on the template ScrollViewer", inner.Message);
        Assert.DoesNotContain("BAML+0x1AA1C", inner.Message);
        Assert.DoesNotContain("LightPrimaryBrush", inner.Message);
        Assert.DoesNotContain("well-known type id -578", inner.Message);
        Assert.DoesNotContain("BAML+0x1AA35", inner.Message);
        Assert.DoesNotContain("'PrimaryBrush'", inner.Message);
        Assert.DoesNotContain("record 0x34 in a LinearGradientBrush", inner.Message);
        Assert.DoesNotContain("BAML+0x1AE91", inner.Message);
        Assert.DoesNotContain("'EffectShadow1'", inner.Message);
        Assert.DoesNotContain("record 0x34 (converted-element values", inner.Message);
        Assert.DoesNotContain("BAML+0x1B001", inner.Message);
        Assert.DoesNotContain("'SearchPathStyle'", inner.Message);
        Assert.DoesNotContain("record 0x38 in a Style", inner.Message);
        Assert.DoesNotContain("BAML+0x1B22E", inner.Message);
        Assert.DoesNotContain("'RenderTransformOrigin' is not a dependency property of UIElement", inner.Message);
        Assert.DoesNotContain("BAML+0x1B260", inner.Message);
        Assert.DoesNotContain("'RightPathStyle'", inner.Message);
        Assert.DoesNotContain("setter property well-known member id -134 in style entry 'RightPathStyle'", inner.Message);
        Assert.DoesNotContain("short-form properties beyond FlowDirection/Template/", inner.Message);
        Assert.DoesNotContain("CarouselPateButton", inner.Message);
        Assert.DoesNotContain("GroupBox", inner.Message);
        Assert.DoesNotContain("templates take a ContentPresenter", inner.Message);
        Assert.DoesNotContain("items panels take a UniformGrid/StackPanel/VirtualizingStackPanel/WrapPanel/DockPanel visual", inner.Message);
    }

    [Fact]
    public void Synthetic_render_origin_setter_lands_live_point()
    {
        // A qualified RenderTransformOrigin setter (serializer 16521 over the
        // well-known UIElement owner with the PointConverter value) lands a
        // live engine Style: the property resolves to the engine DP and the
        // value converts eagerly through the DP type.
        var previous = Application.Current;
        Application.Current = null;
        try
        {
            var dictionary = BamlReader.LoadDictionary(
                StyleDictionary([("S", OriginBody("0.5, 0.5"))], []));
            var style = Assert.IsType<Style>(dictionary["S"]);
            Assert.Same(typeof(Path), style.TargetType);
            var setter = Assert.IsType<Setter>(Assert.Single(style.Setters));
            Assert.Same(UIElement.RenderTransformOriginProperty, setter.Property);
            Assert.Equal(new Point(0.5, 0.5), setter.Value);
        }
        finally
        {
            Application.Current = previous;
        }
    }

    [Fact]
    public void Synthetic_render_origin_apply_sets_clr_origin()
    {
        // The routed setter is live, not retained bytes: applying lands the
        // parsed Point on the CLR slot through the Setter CLR-first path (the
        // DP store keeps its default; readers bind the CLR property like the
        // XAML loader path).
        var previous = Application.Current;
        Application.Current = null;
        try
        {
            var dictionary = BamlReader.LoadDictionary(
                StyleDictionary([("S", OriginBody("0,0"))], []));
            var style = Assert.IsType<Style>(dictionary["S"]);
            var path = new Path();
            Assert.Equal(new Point(0, 0), path.RenderTransformOrigin);
            style.ApplyTo(path);
            Assert.Equal(new Point(0, 0), path.RenderTransformOrigin);
            var moved = BamlReader.LoadDictionary(
                StyleDictionary([("M", OriginBody("0.25, 0.75"))], []));
            var movedStyle = Assert.IsType<Style>(moved["M"]);
            movedStyle.ApplyTo(path);
            Assert.Equal(new Point(0.25, 0.75), path.RenderTransformOrigin);
        }
        finally
        {
            Application.Current = previous;
        }
    }

    [Fact]
    public void Synthetic_unknown_qualified_property_stays_precise()
    {
        // A qualified setter naming a property the owner never registered
        // (here a UIElement-owned 'NoSuchProp') fails naming the property and
        // the owner instead of landing a half-wired setter.
        var previous = Application.Current;
        Application.Current = null;
        try
        {
            var body = new B().Raw(0x03).I16(-620).Raw(0x00)
                .Raw(0x22).I16(0).I16(-442)
                .Raw(0x2E).I16(-238)
                .Raw(0x03).I16(-556).Raw(0x00)
                .Sized(0x06, p => p.I16(1).U16(16521).I16(-694).Str("NoSuchProp"))
                .Sized(0x24, p => p.I16(2).Str("0.5, 0.5").I16(-474))
                .Raw(0x04)
                .Raw(0x04).ToArray();
            var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(
                StyleDictionary([("S", body)], [])));
            Assert.Contains("'NoSuchProp' is not a dependency property of UIElement (setter in style entry 'S')", error.Message);
        }
        finally
        {
            Application.Current = previous;
        }
    }

    [Fact]
    public void Synthetic_bad_render_origin_point_stays_precise()
    {
        // A RenderTransformOrigin value that is not a point fails naming the
        // text and the setter instead of landing a default origin.
        var previous = Application.Current;
        Application.Current = null;
        try
        {
            var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(
                StyleDictionary([("S", OriginBody("half"))], [])));
            Assert.Contains("'half' is not a Point for setter 'UIElement.RenderTransformOrigin' in style entry 'S'", error.Message);
        }
        finally
        {
            Application.Current = previous;
        }
    }

    [Fact]
    public void Style_engine_shape_matches_wpf_render_origin()
    {
        // The routed values match the genuine net10.0-windows shape: the
        // RenderTransformOriginProperty DP is owned by UIElement and typed
        // Point with the 0,0 default, the CLR property stays a writable Point,
        // and Path.DataProperty keeps its owner and type.
        var originProperty = Assert.IsType<DependencyProperty>(typeof(UIElement).GetField(
            "RenderTransformOriginProperty",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.FlattenHierarchy)!.GetValue(null));
        Assert.Same(typeof(UIElement), originProperty.OwnerType);
        Assert.Same(typeof(Point), originProperty.PropertyType);
        Assert.Equal(new Point(0, 0), originProperty.DefaultMetadata.DefaultValue);
        Assert.Same(typeof(Point), typeof(UIElement).GetProperty("RenderTransformOrigin")!.PropertyType);
        Assert.True(typeof(UIElement).GetProperty("RenderTransformOrigin")!.CanWrite);
        Assert.Same(typeof(Path), Path.DataProperty.OwnerType);
        Assert.Same(typeof(Geometry), Path.DataProperty.PropertyType);
    }

    /// <summary>Builds a Path-targeted style body with a single qualified
    /// RenderTransformOrigin setter (the staged RightPathStyle first-setter
    /// shape: qualified PropertyCustom plus the PointConverter value).</summary>
    private static byte[] OriginBody(string text) => new B().Raw(0x03).I16(-620).Raw(0x00)
        .Raw(0x22).I16(0).I16(-442)
        .Raw(0x2E).I16(-238)
        .Raw(0x03).I16(-556).Raw(0x00)
        .Sized(0x06, p => p.I16(1).U16(16521).I16(-694).Str("RenderTransformOrigin"))
        .Sized(0x24, p => p.I16(2).Str(text).I16(-474))
        .Raw(0x04)
        .Raw(0x04).ToArray();

    /// <summary>Builds a single-deferable dictionary with the Style
    /// AttributeInfo rows (TargetType 0, Property 1, Value 2) plus optional
    /// per-file TypeInfo rows. Each entry carries its own key-block statics
    /// (the 0x37 OptimizedStaticResource declarations ride the key like the
    /// staged file). Entry offsets are content-relative like the staged file.</summary>
    private static byte[] StyleDictionary((string Key, byte[] Entry, string[] Statics)[] entries)
    {
        var strings = new List<string>();
        foreach (var (key, _, statics) in entries)
        {
            if (!strings.Contains(key)) strings.Add(key);
            foreach (var name in statics)
                if (!strings.Contains(name)) strings.Add(name);
        }
        var head = StyleTables(strings.ToArray(), []);
        var positions = new List<int>();
        var content = new B();
        foreach (var (_, body, _) in entries)
        {
            positions.Add(content.Position);
            content.Raw(body);
        }
        head.Deferable();
        for (var i = 0; i < entries.Length; i++)
        {
            var position = positions[i];
            head.Sized(0x26, p => p.U16((ushort)strings.IndexOf(entries[i].Key)).U32((uint)position).U16(0));
            foreach (var name in entries[i].Statics)
                head.Raw(0x37, 0x00).U16((ushort)strings.IndexOf(name));
        }
        return head.Raw(content.ToArray()).FinishDictionary().ToArray();
    }

    private static byte[] StyleDictionary((string Key, byte[] Entry)[] entries, string[] staticNames) =>
        StyleDictionary(entries.Select(e => (e.Key, e.Entry, staticNames)).ToArray());

    /// <summary>Builds the staged-numbering dictionary for the sliced
    /// RightPathStyle prefix (AttributeInfo 0/1/2/92 plus StringInfo 0/1 for
    /// the entry key and its BasedOn static name, with the 0x37 declaration
    /// riding the RightPathStyle key like the staged file), plus optional
    /// extra shared entries.</summary>
    private static byte[] StagedRightPathDictionary(byte[] prefix, params (string Key, byte[] Entry)[] extras)
    {
        var head = new B().Raw(Header()).Raw(0x01)
            .Raw(0x03).I16(-524).Raw(0x00)
            .Sized(0x1F, p => p.I16(0).I16(-620).Raw(0x00).Str("TargetType"))
            .Sized(0x1F, p => p.I16(1).I16(-556).Raw(0x00).Str("Property"))
            .Sized(0x1F, p => p.I16(2).I16(-556).Raw(0x00).Str("Value"))
            .Sized(0x1F, p => p.I16(92).I16(-620).Raw(0x00).Str("BasedOn"))
            .Sized(0x20, p => p.U16(0).Str("RightPathStyle"))
            .Sized(0x20, p => p.U16(1).Str("PathBaseStyle"));
        for (var i = 0; i < extras.Length; i++)
            head.Sized(0x20, p => p.U16((ushort)(2 + i)).Str(extras[i].Key));
        var content = new B();
        var positions = new List<int> { 0 };
        content.Raw(prefix);
        foreach (var (_, body) in extras)
        {
            positions.Add(content.Position);
            content.Raw(body);
        }
        head.Deferable();
        head.Sized(0x26, p => p.U16(0).U32((uint)positions[0]).U16(0));
        head.Raw(0x37, 0x00).U16(1);
        for (var i = 0; i < extras.Length; i++)
            head.Sized(0x26, p => p.U16((ushort)(2 + i)).U32((uint)positions[1 + i]).U16(0));
        return head.Raw(content.ToArray()).FinishDictionary().ToArray();
    }

    private static B StyleTables(string[] names, string[] types)
    {
        // Per-file ids stand in for the staged rows (AttributeInfo 0/1/2 are
        // the Style/Setter TargetType/Property/Value rows).
        var head = new B().Raw(Header()).Raw(0x01)
            .Raw(0x03).I16(-524).Raw(0x00)
            .Sized(0x1F, p => p.I16(0).I16(-620).Raw(0x00).Str("TargetType"))
            .Sized(0x1F, p => p.I16(1).I16(-556).Raw(0x00).Str("Property"))
            .Sized(0x1F, p => p.I16(2).I16(-556).Raw(0x00).Str("Value"));
        foreach (var name in names)
            head.Sized(0x20, p => p.U16((ushort)Array.IndexOf(names, name)).Str(name));
        _ = types;
        return head;
    }

    private static byte[] Header() => new B()
        .Raw(0x0C, 0x00, 0x00, 0x00)
        .Raw(0x4D, 0x00, 0x53, 0x00, 0x42, 0x00, 0x41, 0x00, 0x4D, 0x00, 0x4C, 0x00)
        .U16(0).U16(0x60)
        .Raw(0x00, 0x00, 0x60, 0x00, 0x00, 0x00, 0x60, 0x00,
             0x01, 0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0x00)
        .ToArray();

    private static byte[] ReadStagedGResource(string assemblyShortName, string key)
    {
        var asm = AppDomain.CurrentDomain.GetAssemblies()
            .First(a => string.Equals(a.GetName().Name, assemblyShortName, StringComparison.OrdinalIgnoreCase));
        foreach (var name in asm.GetManifestResourceNames().Where(n => n.EndsWith(".g.resources", StringComparison.OrdinalIgnoreCase)))
        {
            using var stream = asm.GetManifestResourceStream(name)!;
            using var reader = new ResourceReader(stream);
            foreach (System.Collections.DictionaryEntry entry in reader)
            {
                if (entry.Key is string k && k.Replace('\\', '/').Equals(key, StringComparison.OrdinalIgnoreCase))
                {
                    if (entry.Value is byte[] raw) return raw;
                    if (entry.Value is Stream s) { using var copy = new MemoryStream(); s.CopyTo(copy); return copy.ToArray(); }
                }
            }
        }
        throw new InvalidOperationException("resource not found: " + key);
    }

    private static string StagedHandyAppsDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(System.IO.Path.Combine(
            directory.FullName, "src", "DesktopWasm.BrowserHost", "wwwroot", "apps", "handycontrol", "app-manifest.json")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return System.IO.Path.Combine(directory!.FullName, "src", "DesktopWasm.BrowserHost", "wwwroot", "apps", "handycontrol");
    }

    private static readonly object StagedGate = new();
    private static Assembly? _stagedEntry;

    private static Assembly LoadStagedHandyControlEntry()
    {
        lock (StagedGate)
        {
            var hit = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => string.Equals(a.GetName().Name, "HandyControlDemo", StringComparison.OrdinalIgnoreCase));
            if (hit is not null)
            {
                VerifyStagedHashes();
                return hit;
            }
            if (_stagedEntry is not null) return _stagedEntry;
            var apps = StagedHandyAppsDirectory();
            using var document = JsonDocument.Parse(
                File.ReadAllBytes(System.IO.Path.Combine(apps, "app-manifest.json")));
            var images = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var assembly in document.RootElement.GetProperty("assemblies").EnumerateArray())
            {
                var file = assembly.GetProperty("file").GetString()!;
                var expected = assembly.GetProperty("sha256").GetString()!;
                var bytes = File.ReadAllBytes(System.IO.Path.Combine(apps, file));
                var actual = Convert.ToHexString(SHA256.HashData(bytes));
                Assert.True(string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase),
                    $"hash mismatch in staged '{file}': {actual} != {expected}");
                images[assembly.GetProperty("name").GetString()!] = bytes;
            }
            AppDomain.CurrentDomain.AssemblyResolve += (_, args) =>
            {
                var requested = new AssemblyName(args.Name).Name;
                var loaded = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => string.Equals(a.GetName().Name, requested, StringComparison.OrdinalIgnoreCase));
                if (loaded is not null) return loaded;
                if (requested is not null && images.TryGetValue(requested, out var image))
                    return Assembly.Load(image);
                return null;
            };
            foreach (var image in images.Values)
                Assembly.Load(image);
            _stagedEntry = AppDomain.CurrentDomain.GetAssemblies()
                .First(a => string.Equals(a.GetName().Name, "HandyControlDemo", StringComparison.OrdinalIgnoreCase));
            return _stagedEntry;
        }
    }

    private static void VerifyStagedHashes()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(System.IO.Path.Combine(
            directory.FullName, "src", "DesktopWasm.BrowserHost", "wwwroot", "apps", "handycontrol", "app-manifest.json")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var apps = System.IO.Path.Combine(directory!.FullName, "src", "DesktopWasm.BrowserHost", "wwwroot", "apps", "handycontrol");
        using var document = JsonDocument.Parse(File.ReadAllText(System.IO.Path.Combine(apps, "app-manifest.json")));
        foreach (var assembly in document.RootElement.GetProperty("assemblies").EnumerateArray())
        {
            var file = assembly.GetProperty("file").GetString()!;
            var expected = assembly.GetProperty("sha256").GetString()!;
            var actual = Convert.ToHexString(SHA256.HashData(
                File.ReadAllBytes(System.IO.Path.Combine(apps, file))));
            Assert.True(string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase),
                $"hash mismatch in staged '{file}': {actual} != {expected}");
        }
    }

    private sealed class B
    {
        private readonly List<byte> _b = new();

        public int Position => _b.Count;

        public B Raw(params byte[] bytes) { _b.AddRange(bytes); return this; }

        public B Raw(B other) { _b.AddRange(other._b); return this; }

        public B U16(ushort value) { _b.Add((byte)value); _b.Add((byte)(value >> 8)); return this; }

        public B I16(short value) => U16((ushort)value);

        public B U32(uint value)
        {
            _b.Add((byte)value);
            _b.Add((byte)(value >> 8));
            _b.Add((byte)(value >> 16));
            _b.Add((byte)(value >> 24));
            return this;
        }

        public B Str(string value)
        {
            var ascii = Encoding.ASCII.GetBytes(value);
            Seven(ascii.Length);
            _b.AddRange(ascii);
            return this;
        }

        public B Seven(int value)
        {
            var v = (uint)value;
            while (v >= 0x80) { _b.Add((byte)(v | 0x80)); v >>= 7; }
            _b.Add((byte)v);
            return this;
        }

        public B Sized(byte record, Func<B, B> body)
        {
            var inner = body(new B()).ToArray();
            Raw(record);
            Seven(inner.Length + 1);
            _b.AddRange(inner);
            return this;
        }

        public B Deferable()
        {
            _pos = _b.Count;
            _b.AddRange(new byte[] { 0x25, 0x00, 0x00, 0x00, 0x00 });
            return this;
        }

        public B FinishDictionary()
        {
            if (_pos < 0) throw new InvalidOperationException("no open deferable");
            var endPos = _b.Count;
            _b.Add(0x04);
            var skip = (uint)(endPos - (_pos + 5));
            var patch = BitConverter.GetBytes(skip);
            for (var i = 0; i < 4; i++) _b[_pos + 1 + i] = patch[i];
            _b.Add(0x02);
            return this;
        }

        public byte[] ToArray() => _b.ToArray();

        private int _pos = -1;
    }
}
