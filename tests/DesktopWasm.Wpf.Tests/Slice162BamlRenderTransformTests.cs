// Copyright (c) 2026 The WPF on WASM Contributors.
// Licensed under the MIT License. See LICENSE in the project root.

using System.Reflection;
using System.Resources;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DesktopWasm.Wpf;

namespace DesktopWasm.Wpf.Tests;

/// <summary>Slice-162: the keyed 'RightPathStyle' Style entry at BAML+0x1B219
/// routes its RenderTransform short-form setter (PropertyCustom 137 with
/// member -134 at BAML+0x1B260 over the PropertyComplexStart/TransformGroup
/// complex at BAML+0x1B268), so the staged boot advances past BAML+0x1B260
/// and stops precise at the qualified Border-owned CornerRadius setter at
/// BAML+0x1B2F9 in style entry 'BorderRegion'.
/// Live counts verified first: handycontrol conversion-failed/6 staged/2 gaps
/// (both declared absent binaries System.Security.Permissions +
/// System.Windows.Extensions with no browser path; Convert map untouched,
/// all 6 staged images sha256-verified, manifest hand-untouched).
/// Triangulation three ways: staged framing spans against the live 466350-byte
/// theme.baml (sha256 D305CC52C4F8A6EB077D1CC79F556BD343830E30C7C67FC73D3988C63AAF65D0,
/// verified live) - the RenderTransform short-form setter head
/// 06 07 01 00 89 00 7A FF at BAML+0x1B260, the 70-byte TransformGroup complex
/// at BAML+0x1B268 (PropertyComplexStart 07 02 00, TransformGroup 03 56 FD 00,
/// Children 2E 7E FF, ScaleTransform 03 DE FD 00 with ScaleY "1"/ScaleX "-1"
/// DoubleConverter PWCs, SkewTransform 03 C1 FD 00 with AngleY/AngleX "0",
/// RotateTransform 03 F0 FD 00 with Angle "0", empty TranslateTransform
/// 03 54 FD 00, the 04 04 08 04 close run, the Data StaticResource setter plus
/// entry close at BAML+0x1B2B2), the per-file AttributeInfo rows 93 ScaleY at
/// 0x2F85 plus 94 ScaleX at 0x2F93 (both owned by ScaleTransform -546), 95
/// AngleY at 0x2FA1 plus 96 AngleX at 0x2FAF (both owned by SkewTransform
/// -575) and 36 Angle at 0x1C60 (owned by RotateTransform -528), the next
/// Path-style entry head at BAML+0x1B2C5, the keyed 'BorderRegion' Border
/// entry head 03 94 FD 00 22 00 00 CE FF at BAML+0x1B2EF plus the qualified
/// CornerRadius PropertyCustom 06 14 01 00 89 40 CE FF with the 12-char
/// "CornerRadius" name at BAML+0x1B2F9;
/// the live upstream HandyControl Themes/Basic/Paths.xaml block (RightPathStyle
/// with the TransformGroup ScaleY=1/ScaleX=-1 plus Skew 0/0 plus Rotate 0 plus
/// empty Translate, carried by the retained E:/tmp/bamlfix161/Paths.xaml
/// fixture verified live in slice-161; the live fetch 404s in this sandbox so
/// the pinned-commit bytes are re-verified through the fixture instead);
/// a genuine net10.0-windows ResourceDictionary fixture compiling that upstream
/// block (Paths.baml at E:/tmp/bamlfix161: identical RenderTransform short-form
/// 89 00 7A FF, TransformGroup 56 FD with Children 7E FF, ScaleTransform DE FD
/// with ScaleY/ScaleX DoubleConverter PWCs, SkewTransform C1 FD, RotateTransform
/// F0 FD, TranslateTransform 54 FD - byte-identical in every staged record shape
/// modulo per-file numbering and the 0x35/0x36 line markers the staged file omits)
/// plus a genuine net10.0-windows reflection probe at E:/tmp/bamlfix162
/// (UIElement.RenderTransformProperty owned by UIElement typed Transform with the
/// Identity default and a writable Transform CLR property; ScaleX/ScaleY owned by
/// ScaleTransform defaulting 1; AngleX/AngleY, Angle, X/Y defaulting 0;
/// TransformGroup.Children a TransformCollection).
/// Real-behavior engine work, never fabricated: UIElement gains the genuine
/// RenderTransformProperty DP (Transform, default Identity, AffectsRender like the
/// RenderTransformOrigin chrome DP; the CLR property is untouched), the short-form
/// -134 arm resolves through DependencyProperty.FromName like WPF, and the complex
/// setter value parses the TransformGroup with per-file owner+name attribute
/// resolution (ScaleX/ScaleY, AngleX/AngleY, Angle required; X/Y optional) landing
/// as the live engine group; style apply lands it through the Setter CLR-first path
/// like the XAML loader text path; unknown children, missing/duplicate values,
/// garbage text, wrong converters and mis-owned attributes stay precise.
/// Re-package skipped by evidence (Convert map untouched, staged bins
/// byte-identical). Tests: 16 new Slice162BamlRenderTransformTests (see below)
/// plus the stop-triple flip with new-stop triples, the Slice128 full short-list
/// extension and the two complex-setter contract re-pins.
/// application-compatibility-status.md handycontrol row verified current
/// against the live manifest (conversion-failed/6/2; no regen, nothing
/// re-packaged). No browser proof - package stays conversion-failed.
/// Precise next blockers: the keyed 'BorderRegion' Style entry (qualified
/// Border.CornerRadius PropertyCustom 0x06 at BAML+0x1B2F9 - Border is outside
/// the routed owner set), then the CarouselPateButton/implicit Carousel styles,
/// the GroupBox styles with BasedOn/DynamicResource setters and TitleElement
/// triggers, EventTriggers, the line-info trailer, the per-file (Type,Name)
/// x:Name rows and the converted-lib elements behind the line-23 theme stop.</summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class Slice162BamlRenderTransformTests : IDisposable
{
    private readonly Application? _previous = Application.Current;

    public Slice162BamlRenderTransformTests()
    {
        XamlLoader.CustomElementSource = null;
    }

    public void Dispose()
    {
        XamlLoader.CustomElementSource = null;
        Application.Current = _previous;
    }

    [Fact]
    public void Theme_baml_right_path_transform_bytes()
    {
        LoadStagedHandyControlEntry();
        var theme = ReadStagedGResource("HandyControl", "themes/theme.baml");
        Assert.Equal(466350, theme.Length);
        Assert.Equal(
            "D305CC52C4F8A6EB077D1CC79F556BD343830E30C7C67FC73D3988C63AAF65D0",
            Convert.ToHexString(SHA256.HashData(theme)));

        // The RenderTransform short-form setter head at BAML+0x1B260 plus the
        // 70-byte TransformGroup complex at BAML+0x1B268: the complex start
        // over the Setter.Value row, the TransformGroup head with the Children
        // content member, the ScaleTransform child with the ScaleY "1" plus
        // ScaleX "-1" DoubleConverter PWCs.
        Assert.Equal(new byte[] { 0x06, 0x07, 0x01, 0x00, 0x89, 0x00, 0x7A, 0xFF }, theme[0x1B260..0x1B268]);
        Assert.Equal(new byte[] { 0x07, 0x02, 0x00, 0x03, 0x56, 0xFD, 0x00, 0x2E, 0x7E, 0xFF, 0x03, 0xDE, 0xFD, 0x00, 0x24, 0x07, 0x5D, 0x00, 0x01, 0x31, 0x51, 0xFF, 0x24, 0x08 }, theme[0x1B268..0x1B280]);
        // The ScaleX "-1" tail plus the SkewTransform child with the AngleY
        // "0" plus AngleX "0" DoubleConverter PWCs.
        Assert.Equal(new byte[] { 0x5E, 0x00, 0x02, 0x2D, 0x31, 0x51, 0xFF, 0x04, 0x03, 0xC1, 0xFD, 0x00, 0x24, 0x07, 0x5F, 0x00, 0x01, 0x30, 0x51, 0xFF, 0x24, 0x07, 0x60, 0x00 }, theme[0x1B280..0x1B298]);
        // The AngleX "0" tail plus the RotateTransform child with Angle "0"
        // plus the empty TranslateTransform child.
        Assert.Equal(new byte[] { 0x01, 0x30, 0x51, 0xFF, 0x04, 0x03, 0xF0, 0xFD, 0x00, 0x24, 0x07, 0x24, 0x00, 0x01, 0x30, 0x51, 0xFF, 0x04, 0x03, 0x54, 0xFD, 0x00 }, theme[0x1B298..0x1B2AE]);
        // The group/element/complex/setter close run plus the Data setter and
        // the entry close at BAML+0x1B2C5.
        Assert.Equal(new byte[] { 0x04, 0x04 }, theme[0x1B2AE..0x1B2B0]);
        Assert.Equal(new byte[] { 0x08, 0x04, 0x03, 0xD4, 0xFD, 0x00, 0x06, 0x07, 0x01, 0x00, 0x89, 0x00, 0xA9, 0xFF, 0x38, 0x02, 0x00, 0x01, 0x00, 0x04, 0x04 }, theme[0x1B2B0..0x1B2C5]);
        // The per-file AttributeInfo rows the complex rides: ScaleY/ScaleX
        // owned by ScaleTransform (-546), AngleY/AngleX owned by SkewTransform
        // (-575), Angle owned by RotateTransform (-528).
        Assert.Equal(new byte[] { 0x1F, 0x0D, 0x5D, 0x00, 0xDE, 0xFD, 0x00, 0x06 }, theme[0x2F85..0x2F8D]);
        Assert.Equal(Encoding.ASCII.GetBytes("ScaleY"), theme[0x2F8D..0x2F93]);
        Assert.Equal(new byte[] { 0x1F, 0x0D, 0x5E, 0x00, 0xDE, 0xFD, 0x00, 0x06 }, theme[0x2F93..0x2F9B]);
        Assert.Equal(Encoding.ASCII.GetBytes("ScaleX"), theme[0x2F9B..0x2FA1]);
        Assert.Equal(new byte[] { 0x1F, 0x0D, 0x5F, 0x00, 0xC1, 0xFD, 0x00, 0x06 }, theme[0x2FA1..0x2FA9]);
        Assert.Equal(Encoding.ASCII.GetBytes("AngleY"), theme[0x2FA9..0x2FAF]);
        Assert.Equal(new byte[] { 0x1F, 0x0D, 0x60, 0x00, 0xC1, 0xFD, 0x00, 0x06 }, theme[0x2FAF..0x2FB7]);
        Assert.Equal(Encoding.ASCII.GetBytes("AngleX"), theme[0x2FB7..0x2FBD]);
        Assert.Equal(new byte[] { 0x1F, 0x0C, 0x24, 0x00, 0xF0, 0xFD, 0x00, 0x05 }, theme[0x1C60..0x1C68]);
        Assert.Equal(Encoding.ASCII.GetBytes("Angle"), theme[0x1C68..0x1C6D]);
    }

    [Fact]
    public void Staged_right_path_full_entry_lands_group()
    {
        // The full 172-byte staged entry parses its BasedOn plus the qualified
        // origin setter plus the RenderTransform short-form setter (member
        // -134 with the staged numbering: row 92 for the base, rows 1/137 for
        // the property, rows 93-96 plus 36 for the transform children) plus
        // the Data StaticResource setter. The origin lands eagerly as a live
        // Point, the group lands eagerly with the mirror-flip values, and the
        // Data setter stays deferred for apply time like the XAML loader path.
        LoadStagedHandyControlEntry();
        var theme = ReadStagedGResource("HandyControl", "themes/theme.baml");
        var dictionary = BamlReader.LoadDictionary(
            StagedRightPathFullDictionary(theme[0x1B219..0x1B2C5]));
        var style = Assert.IsType<Style>(dictionary["RightPathStyle"]);
        Assert.Same(typeof(Path), style.TargetType);
        Assert.Equal("PathBaseStyle", style.BasedOnKey);
        Assert.Equal(3, style.Setters.Count);
        var origin = Assert.IsType<Setter>(style.Setters[0]);
        Assert.Same(UIElement.RenderTransformOriginProperty, origin.Property);
        Assert.Equal(new Point(0.5, 0.5), origin.Value);
        var transform = Assert.IsType<Setter>(style.Setters[1]);
        Assert.Same(UIElement.RenderTransformProperty, transform.Property);
        Assert.Equal("UIElement.RenderTransform", transform.Property.OwnerType.Name + "." + transform.Property.Name);
        var group = Assert.IsType<TransformGroup>(transform.Value);
        Assert.Equal(4, group.Children.Count);
        var scale = Assert.IsType<ScaleTransform>(group.Children[0]);
        Assert.Equal(-1, scale.ScaleX);
        Assert.Equal(1, scale.ScaleY);
        var skew = Assert.IsType<SkewTransform>(group.Children[1]);
        Assert.Equal(0, skew.AngleX);
        Assert.Equal(0, skew.AngleY);
        var rotate = Assert.IsType<RotateTransform>(group.Children[2]);
        Assert.Equal(0, rotate.Angle);
        var translate = Assert.IsType<TranslateTransform>(group.Children[3]);
        Assert.Equal(0, translate.X);
        Assert.Equal(0, translate.Y);
        var data = Assert.IsType<Setter>(style.Setters[2]);
        Assert.Same(Path.DataProperty, data.Property);
        Assert.Equal("{StaticResource LeftGeometry}", data.RawValue);
    }

    [Fact]
    public void Staged_right_path_transform_applies_to_path()
    {
        // The staged entry is live, not retained bytes: with the staged
        // PathBaseStyle entry plus a staged frozen geometry re-keyed as
        // "LeftGeometry" (the staged LeftGeometry figure itself is an unopened
        // follow-up span; the SearchGeometry entry at BAML+0x126A6 is the same
        // frozen-GeometryConverter shape) in Application resources, applying
        // the staged RightPathStyle lands the mirror-flip group on the CLR
        // slot through the Setter CLR-first path (the group Value matrix
        // mirrors X), the (0.5, 0.5) origin, the frozen geometry and the
        // Uniform stretch inherited through the deferred BasedOn.
        var app = new Application();
        try
        {
            LoadStagedHandyControlEntry();
            var theme = ReadStagedGResource("HandyControl", "themes/theme.baml");
            var dictionary = BamlReader.LoadDictionary(StagedRightPathFullDictionary(
                theme[0x1B219..0x1B2C5],
                ("PathBaseStyle", theme[0x166FF..0x1677A]),
                ("LeftGeometry", theme[0x126A6..0x128A6])));
            var style = Assert.IsType<Style>(dictionary["RightPathStyle"]);
            app.Resources.Add("PathBaseStyle", dictionary["PathBaseStyle"]);
            app.Resources.Add("LeftGeometry", dictionary["LeftGeometry"]);
            var path = new Path();
            Assert.Null(path.RenderTransform);
            Assert.Equal(new Point(0, 0), path.RenderTransformOrigin);
            style.ApplyTo(path);
            Assert.Equal(new Point(0.5, 0.5), path.RenderTransformOrigin);
            var applied = Assert.IsType<TransformGroup>(path.RenderTransform);
            Assert.Equal(4, applied.Children.Count);
            Assert.Equal(-1, applied.Value.M11);
            Assert.Equal(1, applied.Value.M22);
            var geometry = Assert.IsAssignableFrom<Geometry>(path.Data);
            Assert.True(geometry.IsFrozen);
            Assert.True(geometry.Bounds.Width > 0);
            Assert.Equal(Stretch.Uniform, path.Stretch);
        }
        finally
        {
            Application.Current = _previous;
        }
    }

    [Fact]
    public void Staged_boot_advances_past_render_transform_to_border_region()
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
        Assert.DoesNotContain("BAML+0x1A9DD", inner.Message);
        Assert.DoesNotContain("element well-known type id -550 on the template root panel", inner.Message);
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
        Assert.DoesNotContain("'RightPathStyle'", inner.Message);
        Assert.DoesNotContain("'RenderTransformOrigin' is not a dependency property of UIElement", inner.Message);
        Assert.DoesNotContain("BAML+0x1B260", inner.Message);
        Assert.DoesNotContain("setter property well-known member id -134 in style entry 'RightPathStyle'", inner.Message);
        Assert.DoesNotContain("short-form properties beyond FlowDirection/Template/", inner.Message);
        Assert.Contains("'BorderRegion'", inner.Message);
        Assert.Contains("BAML+0x1B2F9", inner.Message);
        Assert.Contains("setter property owner Border in style entry 'BorderRegion'", inner.Message);
        Assert.DoesNotContain("CarouselPateButton", inner.Message);
        Assert.DoesNotContain("GroupBox", inner.Message);
        Assert.DoesNotContain("templates take a ContentPresenter", inner.Message);
        Assert.DoesNotContain("items panels take a UniformGrid/StackPanel/VirtualizingStackPanel/WrapPanel/DockPanel visual", inner.Message);
    }

    [Fact]
    public void Staged_border_region_head_bytes()
    {
        // The next stop's framing: the Path-style entry at BAML+0x1B2C5 (same
        // BasedOn/TargetType/Data-static shape as the fifteen-entry run, which
        // the boot parses through), then the keyed 'BorderRegion' Border entry
        // head at BAML+0x1B2EF with the qualified CornerRadius PropertyCustom
        // at BAML+0x1B2F9 (serializer 16521 over the Border-owned (-50) row
        // with the 12-char "CornerRadius" name) plus the StaticResource value.
        LoadStagedHandyControlEntry();
        var theme = ReadStagedGResource("HandyControl", "themes/theme.baml");
        Assert.Equal(new byte[] { 0x03, 0x94, 0xFD, 0x00, 0x38, 0x5C, 0x00, 0x00, 0x00, 0x22, 0x00, 0x00, 0x46, 0xFE, 0x2E, 0x12, 0xFF, 0x03, 0xD4, 0xFD, 0x00, 0x06, 0x07, 0x01 }, theme[0x1B2C5..0x1B2DD]);
        Assert.Equal(new byte[] { 0x00, 0x89, 0x00, 0xA9, 0xFF, 0x38, 0x02, 0x00, 0x01, 0x00, 0x04, 0x04, 0x03, 0x94, 0xFD, 0x00, 0x22, 0x00, 0x00, 0xCE, 0xFF, 0x2E, 0x12, 0xFF }, theme[0x1B2DD..0x1B2F5]);
        Assert.Equal(new byte[] { 0x03, 0xD4, 0xFD, 0x00, 0x06, 0x14, 0x01, 0x00, 0x89, 0x40, 0xCE, 0xFF, 0x0C }, theme[0x1B2F5..0x1B302]);
        Assert.Equal(Encoding.ASCII.GetBytes("CornerRadius"), theme[0x1B302..0x1B30E]);
        Assert.Equal(new byte[] { 0x38, 0x02, 0x00, 0x00, 0x00, 0x04, 0x03, 0xD4, 0xFD, 0x00, 0x06, 0x0F, 0x01, 0x00, 0x89, 0x40, 0xCE, 0xFF }, theme[0x1B30E..0x1B320]);
    }

    [Fact]
    public void Synthetic_render_transform_lands_group()
    {
        // A RenderTransform short-form setter (member -134) with a complex
        // TransformGroup value lands a live engine Style: the property
        // resolves to the engine DP and the four children parse through the
        // per-file owner+name rows with the DoubleConverter.
        var previous = Application.Current;
        Application.Current = null;
        try
        {
            var dictionary = BamlReader.LoadDictionary(
                StyleDictionary([("S", RenderTransformBody(
                    ScaleChild("2", "3"),
                    SkewChild("10", "20"),
                    RotateChild("30"),
                    TranslateChild("4", "5")))]));
            var style = Assert.IsType<Style>(dictionary["S"]);
            Assert.Same(typeof(Path), style.TargetType);
            var setter = Assert.IsType<Setter>(Assert.Single(style.Setters));
            Assert.Same(UIElement.RenderTransformProperty, setter.Property);
            var group = Assert.IsType<TransformGroup>(setter.Value);
            Assert.Equal(4, group.Children.Count);
            Assert.Equal(2, Assert.IsType<ScaleTransform>(group.Children[0]).ScaleX);
            Assert.Equal(3, Assert.IsType<ScaleTransform>(group.Children[0]).ScaleY);
            Assert.Equal(10, Assert.IsType<SkewTransform>(group.Children[1]).AngleX);
            Assert.Equal(20, Assert.IsType<SkewTransform>(group.Children[1]).AngleY);
            Assert.Equal(30, Assert.IsType<RotateTransform>(group.Children[2]).Angle);
            Assert.Equal(4, Assert.IsType<TranslateTransform>(group.Children[3]).X);
            Assert.Equal(5, Assert.IsType<TranslateTransform>(group.Children[3]).Y);
        }
        finally
        {
            Application.Current = previous;
        }
    }

    [Fact]
    public void Synthetic_render_transform_applies_mirror_to_path()
    {
        // The routed setter is live, not retained bytes: applying lands the
        // parsed group on the CLR slot through the Setter CLR-first path (the
        // DP store keeps its default; readers bind the CLR property like the
        // XAML loader path).
        var previous = Application.Current;
        Application.Current = null;
        try
        {
            var dictionary = BamlReader.LoadDictionary(
                StyleDictionary([("S", RenderTransformBody(
                    ScaleChild("-1", "1"),
                    SkewChild("0", "0"),
                    RotateChild("0"),
                    TranslateChild()))]));
            var style = Assert.IsType<Style>(dictionary["S"]);
            var path = new Path();
            Assert.Null(path.RenderTransform);
            style.ApplyTo(path);
            var applied = Assert.IsType<TransformGroup>(path.RenderTransform);
            Assert.Equal(-1, applied.Value.M11);
            Assert.Equal(1, applied.Value.M22);
        }
        finally
        {
            Application.Current = previous;
        }
    }

    [Fact]
    public void Synthetic_group_on_template_stays_precise()
    {
        // A TransformGroup complex on a non-RenderTransform property (here the
        // qualified Shape.Stretch setter) fails naming the element instead of
        // landing a half-built setter.
        var previous = Application.Current;
        Application.Current = null;
        try
        {
            var body = new B().Raw(0x03).I16(-620).Raw(0x00)
                .Raw(0x22).I16(0).I16(-442)
                .Raw(0x2E).I16(-238)
                .Raw(0x03).I16(-556).Raw(0x00)
                .Sized(0x06, p => p.I16(1).U16(16521).I16(-558).Str("Stretch"))
                .Raw(0x07).I16(2)
                .Raw(0x03).I16(-682).Raw(0x00)
                .Raw(0x2E).I16(-130)
                .Raw(TranslateChild())
                .Raw(0x04).Raw(0x08).Raw(0x04).Raw(0x04).ToArray();
            var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(
                StyleDictionary([("S", body)])));
            Assert.Contains("holds <TransformGroup> in a complex value", error.Message);
            Assert.Contains("a ControlTemplate, a Binding, an ItemsPanelTemplate, a NullExtension or a TransformGroup", error.Message);
        }
        finally
        {
            Application.Current = previous;
        }
    }

    [Fact]
    public void Synthetic_unknown_group_child_stays_precise()
    {
        // A TransformGroup child that is not a transform (here an empty
        // SolidColorBrush element) fails naming the element instead of landing
        // a half-built group.
        var previous = Application.Current;
        Application.Current = null;
        try
        {
            var brush = new B().Raw(0x03).I16(-578).Raw(0x00).Raw(0x04).ToArray();
            var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(
                StyleDictionary([("S", RenderTransformBody(brush))])));
            Assert.Contains("element SolidColorBrush in the setter TransformGroup", error.Message);
            Assert.Contains("ScaleTransform/SkewTransform/RotateTransform/TranslateTransform", error.Message);
        }
        finally
        {
            Application.Current = previous;
        }
    }

    [Fact]
    public void Synthetic_missing_scale_y_stays_precise()
    {
        // A ScaleTransform without ScaleY fails naming the missing property
        // instead of landing a half-built scale.
        var previous = Application.Current;
        Application.Current = null;
        try
        {
            var partial = new B().Raw(0x03).I16(-546).Raw(0x00)
                .Sized(0x24, p => p.I16(3).Str("-1").I16(-175))
                .Raw(0x04).ToArray();
            var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(
                StyleDictionary([("S", RenderTransformBody(partial))])));
            Assert.Contains("has no ScaleY", error.Message);
            Assert.Contains("scales take ScaleX plus ScaleY", error.Message);
        }
        finally
        {
            Application.Current = previous;
        }
    }

    [Fact]
    public void Synthetic_garbage_scale_text_stays_precise()
    {
        // A ScaleX value that is not a double fails naming the text instead of
        // landing a default scale.
        var previous = Application.Current;
        Application.Current = null;
        try
        {
            var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(
                StyleDictionary([("S", RenderTransformBody(ScaleChild("wide", "1")))])));
            Assert.Contains("'wide' is not a Double for ScaleX on setter 'RenderTransform' in style entry 'S'", error.Message);
        }
        finally
        {
            Application.Current = previous;
        }
    }

    [Fact]
    public void Synthetic_wrong_converter_stays_precise()
    {
        // An Angle value through the EnumConverter fails naming the converter
        // instead of landing a default rotation.
        var previous = Application.Current;
        Application.Current = null;
        try
        {
            var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(
                StyleDictionary([("S", RenderTransformBody(RotateChild("0", -195)))])));
            Assert.Contains("uses converter -195", error.Message);
            Assert.Contains("transform doubles take the DoubleConverter", error.Message);
        }
        finally
        {
            Application.Current = previous;
        }
    }

    [Fact]
    public void Synthetic_wrong_owner_stays_precise()
    {
        // A ScaleX value over a Skew-owned row fails naming the attribute id
        // instead of landing a mis-owned scale.
        var previous = Application.Current;
        Application.Current = null;
        try
        {
            var misowned = new B().Raw(0x03).I16(-546).Raw(0x00)
                .Sized(0x24, p => p.I16(10).Str("-1").I16(-175))
                .Raw(0x04).ToArray();
            var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(
                StyleDictionary([("S", RenderTransformBody(misowned))])));
            Assert.Contains("setter ScaleTransform property id 10", error.Message);
            Assert.Contains("scales take ScaleX/ScaleY", error.Message);
        }
        finally
        {
            Application.Current = previous;
        }
    }

    [Fact]
    public void Synthetic_duplicate_child_stays_precise()
    {
        // A second ScaleTransform in one group fails naming the duplicate
        // instead of landing a doubled scale.
        var previous = Application.Current;
        Application.Current = null;
        try
        {
            var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(
                StyleDictionary([("S", RenderTransformBody(ScaleChild("2", "3"), ScaleChild("2", "3")))])));
            Assert.Contains("duplicate ScaleTransform on the setter TransformGroup", error.Message);
        }
        finally
        {
            Application.Current = previous;
        }
    }

    [Fact]
    public void Synthetic_missing_children_stays_precise()
    {
        // A TransformGroup without the Children content member fails naming
        // the missing content instead of landing an empty group.
        var previous = Application.Current;
        Application.Current = null;
        try
        {
            var body = new B().Raw(0x03).I16(-620).Raw(0x00)
                .Raw(0x22).I16(0).I16(-442)
                .Raw(0x2E).I16(-238)
                .Raw(0x03).I16(-556).Raw(0x00)
                .Sized(0x06, p => p.I16(1).U16(137).I16(-134))
                .Raw(0x07).I16(2)
                .Raw(0x03).I16(-682).Raw(0x00)
                .Raw(ScaleChild("2", "3"))
                .Raw(0x04).Raw(0x08).Raw(0x04).Raw(0x04).ToArray();
            var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(
                StyleDictionary([("S", body)])));
            Assert.Contains("transform child precedes the Children content", error.Message);
        }
        finally
        {
            Application.Current = previous;
        }
    }

    [Fact]
    public void Style_engine_shape_matches_wpf_render_transform()
    {
        // The routed values match the genuine net10.0-windows shape: the
        // RenderTransformProperty DP is owned by UIElement and typed Transform
        // with the Identity default, the CLR property stays a writable
        // Transform, ScaleX/ScaleY default 1, AngleX/AngleY/Angle/X/Y default 0
        // and group children ride a TransformCollection.
        var transformProperty = Assert.IsType<DependencyProperty>(typeof(UIElement).GetField(
            "RenderTransformProperty",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.FlattenHierarchy)!.GetValue(null));
        Assert.Same(typeof(UIElement), transformProperty.OwnerType);
        Assert.Same(typeof(Transform), transformProperty.PropertyType);
        Assert.Same(Transform.Identity, transformProperty.DefaultMetadata.DefaultValue);
        Assert.Same(typeof(Transform), typeof(UIElement).GetProperty("RenderTransform")!.PropertyType);
        Assert.True(typeof(UIElement).GetProperty("RenderTransform")!.CanWrite);
        Assert.Equal(1, new ScaleTransform().ScaleX);
        Assert.Equal(1, new ScaleTransform().ScaleY);
        Assert.Equal(0, new SkewTransform().AngleX);
        Assert.Equal(0, new SkewTransform().AngleY);
        Assert.Equal(0, new RotateTransform().Angle);
        Assert.Equal(0, new TranslateTransform().X);
        Assert.Equal(0, new TranslateTransform().Y);
        Assert.Same(typeof(TransformCollection), typeof(TransformGroup).GetProperty("Children")!.PropertyType);
    }

    /// <summary>Builds a ScaleTransform child with ScaleY/ScaleX simples (the
    /// staged order) through the DoubleConverter.</summary>
    private static byte[] ScaleChild(string scaleX, string scaleY, short conv = -175) => new B()
        .Raw(0x03).I16(-546).Raw(0x00)
        .Sized(0x24, p => p.I16(4).Str(scaleY).I16(conv))
        .Sized(0x24, p => p.I16(3).Str(scaleX).I16(conv))
        .Raw(0x04).ToArray();

    /// <summary>Builds a SkewTransform child with AngleY/AngleX simples (the
    /// staged order) through the DoubleConverter.</summary>
    private static byte[] SkewChild(string angleX, string angleY) => new B()
        .Raw(0x03).I16(-575).Raw(0x00)
        .Sized(0x24, p => p.I16(6).Str(angleY).I16(-175))
        .Sized(0x24, p => p.I16(5).Str(angleX).I16(-175))
        .Raw(0x04).ToArray();

    /// <summary>Builds a RotateTransform child with the Angle simple through
    /// the DoubleConverter.</summary>
    private static byte[] RotateChild(string angle, short conv = -175) => new B()
        .Raw(0x03).I16(-528).Raw(0x00)
        .Sized(0x24, p => p.I16(7).Str(angle).I16(conv))
        .Raw(0x04).ToArray();

    /// <summary>Builds a TranslateTransform child with the optional X/Y
    /// simples (the staged child is empty: X/Y default 0).</summary>
    private static byte[] TranslateChild(string? x = null, string? y = null)
    {
        var child = new B().Raw(0x03).I16(-684).Raw(0x00);
        if (x is not null) child.Sized(0x24, p => p.I16(8).Str(x).I16(-175));
        if (y is not null) child.Sized(0x24, p => p.I16(9).Str(y).I16(-175));
        return child.Raw(0x04).ToArray();
    }

    /// <summary>Builds a Path-targeted style body with a single RenderTransform
    /// short-form setter (member -134) holding a TransformGroup complex.</summary>
    private static byte[] RenderTransformBody(params byte[][] children)
    {
        var body = new B().Raw(0x03).I16(-620).Raw(0x00)
            .Raw(0x22).I16(0).I16(-442)
            .Raw(0x2E).I16(-238)
            .Raw(0x03).I16(-556).Raw(0x00)
            .Sized(0x06, p => p.I16(1).U16(137).I16(-134))
            .Raw(0x07).I16(2)
            .Raw(0x03).I16(-682).Raw(0x00)
            .Raw(0x2E).I16(-130);
        foreach (var child in children) body.Raw(child);
        return body.Raw(0x04).Raw(0x08).Raw(0x04).Raw(0x04).ToArray();
    }

    /// <summary>Builds a single-deferable dictionary with the Style
    /// AttributeInfo rows (TargetType 0, Property 1, Value 2) plus the
    /// transform-child rows (ScaleX 3, ScaleY 4, AngleX 5, AngleY 6, Angle 7,
    /// X 8, Y 9, plus a Skew-owned ScaleX decoy row 10 for the wrong-owner
    /// pin). Each entry carries its own key-block statics (none here).</summary>
    private static byte[] StyleDictionary((string Key, byte[] Entry)[] entries)
    {
        var strings = entries.Select(e => e.Key).Distinct().ToArray();
        var head = TransformTables(strings);
        var positions = new List<int>();
        var content = new B();
        foreach (var (_, body) in entries)
        {
            positions.Add(content.Position);
            content.Raw(body);
        }
        head.Deferable();
        for (var i = 0; i < entries.Length; i++)
        {
            var position = positions[i];
            head.Sized(0x26, p => p.U16((ushort)Array.IndexOf(strings, entries[i].Key)).U32((uint)position).U16(0));
        }
        return head.Raw(content.ToArray()).FinishDictionary().ToArray();
    }

    private static B TransformTables(string[] names)
    {
        // Per-file ids stand in for the staged rows (AttributeInfo 0/1/2 are
        // the Style/Setter TargetType/Property/Value rows; 3-9 are the
        // transform-child rows; 10 is the Skew-owned ScaleX decoy).
        var head = new B().Raw(Header()).Raw(0x01)
            .Raw(0x03).I16(-524).Raw(0x00)
            .Sized(0x1F, p => p.I16(0).I16(-620).Raw(0x00).Str("TargetType"))
            .Sized(0x1F, p => p.I16(1).I16(-556).Raw(0x00).Str("Property"))
            .Sized(0x1F, p => p.I16(2).I16(-556).Raw(0x00).Str("Value"))
            .Sized(0x1F, p => p.I16(3).I16(-546).Raw(0x00).Str("ScaleX"))
            .Sized(0x1F, p => p.I16(4).I16(-546).Raw(0x00).Str("ScaleY"))
            .Sized(0x1F, p => p.I16(5).I16(-575).Raw(0x00).Str("AngleX"))
            .Sized(0x1F, p => p.I16(6).I16(-575).Raw(0x00).Str("AngleY"))
            .Sized(0x1F, p => p.I16(7).I16(-528).Raw(0x00).Str("Angle"))
            .Sized(0x1F, p => p.I16(8).I16(-684).Raw(0x00).Str("X"))
            .Sized(0x1F, p => p.I16(9).I16(-684).Raw(0x00).Str("Y"))
            .Sized(0x1F, p => p.I16(10).I16(-575).Raw(0x00).Str("ScaleX"));
        foreach (var name in names)
            head.Sized(0x20, p => p.U16((ushort)Array.IndexOf(names, name)).Str(name));
        return head;
    }

    /// <summary>Builds the staged-numbering dictionary for the full
    /// RightPathStyle entry (AttributeInfo 0/1/2/92 plus the transform-child
    /// rows 93 ScaleY/94 ScaleX/95 AngleY/96 AngleX/36 Angle plus StringInfo
    /// 0/1/2 for the entry key, its BasedOn static name and its Data static
    /// name, with the two 0x37 declarations riding the RightPathStyle key like
    /// the staged file), plus optional extra shared entries.</summary>
    private static byte[] StagedRightPathFullDictionary(byte[] fullEntry, params (string Key, byte[] Entry)[] extras)
    {
        var head = new B().Raw(Header()).Raw(0x01)
            .Raw(0x03).I16(-524).Raw(0x00)
            .Sized(0x1F, p => p.I16(0).I16(-620).Raw(0x00).Str("TargetType"))
            .Sized(0x1F, p => p.I16(1).I16(-556).Raw(0x00).Str("Property"))
            .Sized(0x1F, p => p.I16(2).I16(-556).Raw(0x00).Str("Value"))
            .Sized(0x1F, p => p.I16(92).I16(-620).Raw(0x00).Str("BasedOn"))
            .Sized(0x1F, p => p.I16(93).I16(-546).Raw(0x00).Str("ScaleY"))
            .Sized(0x1F, p => p.I16(94).I16(-546).Raw(0x00).Str("ScaleX"))
            .Sized(0x1F, p => p.I16(95).I16(-575).Raw(0x00).Str("AngleY"))
            .Sized(0x1F, p => p.I16(96).I16(-575).Raw(0x00).Str("AngleX"))
            .Sized(0x1F, p => p.I16(36).I16(-528).Raw(0x00).Str("Angle"))
            .Sized(0x20, p => p.U16(0).Str("RightPathStyle"))
            .Sized(0x20, p => p.U16(1).Str("PathBaseStyle"))
            .Sized(0x20, p => p.U16(2).Str("LeftGeometry"));
        for (var i = 0; i < extras.Length; i++)
            head.Sized(0x20, p => p.U16((ushort)(3 + i)).Str(extras[i].Key));
        var content = new B();
        var positions = new List<int> { 0 };
        content.Raw(fullEntry);
        foreach (var (_, body) in extras)
        {
            positions.Add(content.Position);
            content.Raw(body);
        }
        head.Deferable();
        head.Sized(0x26, p => p.U16(0).U32((uint)positions[0]).U16(0));
        head.Raw(0x37, 0x00).U16(1);
        head.Raw(0x37, 0x00).U16(2);
        for (var i = 0; i < extras.Length; i++)
            head.Sized(0x26, p => p.U16((ushort)(3 + i)).U32((uint)positions[1 + i]).U16(0));
        return head.Raw(content.ToArray()).FinishDictionary().ToArray();
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
