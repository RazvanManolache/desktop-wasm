// Copyright (c) 2026 The DesktopWasm Authors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Reflection;
using System.Resources;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DesktopWasm.Wpf.Tests;

/// <summary>Slice-123 HandyControl theme.baml Notification + SnapRect4ScreenshotStyle run.
///
/// Scope decision (see the slice entry in docs/preview-upgrade-tasks.md):
/// entry #107 (implicit Notification style at file 0x16C37: WindowAttach
/// attached setters plus Window-qualified SizeToContent/ResizeMode/
/// ShowActivated/ShowInTaskbar/Topmost setters plus short-form Background
/// (-24) with a packed-ARGB brush value plus a Template setter whose
/// ControlTemplate holds a ContentPresenter with a Margin TemplateBinding
/// over Padding plus Center alignment PWCs) and the flat run of entry #108
/// (keyed 'SnapRect4ScreenshotStyle' style over well-known Rectangle -514:
/// RectangleAttach.Circular plus short-form Height/Width/Fill
/// plus HorizontalAlignment/VerticalAlignment plus Visibility) land as live engine styles,
/// and the run stops precise at the #108 Trigger list (0x0B).
///
/// Byte-exact decode against the staged 466350-byte theme.baml,
/// triangulated three ways (the staged bytes, the staged converted owner
/// shapes via Mono.Cecil, and a real net10 BAML fixture compiling the same
/// Window/Rectangle/StaticResource/TemplateBinding/trigger shapes:
/// qualified owners travel -739 for Window with WidthAndHeight/NoResize/
/// False/True EnumConverter/BooleanConverter values; Background travels
/// short-form -24 with a 0x06/744 brush value 01+ARGB (Transparent);
/// Fill travels short-form -100 with DodgerBlue; Height/Width travel -47/
/// -57 with "5"/LengthConverter; Visibility travels -135 with
/// "Collapsed"/EnumConverter; the template Margin TemplateBinding is 0x23
/// target -49 extension 634 source -35; the Center alignments are
/// template-form 0x24 member/text/converter records; the Trigger list is
/// record 0x0B).
///
/// Live counts verified first: handycontrol conversion-failed, 6 staged,
/// 2 gaps (both declared absent binaries System.Security.Permissions +
/// System.Windows.Extensions with no browser path; Convert map untouched,
/// all 6 staged images sha256-verified, manifest hand-untouched).
/// </summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class Slice123BamlNotificationTests : IDisposable
{
    private readonly Application? _previous = Application.Current;

    public Slice123BamlNotificationTests()
    {
        WpfAnimationClock.ResetForTests();
    }

    public void Dispose()
    {
        XamlLoader.CustomElementSource = null;
        Application.Current = _previous;
        WpfAnimationClock.ResetForTests();
    }

    [Fact]
    public void Staged_manifest_keeps_six_staged_two_absent()
    {
        var entry = LoadStagedHandyControlEntry();
        var apps = StagedHandyAppsDirectory();
        using var document = JsonDocument.Parse(
            File.ReadAllText(System.IO.Path.Combine(apps, "app-manifest.json")));
        var root = document.RootElement;
        Assert.Equal("conversion-failed", root.GetProperty("status").GetString());
        var assemblies = root.GetProperty("assemblies").EnumerateArray().ToList();
        Assert.Equal(6, assemblies.Count);
        foreach (var assembly in assemblies)
        {
            var file = assembly.GetProperty("file").GetString()!;
            var expected = assembly.GetProperty("sha256").GetString()!;
            var actual = Convert.ToHexString(SHA256.HashData(
                File.ReadAllBytes(System.IO.Path.Combine(apps, file))));
            Assert.True(string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase),
                $"hash mismatch in staged '{file}': {actual} != {expected}");
        }
        var gaps = root.GetProperty("gaps").EnumerateArray().ToList();
        Assert.Equal(2, gaps.Count);
        var details = gaps.Select(g => g.GetProperty("detail").GetString()!).ToList();
        Assert.Contains(details, d => d.Contains("System.Security.Permissions"));
        Assert.Contains(details, d => d.Contains("System.Windows.Extensions"));
        _ = entry;
    }

    [Fact]
    public void Window_chrome_dps_carry_live_wpf_shapes()
    {
        // The qualified Window owners need DependencyProperty identities:
        // live-verified against the net10 WindowsDesktop runtime (owner
        // System.Windows.Window for all five; SizeToContent/Manual,
        // ResizeMode/CanResize, ShowActivated/True, ShowInTaskbar/True,
        // Topmost/False defaults).
        Assert.Same(typeof(SizeToContent), Window.SizeToContentProperty.PropertyType);
        Assert.Same(typeof(Window), Window.SizeToContentProperty.OwnerType);
        Assert.Equal(SizeToContent.Manual, Window.SizeToContentProperty.DefaultMetadata?.DefaultValue);
        Assert.Same(typeof(ResizeMode), Window.ResizeModeProperty.PropertyType);
        Assert.Same(typeof(Window), Window.ResizeModeProperty.OwnerType);
        Assert.Equal(ResizeMode.CanResize, Window.ResizeModeProperty.DefaultMetadata?.DefaultValue);
        Assert.Same(typeof(bool), Window.ShowActivatedProperty.PropertyType);
        Assert.Equal(true, Window.ShowActivatedProperty.DefaultMetadata?.DefaultValue);
        Assert.Same(typeof(bool), Window.ShowInTaskbarProperty.PropertyType);
        Assert.Equal(true, Window.ShowInTaskbarProperty.DefaultMetadata?.DefaultValue);
        Assert.Same(typeof(bool), Window.TopmostProperty.PropertyType);
        Assert.Same(typeof(Window), Window.TopmostProperty.OwnerType);
        Assert.Equal(false, Window.TopmostProperty.DefaultMetadata?.DefaultValue);
        var window = new Window();
        Assert.Equal(SizeToContent.Manual, window.SizeToContent);
        Assert.Equal(ResizeMode.CanResize, window.ResizeMode);
        Assert.True(window.ShowActivated);
        Assert.True(window.ShowInTaskbar);
        Assert.False(window.Topmost);
    }

    [Fact]
    public void Qualified_window_owner_setters_land_and_apply()
    {
        // The staged #107 Window-qualified shape over an engine target:
        // SizeToContent/ResizeMode/ShowActivated/ShowInTaskbar/Topmost with
        // owner -739 resolve the engine Window DPs and apply through the
        // CLR like the XAML loader path.
        XamlLoader.CustomElementSource = null;
        var bytes = QualifiedWindowDictionary(
            ("SizeToContent", "WidthAndHeight", -195),
            ("ResizeMode", "NoResize", -195),
            ("ShowActivated", "False", -46),
            ("ShowInTaskbar", "False", -46),
            ("Topmost", "True", -46));
        var dictionary = BamlReader.LoadDictionary(bytes);
        var style = Assert.IsType<Style>(dictionary[typeof(Window)]);
        Assert.Same(typeof(Window), style.TargetType);
        Assert.Equal(5, style.Setters.Count);
        var sizeToContent = Assert.IsType<Setter>(style.Setters[0]);
        Assert.Same(Window.SizeToContentProperty, sizeToContent.Property);
        Assert.Equal(SizeToContent.WidthAndHeight, sizeToContent.Value);
        var resizeMode = Assert.IsType<Setter>(style.Setters[1]);
        Assert.Same(Window.ResizeModeProperty, resizeMode.Property);
        Assert.Equal(ResizeMode.NoResize, resizeMode.Value);
        var showActivated = Assert.IsType<Setter>(style.Setters[2]);
        Assert.Same(Window.ShowActivatedProperty, showActivated.Property);
        Assert.Equal(false, showActivated.Value);
        var showInTaskbar = Assert.IsType<Setter>(style.Setters[3]);
        Assert.Same(Window.ShowInTaskbarProperty, showInTaskbar.Property);
        Assert.Equal(false, showInTaskbar.Value);
        var topmost = Assert.IsType<Setter>(style.Setters[4]);
        Assert.Same(Window.TopmostProperty, topmost.Property);
        Assert.Equal(true, topmost.Value);
        var window = new Window();
        style.ApplyTo(window);
        Assert.Equal(SizeToContent.WidthAndHeight, window.SizeToContent);
        Assert.Equal(ResizeMode.NoResize, window.ResizeMode);
        Assert.False(window.ShowActivated);
        Assert.False(window.ShowInTaskbar);
        Assert.True(window.Topmost);
    }

    [Fact]
    public void Converted_attached_pair_lands_through_staged_owner_shape()
    {
        // The staged #107 WindowAttach shape (two qualified setters over one
        // per-file TypeInfo owner) over a test-assembly owner: both land as
        // live attached setters with converted bools.
        XamlLoader.CustomElementSource = null;
        var content = new B()
            .Raw(0x03).I16(-620).Raw(0x00)
            .Raw(0x22).I16(0).I16(0)
            .Raw(0x2E).I16(-238)
            .Raw(0x03).I16(-556).Raw(0x00)
            .Sized(0x06, q => q.I16(1).U16(16521).I16(1).Str("Alpha"))
            .Sized(0x24, q => q.I16(2).Str("True").I16(-46))
            .Raw(0x04)
            .Raw(0x03).I16(-556).Raw(0x00)
            .Sized(0x06, q => q.I16(1).U16(16521).I16(1).Str("Beta"))
            .Sized(0x24, q => q.I16(2).Str("False").I16(-46))
            .Raw(0x04)
            .Raw(0x04);
        var bytes = ImplicitDictionary(content.ToArray(), 0,
            [("DesktopWasm.Wpf.Window", (ushort)0), (typeof(Slice123OwnerStub).FullName!, (ushort)1)],
            [(0, "TargetType", -620), (1, "Property", -556), (2, "Value", -556)]);
        var dictionary = BamlReader.LoadDictionary(bytes);
        var style = Assert.IsType<Style>(dictionary[typeof(Window)]);
        Assert.Equal(2, style.Setters.Count);
        var alpha = Assert.IsType<Setter>(style.Setters[0]);
        Assert.Same(Slice123OwnerStub.AlphaProperty, alpha.Property);
        Assert.True(alpha.Property.IsAttached);
        Assert.Equal(true, alpha.Value);
        var beta = Assert.IsType<Setter>(style.Setters[1]);
        Assert.Same(Slice123OwnerStub.BetaProperty, beta.Property);
        Assert.Equal(false, beta.Value);
    }

    [Fact]
    public void Background_brush_value_lands_transparent()
    {
        // The staged #107 Background shape (short-form -24 plus a 0x06/744
        // packed-ARGB value 01 FFFFFF00) lands Control.BackgroundProperty
        // with a Transparent brush; applying it to a real Window paints the
        // CLR mirror like the XAML loader path.
        XamlLoader.CustomElementSource = null;
        var bytes = BrushDictionary("DesktopWasm.Wpf.Window", -24,
            0x01, 0xFF, 0xFF, 0xFF, 0x00);
        var dictionary = BamlReader.LoadDictionary(bytes);
        var style = Assert.IsType<Style>(dictionary[typeof(Window)]);
        var setter = Assert.IsType<Setter>(Assert.Single(style.Setters));
        Assert.Same(Control.BackgroundProperty, setter.Property);
        var brush = Assert.IsType<SolidColorBrush>(setter.Value);
        Assert.Equal(Color.FromArgb(0, 255, 255, 255), brush.Color);
        var window = new Window();
        Assert.Null(window.Background);
        style.ApplyTo(window);
        Assert.Equal(Color.FromArgb(0, 255, 255, 255), Assert.IsType<SolidColorBrush>(window.Background).Color);
    }

    [Fact]
    public void Rectangle_flat_run_lands_and_applies()
    {
        // The staged #108 flat run over the well-known Rectangle target:
        // short-form Height/Width ("5"/LengthConverter), Fill (744
        // DodgerBlue), Visibility ("Collapsed"/EnumConverter) plus
        // HorizontalAlignment/VerticalAlignment land and apply.
        XamlLoader.CustomElementSource = null;
        var bytes = RectangleFlatDictionary();
        var dictionary = BamlReader.LoadDictionary(bytes);
        var style = Assert.IsType<Style>(dictionary[typeof(Rectangle)]);
        Assert.Same(typeof(Rectangle), style.TargetType);
        Assert.Equal(6, style.Setters.Count);
        var height = Assert.IsType<Setter>(style.Setters[0]);
        Assert.Same(FrameworkElement.HeightProperty, height.Property);
        Assert.Equal(5.0, height.Value);
        var width = Assert.IsType<Setter>(style.Setters[1]);
        Assert.Same(FrameworkElement.WidthProperty, width.Property);
        Assert.Equal(5.0, width.Value);
        var fill = Assert.IsType<Setter>(style.Setters[2]);
        Assert.Same(Shape.FillProperty, fill.Property);
        Assert.Equal(Color.FromArgb(255, 30, 144, 255), Assert.IsType<SolidColorBrush>(fill.Value).Color);
        var halign = Assert.IsType<Setter>(style.Setters[3]);
        Assert.Same(FrameworkElement.HorizontalAlignmentProperty, halign.Property);
        Assert.Equal(HorizontalAlignment.Left, halign.Value);
        var valign = Assert.IsType<Setter>(style.Setters[4]);
        Assert.Same(FrameworkElement.VerticalAlignmentProperty, valign.Property);
        Assert.Equal(VerticalAlignment.Top, valign.Value);
        var visibility = Assert.IsType<Setter>(style.Setters[5]);
        Assert.Same(UIElement.VisibilityProperty, visibility.Property);
        Assert.Equal(Visibility.Collapsed, visibility.Value);
        var rectangle = new Rectangle();
        style.ApplyTo(rectangle);
        Assert.Equal(5.0, rectangle.Height);
        Assert.Equal(5.0, rectangle.Width);
        Assert.Equal(Color.FromArgb(255, 30, 144, 255), Assert.IsType<SolidColorBrush>(rectangle.Fill).Color);
        Assert.Equal(HorizontalAlignment.Left, rectangle.HorizontalAlignment);
        Assert.Equal(VerticalAlignment.Top, rectangle.VerticalAlignment);
        Assert.Equal(Visibility.Collapsed, rectangle.Visibility);
    }

    [Fact]
    public void Template_center_alignments_land_on_presenter()
    {
        // The staged #107 template shape (ContentPresenter with a Margin
        // TemplateBinding over Padding plus Center HorizontalAlignment/
        // VerticalAlignment PWCs) lands with the alignments on the presenter
        // and the TemplateBinding tuple recorded like the XAML loader path.
        XamlLoader.CustomElementSource = null;
        var bytes = PresenterCenterDictionary("DesktopWasm.Wpf.Button");
        var dictionary = BamlReader.LoadDictionary(bytes);
        var style = Assert.IsType<Style>(dictionary[typeof(Button)]);
        var template = Assert.IsType<ControlTemplate>(Assert.IsType<Setter>(Assert.Single(style.Setters)).Value);
        var presenter = Assert.IsType<ContentPresenter>(template.VisualTree);
        Assert.Equal(HorizontalAlignment.Center, presenter.HorizontalAlignment);
        Assert.Equal(VerticalAlignment.Center, presenter.VerticalAlignment);
        var binding = Assert.Single(template.TemplateBindings);
        Assert.Same(presenter, binding.Element);
        Assert.Equal("Padding", binding.Property);
    }

    [Fact]
    public void Template_bad_alignment_converter_fails_precise()
    {
        // Template ContentPresenter alignment PWCs take the EnumConverter
        // like the staged Center shapes; any other converter fails naming
        // both instead of landing a half-built visual.
        XamlLoader.CustomElementSource = null;
        var entry = new B()
            .Raw(0x03).I16(-620).Raw(0x00)
            .Raw(0x22).I16(0).I16(0)
            .Raw(0x2E).I16(-238)
            .Raw(0x03).I16(-556).Raw(0x00)
            .Sized(0x06, q => q.I16(1).U16(137).I16(-37))
            .Raw(0x07).I16(2)
            .Raw(0x03).I16(-108).Raw(0x00)
            .Raw(0x22).I16(3).I16(0)
            .Raw(0x2E).I16(-174)
            .Raw(0x03).I16(-102).Raw(0x00)
            .Sized(0x24, q => q.I16(-48).Str("Center").I16(-615))
            .Raw(0x04)
            .Raw(0x04)
            .Raw(0x08)
            .Raw(0x04)
            .Raw(0x04);
        var bytes = ImplicitDictionary(entry.ToArray(), 0,
            [("DesktopWasm.Wpf.Button", (ushort)0)],
            [(0, "TargetType", -620), (1, "Property", -556), (2, "Value", -556), (3, "TargetType", -108)]);
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(bytes));
        Assert.Contains("template ContentPresenter property id -48 with converter -615", error.Message);
        Assert.Contains("presenters take ContentSource/Name/RecognizesAccessKey strings plus HorizontalAlignment/VerticalAlignment/Visibility enum values", error.Message);
    }

    [Fact]
    public void Trigger_list_needs_elements_precise()
    {
        // Trigger lists land (slice-124); an abrupt list end still stops
        // precise naming the offending record instead of landing a
        // half-built trigger.
        XamlLoader.CustomElementSource = null;
        var content = new B()
            .Raw(0x03).I16(-620).Raw(0x00)
            .Raw(0x22).I16(0).I16(-514)
            .Raw(0x2E).I16(-238)
            .Raw(0x03).I16(-556).Raw(0x00)
            .Sized(0x06, q => q.I16(1).U16(137).I16(-47))
            .Sized(0x24, q => q.I16(2).Str("5").I16(-348))
            .Raw(0x04)
            .Raw(0x0B).U16(3);
        var bytes = ImplicitDictionary(content.ToArray(), -514, null,
            [(0, "TargetType", -620), (1, "Property", -556), (2, "Value", -556), (3, "Triggers", -620)]);
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(bytes));
        Assert.Contains("trigger list in style entry", error.Message);
        Assert.Contains("trigger lists take Trigger/DataTrigger elements", error.Message);
    }

    [Fact]
    public void Unknown_short_member_still_fails_precise()
    {
        // Short-form members beyond the staged run fail naming the member
        // id instead of landing a half-built setter.
        XamlLoader.CustomElementSource = null;
        var bytes = ShortDictionary("DesktopWasm.Wpf.ContentControl", -777, "X", -615);
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(bytes));
        Assert.Contains("well-known member id -777", error.Message);
        Assert.Contains("short-form properties beyond FlowDirection/Template/Focusable/IsEnabled/HorizontalAlignment/VerticalAlignment/HorizontalContentAlignment/VerticalContentAlignment/Background/Height/Width/Fill/Visibility", error.Message);
    }

    [Fact]
    public void Unknown_qualified_owner_still_fails_precise()
    {
        // Qualified owners beyond Shape/UIElement/Window/FrameworkElement/Canvas
        // fail naming the owner instead of landing a half-built setter.
        XamlLoader.CustomElementSource = null;
        var content = new B()
            .Raw(0x03).I16(-620).Raw(0x00)
            .Raw(0x22).I16(0).I16(0)
            .Raw(0x2E).I16(-238)
            .Raw(0x03).I16(-556).Raw(0x00)
            .Sized(0x06, q => q.I16(1).U16(16521).I16(-483).Str("SizeToContent"))
            .Sized(0x24, q => q.I16(2).Str("WidthAndHeight").I16(-195))
            .Raw(0x04)
            .Raw(0x04);
        var bytes = ImplicitDictionary(content.ToArray(), 0,
            [("DesktopWasm.Wpf.Window", (ushort)0)],
            [(0, "TargetType", -620), (1, "Property", -556), (2, "Value", -556)]);
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(bytes));
        Assert.Contains("setter property owner well-known type id -483", error.Message);
        Assert.Contains("owners beyond Shape/UIElement/Window/FrameworkElement/Canvas", error.Message);
    }

    [Fact]
    public void Brush_value_wrong_serializer_fails_precise()
    {
        // A PropertyCustom value with any serializer but the packed-ARGB
        // brush serializer fails naming it instead of landing raw bytes.
        XamlLoader.CustomElementSource = null;
        var content = new B()
            .Raw(0x03).I16(-620).Raw(0x00)
            .Raw(0x22).I16(0).I16(0)
            .Raw(0x2E).I16(-238)
            .Raw(0x03).I16(-556).Raw(0x00)
            .Sized(0x06, q => q.I16(1).U16(137).I16(-24))
            .Sized(0x06, q => q.I16(2).U16(137).Raw(0x01, 0x00, 0x00, 0xFF, 0xFF))
            .Raw(0x04)
            .Raw(0x04);
        var bytes = ImplicitDictionary(content.ToArray(), 0,
            [("DesktopWasm.Wpf.Window", (ushort)0)],
            [(0, "TargetType", -620), (1, "Property", -556), (2, "Value", -556)]);
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(bytes));
        Assert.Contains("holds a PropertyCustom value with serializer 137", error.Message);
        Assert.Contains("setter values take converted text, packed-ARGB brushes or templates", error.Message);
    }

    [Fact]
    public void Brush_value_bad_mark_fails_precise()
    {
        // Packed-ARGB brush payloads start with 0x01 in every
        // fixture-verified sample; any other lead byte fails naming it.
        XamlLoader.CustomElementSource = null;
        var bytes = BrushDictionary("DesktopWasm.Wpf.Window", -24,
            0x02, 0xFF, 0xFF, 0xFF, 0x00);
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(bytes));
        Assert.Contains("holds a brush value marked 0x02", error.Message);
        Assert.Contains("packed-ARGB brushes start with 0x01", error.Message);
    }

    [Fact]
    public void Brush_value_on_non_brush_property_fails_precise()
    {
        // A packed-ARGB brush value on a non-Brush property (Height is a
        // double) fails naming the mismatch instead of landing a brush
        // where a double belongs.
        XamlLoader.CustomElementSource = null;
        var bytes = BrushDictionary("DesktopWasm.Wpf.Rectangle", -47,
            0x01, 0xFF, 0xFF, 0xFF, 0x00);
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(bytes));
        Assert.Contains("holds a packed-ARGB brush for Double", error.Message);
        Assert.Contains("brush values take a Brush property", error.Message);
    }

    [Fact]
    public void Theme_baml_lands_notification_and_rectangle_flat_run_then_stops_at_triggers()
    {
        LoadStagedHandyControlEntry();
        var theme = ReadStagedGResource("HandyControl", "themes/theme.baml");
        Assert.Equal(466350, theme.Length);
        // Byte-verbatim #107 framing: the WindowAttach-qualified IgnoreAltF4
        // head (owner TypeInfo 37); the staged SizeToContent head (qualified
        // owner -739 = Window, kept from the previous slice); the Background
        // short-form -24 plus its 744 Transparent value; the Margin
        // TemplateBinding (-49/634/-35); the template/presenter/setter/style
        // close run.
        Assert.Equal(
            new byte[] { 0x06, 0x13, 0x01, 0x00, 0x89, 0x40, 0x25, 0x00, 0x0B },
            theme[0x16C47..0x16C50]);
        Assert.Equal(
            new byte[] { 0x03, 0xD4, 0xFD, 0x00, 0x06, 0x15, 0x01, 0x00, 0x89, 0x40, 0x1D, 0xFD, 0x0D, 0x53, 0x69, 0x7A, 0x65 },
            theme[0x16C92..0x16CA3]);
        Assert.Equal(
            new byte[] { 0x06, 0x07, 0x01, 0x00, 0x89, 0x00, 0xE8, 0xFF, 0x06, 0x0A, 0x02, 0x00, 0xE8, 0x02, 0x01, 0xFF, 0xFF, 0xFF, 0x00 },
            theme[0x16CED..0x16D00]);
        Assert.Equal(
            new byte[] { 0x23, 0xCF, 0xFF, 0x7A, 0x02, 0xDD, 0xFF },
            theme[0x16D8E..0x16D95]);
        Assert.Equal(
            new byte[] { 0x24, 0x0C, 0xD0, 0xFF, 0x06, 0x43, 0x65, 0x6E, 0x74 },
            theme[0x16D95..0x16D9E]);
        Assert.Equal(
            new byte[] { 0x04, 0x04, 0x08, 0x04, 0x04 },
            theme[0x16DAF..0x16DB4]);
        // Byte-verbatim #108 framing: the Style start over well-known
        // Rectangle (-514); the RectangleAttach-qualified Circular head
        // (owner TypeInfo 38); the Fill short-form -100 plus its 744
        // DodgerBlue value; the Visibility setter end plus the Trigger list.
        Assert.Equal(
            new byte[] { 0x03, 0x94, 0xFD, 0x00, 0x22, 0x00, 0x00, 0xFE, 0xFD, 0x2E, 0x12, 0xFF },
            theme[0x16DB4..0x16DC0]);
        Assert.Equal(
            new byte[] { 0x06, 0x10, 0x01, 0x00, 0x89, 0x40, 0x26, 0x00, 0x08, 0x43, 0x69 },
            theme[0x16DC4..0x16DCF]);
        Assert.Equal(
            new byte[] { 0x06, 0x07, 0x01, 0x00, 0x89, 0x00, 0x9C, 0xFF, 0x06, 0x0A, 0x02, 0x00, 0xE8, 0x02, 0x01, 0xFF, 0x90, 0x1E, 0xFF },
            theme[0x16E1C..0x16E2F]);
        Assert.Equal(
            new byte[] { 0x04, 0x0B, 0x0C, 0x00 },
            theme[0x16E7B..0x16E7F]);
        // The #108 key in the StringInfo table (id 102, verified
        // byte-by-byte): the 'RectangleCircular' string beside it (id 101)
        // keys a later entry behind the Trigger-list stop.
        Assert.Equal(
            new byte[] { 0x20, 0x1C, 0x66, 0x00, 0x18, 0x53, 0x6E },
            theme[0x1729..0x1730]);
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(theme));
        // Entries #87-107 materialize in index order (the entry machine
        // consumes keys strictly in order, so any earlier failure would
        // throw its own offset instead).
        Assert.DoesNotContain("BAML+0x166FF", error.Message);
        Assert.DoesNotContain("PathBaseStyle", error.Message);
        Assert.DoesNotContain("BAML+0x167B8", error.Message);
        Assert.DoesNotContain("x:Static", error.Message);
        Assert.DoesNotContain("VerticalScrollBarButtonHeightKey", error.Message);
        Assert.DoesNotContain("BAML+0x167C8", error.Message);
        Assert.DoesNotContain("BehaviorXY200", error.Message);
        Assert.DoesNotContain("FluidMoveBehavior", error.Message);
        Assert.DoesNotContain("BAML+0x168D2", error.Message);
        Assert.DoesNotContain("BlendEffectBox", error.Message);
        Assert.DoesNotContain("BAML+0x16926", error.Message);
        Assert.DoesNotContain("BorderCircular", error.Message);
        Assert.DoesNotContain("converted type", error.Message);
        Assert.DoesNotContain("BAML+0x16954", error.Message);
        Assert.DoesNotContain("HandyControl.Controls.Col", error.Message);
        Assert.DoesNotContain("well-known member id -132", error.Message);
        Assert.DoesNotContain("BAML+0x16990", error.Message);
        Assert.DoesNotContain("BAML+0x169D0", error.Message);
        Assert.DoesNotContain("HandyControl.Controls.FlipClock", error.Message);
        Assert.DoesNotContain("templates take a ContentPresenter visual", error.Message);
        Assert.DoesNotContain("BAML+0x16BBB", error.Message);
        Assert.DoesNotContain("HandyControl.Controls.FloatingBlock", error.Message);
        Assert.DoesNotContain("(implicit style key 'GridSplitter')", error.Message);
        Assert.DoesNotContain("BAML+0x16C96", error.Message);
        Assert.DoesNotContain("setter property owner Window", error.Message);
        Assert.DoesNotContain("well-known member id -24", error.Message);
        Assert.DoesNotContain("BAML+0x16E7C", error.Message);
        Assert.DoesNotContain("RectangleCircular", error.Message);
        Assert.DoesNotContain("SnapRect4ScreenshotStyle", error.Message);
        Assert.DoesNotContain("TargetArea4ScreenshotStyle", error.Message);
        Assert.DoesNotContain("holds a Trigger list", error.Message);
        Assert.DoesNotContain("BAML+0x1760B", error.Message);
        Assert.DoesNotContain("BAML+0x17C06", error.Message);
        Assert.DoesNotContain("Right2LeftTransition", error.Message);
        Assert.DoesNotContain("SliderRepeatButtonHorizontalStyle", error.Message);
        Assert.DoesNotContain("SliderRepeatButtonVerticalStyle", error.Message);
        Assert.DoesNotContain("CompareSliderRepeatButtonTransparent", error.Message);
        Assert.DoesNotContain("ScrollBarBaseRepeatButton", error.Message);
        Assert.DoesNotContain("BAML+0x17F4C", error.Message);
        Assert.DoesNotContain("ToggleBlockBaseStyle", error.Message);
        Assert.DoesNotContain("well-known member id -38", error.Message);
        Assert.DoesNotContain("BAML+0x1835D", error.Message);
        Assert.DoesNotContain("GroupBoxBaseTopTemplate", error.Message);
        Assert.DoesNotContain("BAML+0x1859F", error.Message);
        Assert.DoesNotContain("GroupBoxBaseLeftTemplate", error.Message);
        Assert.DoesNotContain("holds ControlTemplate", error.Message);
        Assert.DoesNotContain("BAML+0x1888C", error.Message);
        Assert.DoesNotContain("GroupBoxTabTopTemplate", error.Message);
        Assert.DoesNotContain("GroupBoxTabLeftTemplate", error.Message);
        Assert.DoesNotContain("template Binding holds record 0x38", error.Message);
        Assert.DoesNotContain("BAML+0x18D3F", error.Message);
        Assert.DoesNotContain("ButtonGroupHorizontalUniformItemsPanelTemplate", error.Message);
        Assert.DoesNotContain("holds well-known type id -330", error.Message);
        Assert.DoesNotContain("BAML+0x18DBD", error.Message);
        Assert.DoesNotContain("ProgressBarIndicatorAnimatedFill", error.Message);
        Assert.DoesNotContain("holds well-known type id -358", error.Message);
        Assert.DoesNotContain("BAML+0x18E5D", error.Message);
        Assert.DoesNotContain("BAML+0x19029", error.Message);
        Assert.DoesNotContain("CircleProgressBarTemplate", error.Message);
        Assert.DoesNotContain("VirtualizingStackHorizontalItemsPanelTemplate", error.Message);
        Assert.DoesNotContain("WrapHorizontalItemsPanelTemplate", error.Message);
        Assert.DoesNotContain("WrapVerticalItemsPanelTemplate", error.Message);
        Assert.DoesNotContain("DockItemsPanelTemplate", error.Message);
        Assert.DoesNotContain("ResizeGripperForeground", error.Message);
        Assert.DoesNotContain("SineEaseOut", error.Message);
        Assert.DoesNotContain("BAML+0x19150", error.Message);
        Assert.DoesNotContain("BadgeDotTemplate", error.Message);
        Assert.DoesNotContain("element ContentPresenter on the template root panel", error.Message);
        Assert.DoesNotContain("root panels take ContentPresenter, Border, converted Shape and TextBlock children", error.Message);
        Assert.DoesNotContain("BAML+0x1927D", error.Message);
        Assert.DoesNotContain("ColorPickerSliderThumbTemplate", error.Message);
        Assert.DoesNotContain("BAML+0x192B9", error.Message);
        Assert.DoesNotContain("ColorPickerRainbowBrush", error.Message);
        Assert.DoesNotContain("has no Offset", error.Message);
        Assert.DoesNotContain("BAML+0x19383", error.Message);
        Assert.DoesNotContain("ColorPickerOpacityBrush", error.Message);
        Assert.DoesNotContain("well-known type id -180", error.Message);
        Assert.DoesNotContain("BAML+0x19497", error.Message);
        Assert.DoesNotContain("CoverFlowBaseStyle", error.Message);
        Assert.DoesNotContain("well-known type id -729", error.Message);
        Assert.DoesNotContain("BAML+0x195FD", error.Message);
        Assert.DoesNotContain("GridViewColumnHeaderGripper", error.Message);
        Assert.DoesNotContain("well-known type id -66", error.Message);
        Assert.DoesNotContain("Canvas.Right", error.Message);
        Assert.DoesNotContain("BAML+0x196C6", error.Message);
        Assert.DoesNotContain("'Padding' is not a dependency property of Border", error.Message);
        Assert.DoesNotContain("SortByCategoryDrawingBrush", error.Message);
        Assert.DoesNotContain("BAML+0x196F6", error.Message);
        Assert.DoesNotContain("SortByNameDrawingBrush", error.Message);
        Assert.DoesNotContain("TabItemMenu", error.Message);
        Assert.DoesNotContain("BAML+0x19D99", error.Message);
        Assert.DoesNotContain("BAML+0x19DCE", error.Message);
        Assert.DoesNotContain("well-known type id -105", error.Message);
        Assert.DoesNotContain("Lang.Close", error.Message);
        Assert.DoesNotContain("throws reading the static property", error.Message);
        Assert.DoesNotContain("XmlnsDefinitionAttribute", error.Message);
        Assert.DoesNotContain("ToolBarTrayBaseStyle", error.Message);
        Assert.DoesNotContain("BAML+0x19E4F", error.Message);
        Assert.DoesNotContain("well-known type id -672", error.Message);
        Assert.DoesNotContain("TreeArrow", error.Message);
        Assert.DoesNotContain("BAML+0x19E7B", error.Message);
        Assert.DoesNotContain("well-known type id -446", error.Message);
        Assert.DoesNotContain("RowHeaderGripperStyle", error.Message);
        Assert.DoesNotContain("BAML+0x1A034", error.Message);
        Assert.DoesNotContain("'Cursor' is not a dependency property of FrameworkElement", error.Message);
        Assert.DoesNotContain("DataGridComboBoxColumnStyle", error.Message);
        Assert.DoesNotContain("DataGridTextColumnStyle", error.Message);        
        Assert.DoesNotContain("CarouselItemBaseStyle", error.Message);
        Assert.DoesNotContain("BAML+0x1A113", error.Message);
        Assert.DoesNotContain("BAML+0x1A186", error.Message);        
        Assert.DoesNotContain("BAML+0x1A21B", error.Message);
        Assert.DoesNotContain("well-known type id -92", error.Message);
        Assert.DoesNotContain("targets TextBlock", error.Message);        
        Assert.DoesNotContain("template property id -21", error.Message);
        Assert.DoesNotContain("GrowlWindow", error.Message);
        Assert.DoesNotContain("BAML+0x1A2CA", error.Message);
        Assert.DoesNotContain("well-known member id -51", error.Message);
        Assert.DoesNotContain("ListBoxItemCustom", error.Message);
        Assert.DoesNotContain("BAML+0x1A368", error.Message);
        Assert.DoesNotContain("well-known type id -374", error.Message);
        Assert.DoesNotContain("BAML+0x1A92D", error.Message);
        Assert.DoesNotContain("well-known member id -81", error.Message);
        Assert.DoesNotContain("RibbonTab", error.Message);
        Assert.DoesNotContain("BAML+0x1A990", error.Message);
        Assert.DoesNotContain("template root panel property 'Visibility' with converter -195", error.Message);
        Assert.DoesNotContain("BAML+0x1A9DD", error.Message);
        Assert.DoesNotContain("element well-known type id -550 on the template root panel", error.Message);
        Assert.DoesNotContain("BAML+0x1AE91", error.Message);
        Assert.DoesNotContain("'EffectShadow1'", error.Message);
        Assert.DoesNotContain("record 0x34 (converted-element values", error.Message);
        Assert.DoesNotContain("BAML+0x1B001", error.Message);
        Assert.DoesNotContain("BAML+0x1B22E", error.Message);
        Assert.DoesNotContain("'SearchPathStyle'", error.Message);
        Assert.DoesNotContain("'RightPathStyle'", error.Message);
        Assert.DoesNotContain("record 0x38 in a Style", error.Message);
        Assert.DoesNotContain("'RenderTransformOrigin' is not a dependency property of UIElement", error.Message);
        Assert.DoesNotContain("BAML+0x1B260", error.Message);
        Assert.DoesNotContain("well-known member id -134", error.Message);
        Assert.Contains("'BorderRegion'", error.Message);
        Assert.Contains("BAML+0x1B2F9", error.Message);
        Assert.Contains("setter property owner Border in style entry 'BorderRegion'", error.Message);
        Assert.DoesNotContain("BAML+0x1AA35", error.Message);
        Assert.DoesNotContain("'PrimaryBrush'", error.Message);
        Assert.DoesNotContain("record 0x34 in a LinearGradientBrush", error.Message);
        Assert.DoesNotContain("BAML+0x1AA1C", error.Message);
        Assert.DoesNotContain("LightPrimaryBrush", error.Message);
        Assert.DoesNotContain("well-known type id -578", error.Message);
        Assert.DoesNotContain("on setter 'Template'", error.Message);
        Assert.DoesNotContain("well-known type id -658", error.Message);
        Assert.DoesNotContain("HandyControl.Controls.SimplePanel", error.Message);
        Assert.DoesNotContain("VirtualizingStackPanel", error.Message);
        Assert.DoesNotContain("templates take a ContentPresenter", error.Message);
        Assert.DoesNotContain("items panels take a UniformGrid/StackPanel/VirtualizingStackPanel/WrapPanel/DockPanel visual", error.Message);

        Assert.DoesNotContain("well-known type id -608", error.Message);
        Assert.DoesNotContain("well-known type id -522", error.Message);
        Assert.DoesNotContain("BAML+0x1744C", error.Message);
        Assert.DoesNotContain("element Border on the template", error.Message);
        Assert.DoesNotContain("SimpleItemsControl", error.Message);
        Assert.DoesNotContain("BAML+0x17483", error.Message);
        Assert.DoesNotContain("Sprite", error.Message);
        Assert.DoesNotContain("IsDragElement", error.Message);
        Assert.DoesNotContain("HandyControl.Controls.SimpleItemsControl", error.Message);
    }

    [Fact]
    public void Staged_boot_advances_past_notification_to_rectangle_trigger_stop()
    {
        var entry = LoadStagedHandyControlEntry();
        var error = Assert.Throws<InvalidOperationException>(() => WpfAppBoot.Boot(entry));
        Assert.Contains("startup failed in entry point", error.Message);
        // The line-17 SkinDefault stop, the line-23 Theme key/index plus
        // scalar run, PathBaseStyle, the x:Static-keyed Double, the six
        // converted Behavior entries, the implicit BlendEffectBox style, the
        // 'BorderCircular' entry, the implicit Col/ContentControl styles,
        // the implicit FlipClock style, the implicit FloatingBlock style,
        // the implicit GridSplitter style, the implicit Notification style
        // and the 'SnapRect4ScreenshotStyle' flat run plus its Trigger list now resolve; boot fails past
        // the TargetArea4ScreenshotStyle entry - the precise next blocker.
        Assert.DoesNotContain("SkinDefault", error.Message);
        var inner = Assert.IsType<XamlLoadException>(error.InnerException);
        Assert.DoesNotContain("PathBaseStyle", inner.Message);
        Assert.DoesNotContain("BAML+0x166FF", inner.Message);
        Assert.DoesNotContain("BAML+0x167B8", inner.Message);
        Assert.DoesNotContain("x:Static", inner.Message);
        Assert.DoesNotContain("VerticalScrollBarButtonHeightKey", inner.Message);
        Assert.DoesNotContain("BAML+0x167C8", inner.Message);
        Assert.DoesNotContain("BehaviorXY200", inner.Message);
        Assert.DoesNotContain("FluidMoveBehavior", inner.Message);
        Assert.DoesNotContain("BAML+0x168D2", inner.Message);
        Assert.DoesNotContain("BlendEffectBox", inner.Message);
        Assert.DoesNotContain("BAML+0x16926", inner.Message);
        Assert.DoesNotContain("BorderCircular", inner.Message);
        Assert.Contains("Themes/Theme.xaml", inner.Message);
        Assert.Contains("line 23", inner.Message);
        Assert.DoesNotContain("BAML+0x16954", inner.Message);
        Assert.DoesNotContain("HandyControl.Controls.Col", inner.Message);
        Assert.DoesNotContain("well-known member id -132", inner.Message);
        Assert.DoesNotContain("BAML+0x16BBB", inner.Message);
        Assert.DoesNotContain("HandyControl.Controls.FloatingBlock", inner.Message);
        Assert.DoesNotContain("(implicit style key 'GridSplitter')", inner.Message);
        Assert.DoesNotContain("BAML+0x16C96", inner.Message);
        Assert.DoesNotContain("HandyControl.Controls.Notification", inner.Message);
        Assert.DoesNotContain("setter property owner Window", inner.Message);
        Assert.DoesNotContain("BAML+0x16E7C", inner.Message);
        Assert.DoesNotContain("RectangleCircular", inner.Message);
        Assert.DoesNotContain("SnapRect4ScreenshotStyle", inner.Message);
        Assert.DoesNotContain("TargetArea4ScreenshotStyle", inner.Message);
        Assert.DoesNotContain("holds a Trigger list", inner.Message);
        Assert.DoesNotContain("BAML+0x1760B", inner.Message);
        Assert.DoesNotContain("BAML+0x17C06", inner.Message);
        Assert.DoesNotContain("Right2LeftTransition", inner.Message);
        Assert.DoesNotContain("SliderRepeatButtonHorizontalStyle", inner.Message);
        Assert.DoesNotContain("SliderRepeatButtonVerticalStyle", inner.Message);
        Assert.DoesNotContain("CompareSliderRepeatButtonTransparent", inner.Message);
        Assert.DoesNotContain("ScrollBarBaseRepeatButton", inner.Message);
        Assert.DoesNotContain("BAML+0x17F4C", inner.Message);
        Assert.DoesNotContain("ToggleBlockBaseStyle", inner.Message);
        Assert.DoesNotContain("well-known member id -38", inner.Message);
        Assert.DoesNotContain("BAML+0x1835D", inner.Message);
        Assert.DoesNotContain("GroupBoxBaseTopTemplate", inner.Message);
        Assert.DoesNotContain("BAML+0x1859F", inner.Message);
        Assert.DoesNotContain("GroupBoxBaseLeftTemplate", inner.Message);
        Assert.DoesNotContain("holds ControlTemplate", inner.Message);
        Assert.DoesNotContain("BAML+0x1888C", inner.Message);
        Assert.DoesNotContain("GroupBoxTabTopTemplate", inner.Message);
        Assert.DoesNotContain("GroupBoxTabLeftTemplate", inner.Message);
        Assert.DoesNotContain("template Binding holds record 0x38", inner.Message);
        Assert.DoesNotContain("BAML+0x18D3F", inner.Message);
        Assert.DoesNotContain("ButtonGroupHorizontalUniformItemsPanelTemplate", inner.Message);
        Assert.DoesNotContain("holds well-known type id -330", inner.Message);
        Assert.DoesNotContain("BAML+0x18DBD", inner.Message);
        Assert.DoesNotContain("ProgressBarIndicatorAnimatedFill", inner.Message);
        Assert.DoesNotContain("holds well-known type id -358", inner.Message);
        Assert.DoesNotContain("BAML+0x18E5D", inner.Message);
        Assert.DoesNotContain("BAML+0x19029", inner.Message);
        Assert.DoesNotContain("CircleProgressBarTemplate", inner.Message);
        Assert.DoesNotContain("VirtualizingStackHorizontalItemsPanelTemplate", inner.Message);
        Assert.DoesNotContain("WrapHorizontalItemsPanelTemplate", inner.Message);
        Assert.DoesNotContain("WrapVerticalItemsPanelTemplate", inner.Message);
        Assert.DoesNotContain("DockItemsPanelTemplate", inner.Message);
        Assert.DoesNotContain("ResizeGripperForeground", inner.Message);
        Assert.DoesNotContain("SineEaseOut", inner.Message);
        Assert.DoesNotContain("BAML+0x19150", inner.Message);
        Assert.DoesNotContain("BadgeDotTemplate", inner.Message);
        Assert.DoesNotContain("element ContentPresenter on the template root panel", inner.Message);
        Assert.DoesNotContain("root panels take ContentPresenter, Border, converted Shape and TextBlock children", inner.Message);
        Assert.DoesNotContain("BAML+0x1927D", inner.Message);
        Assert.DoesNotContain("ColorPickerSliderThumbTemplate", inner.Message);
        Assert.DoesNotContain("BAML+0x192B9", inner.Message);
        Assert.DoesNotContain("ColorPickerRainbowBrush", inner.Message);
        Assert.DoesNotContain("has no Offset", inner.Message);
        Assert.DoesNotContain("BAML+0x19383", inner.Message);
        Assert.DoesNotContain("ColorPickerOpacityBrush", inner.Message);
        Assert.DoesNotContain("well-known type id -180", inner.Message);
        Assert.DoesNotContain("BAML+0x19497", inner.Message);
        Assert.DoesNotContain("CoverFlowBaseStyle", inner.Message);
        Assert.DoesNotContain("well-known type id -729", inner.Message);
        Assert.DoesNotContain("BAML+0x195FD", inner.Message);
        Assert.DoesNotContain("GridViewColumnHeaderGripper", inner.Message);
        Assert.DoesNotContain("well-known type id -66", inner.Message);
        Assert.DoesNotContain("Canvas.Right", inner.Message);
        Assert.DoesNotContain("BAML+0x196C6", inner.Message);
        Assert.DoesNotContain("'Padding' is not a dependency property of Border", inner.Message);
        Assert.DoesNotContain("SortByCategoryDrawingBrush", inner.Message);
        Assert.DoesNotContain("BAML+0x196F6", inner.Message);
        Assert.DoesNotContain("SortByNameDrawingBrush", inner.Message);
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
        Assert.DoesNotContain("DataGridTextColumnStyle", inner.Message);        
        Assert.DoesNotContain("CarouselItemBaseStyle", inner.Message);
        Assert.DoesNotContain("BAML+0x1A113", inner.Message);
        Assert.DoesNotContain("BAML+0x1A186", inner.Message);        
        Assert.DoesNotContain("BAML+0x1A21B", inner.Message);
        Assert.DoesNotContain("well-known type id -92", inner.Message);
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
        Assert.DoesNotContain("BAML+0x1A9DD", inner.Message);
        Assert.DoesNotContain("element well-known type id -550 on the template root panel", inner.Message);
        Assert.DoesNotContain("BAML+0x1AE91", inner.Message);
        Assert.DoesNotContain("'EffectShadow1'", inner.Message);
        Assert.DoesNotContain("record 0x34 (converted-element values", inner.Message);
        Assert.DoesNotContain("BAML+0x1B001", inner.Message);
        Assert.DoesNotContain("BAML+0x1B22E", inner.Message);
        Assert.DoesNotContain("'SearchPathStyle'", inner.Message);
        Assert.DoesNotContain("'RightPathStyle'", inner.Message);
        Assert.DoesNotContain("record 0x38 in a Style", inner.Message);
        Assert.DoesNotContain("'RenderTransformOrigin' is not a dependency property of UIElement", inner.Message);
        Assert.DoesNotContain("BAML+0x1B260", inner.Message);
        Assert.DoesNotContain("well-known member id -134", inner.Message);
        Assert.Contains("'BorderRegion'", inner.Message);
        Assert.Contains("BAML+0x1B2F9", inner.Message);
        Assert.Contains("setter property owner Border in style entry 'BorderRegion'", inner.Message);
        Assert.DoesNotContain("BAML+0x1AA35", inner.Message);
        Assert.DoesNotContain("'PrimaryBrush'", inner.Message);
        Assert.DoesNotContain("record 0x34 in a LinearGradientBrush", inner.Message);
        Assert.DoesNotContain("BAML+0x1AA1C", inner.Message);
        Assert.DoesNotContain("LightPrimaryBrush", inner.Message);
        Assert.DoesNotContain("well-known type id -578", inner.Message);
        Assert.DoesNotContain("on setter 'Template'", inner.Message);
        Assert.DoesNotContain("well-known type id -658", inner.Message);
        Assert.DoesNotContain("HandyControl.Controls.SimplePanel", inner.Message);
        Assert.DoesNotContain("VirtualizingStackPanel", inner.Message);
        Assert.DoesNotContain("templates take a ContentPresenter", inner.Message);
        Assert.DoesNotContain("items panels take a UniformGrid/StackPanel/VirtualizingStackPanel/WrapPanel/DockPanel visual", inner.Message);

        Assert.DoesNotContain("well-known type id -608", inner.Message);
        Assert.DoesNotContain("well-known type id -522", inner.Message);
        Assert.DoesNotContain("BAML+0x1744C", inner.Message);
        Assert.DoesNotContain("element Border on the template", inner.Message);
        Assert.DoesNotContain("SimpleItemsControl", inner.Message);
        Assert.DoesNotContain("BAML+0x17483", inner.Message);
        Assert.DoesNotContain("Sprite", inner.Message);
        Assert.DoesNotContain("IsDragElement", inner.Message);
        Assert.DoesNotContain("HandyControl.Controls.SimpleItemsControl", inner.Message);
    }

    [Fact]
    public void Staged_shape_notification_materializes_with_nine_setters()
    {
        // The staged #107 entry shape (implicit Notification style with the
        // two WindowAttach attached setters, five Window-qualified setters,
        // the Transparent Background brush value and the Margin/Padding
        // TemplateBinding plus Center-alignment template) lands as a live
        // Style against the staged converted lib.
        LoadStagedHandyControlEntry();
        var handyControl = AppDomain.CurrentDomain.GetAssemblies()
            .First(a => string.Equals(a.GetName().Name, "HandyControl", StringComparison.OrdinalIgnoreCase));
        var notification = handyControl.GetType("HandyControl.Controls.Notification");
        Assert.NotNull(notification);
        var attach = handyControl.GetType("HandyControl.Controls.WindowAttach");
        Assert.NotNull(attach);
        var ignoreAltF4 = Assert.IsType<DependencyProperty>(attach!.GetField("IgnoreAltF4Property")!.GetValue(null));
        Assert.True(ignoreAltF4.IsAttached);
        var showInTaskManager = Assert.IsType<DependencyProperty>(attach.GetField("ShowInTaskManagerProperty")!.GetValue(null));
        Assert.True(showInTaskManager.IsAttached);
        var bytes = NotificationDictionary();
        var dictionary = BamlReader.LoadDictionary(bytes);
        var style = Assert.IsType<Style>(dictionary[notification!]);
        Assert.Same(notification, style.TargetType);
        Assert.Equal(9, style.Setters.Count);
        var ignore = Assert.IsType<Setter>(style.Setters[0]);
        Assert.Same(ignoreAltF4, ignore.Property);
        Assert.Equal(true, ignore.Value);
        var manager = Assert.IsType<Setter>(style.Setters[1]);
        Assert.Same(showInTaskManager, manager.Property);
        Assert.Equal(false, manager.Value);
        var sizeToContent = Assert.IsType<Setter>(style.Setters[2]);
        Assert.Same(Window.SizeToContentProperty, sizeToContent.Property);
        Assert.Equal(SizeToContent.WidthAndHeight, sizeToContent.Value);
        var resizeMode = Assert.IsType<Setter>(style.Setters[3]);
        Assert.Same(Window.ResizeModeProperty, resizeMode.Property);
        Assert.Equal(ResizeMode.NoResize, resizeMode.Value);
        var background = Assert.IsType<Setter>(style.Setters[4]);
        Assert.Same(Control.BackgroundProperty, background.Property);
        Assert.Equal(Color.FromArgb(0, 255, 255, 255), Assert.IsType<SolidColorBrush>(background.Value).Color);
        var showActivated = Assert.IsType<Setter>(style.Setters[5]);
        Assert.Same(Window.ShowActivatedProperty, showActivated.Property);
        Assert.Equal(false, showActivated.Value);
        var showInTaskbar = Assert.IsType<Setter>(style.Setters[6]);
        Assert.Same(Window.ShowInTaskbarProperty, showInTaskbar.Property);
        Assert.Equal(false, showInTaskbar.Value);
        var topmost = Assert.IsType<Setter>(style.Setters[7]);
        Assert.Same(Window.TopmostProperty, topmost.Property);
        Assert.Equal(true, topmost.Value);
        var templateSetter = Assert.IsType<Setter>(style.Setters[8]);
        Assert.Same(Control.TemplateProperty, templateSetter.Property);
        var template = Assert.IsType<ControlTemplate>(templateSetter.Value);
        Assert.Same(notification, template.TargetType);
        var presenter = Assert.IsType<ContentPresenter>(template.VisualTree);
        Assert.Equal(HorizontalAlignment.Center, presenter.HorizontalAlignment);
        Assert.Equal(VerticalAlignment.Center, presenter.VerticalAlignment);
        var binding = Assert.Single(template.TemplateBindings);
        Assert.Same(presenter, binding.Element);
        Assert.Equal("Padding", binding.Property);
    }

    [Fact]
    public void Staged_shape_rectangle_circular_materializes_flat_run()
    {
        // The staged #108 entry shape (keyed 'SnapRect4ScreenshotStyle' style over
        // well-known Rectangle with the RectangleAttach.Circular attached
        // setter plus the Height/Width/Fill/HorizontalAlignment/VerticalAlignment/Visibility
        // VerticalAlignment flat run) lands as a live Style against the
        // staged converted lib and applies to a real Rectangle.
        LoadStagedHandyControlEntry();
        var handyControl = AppDomain.CurrentDomain.GetAssemblies()
            .First(a => string.Equals(a.GetName().Name, "HandyControl", StringComparison.OrdinalIgnoreCase));
        var attach = handyControl.GetType("HandyControl.Controls.RectangleAttach");
        Assert.NotNull(attach);
        var circular = Assert.IsType<DependencyProperty>(attach!.GetField("CircularProperty")!.GetValue(null));
        Assert.True(circular.IsAttached);
        var bytes = KeyedRectangleDictionary();
        var dictionary = BamlReader.LoadDictionary(bytes);
        var style = Assert.IsType<Style>(dictionary["SnapRect4ScreenshotStyle"]);
        Assert.Same(typeof(Rectangle), style.TargetType);
        Assert.Equal(7, style.Setters.Count);
        var circularSetter = Assert.IsType<Setter>(style.Setters[0]);
        Assert.Same(circular, circularSetter.Property);
        Assert.Equal(true, circularSetter.Value);
        Assert.Same(FrameworkElement.HeightProperty, Assert.IsType<Setter>(style.Setters[1]).Property);
        Assert.Equal(5.0, Assert.IsType<Setter>(style.Setters[1]).Value);
        Assert.Same(FrameworkElement.WidthProperty, Assert.IsType<Setter>(style.Setters[2]).Property);
        Assert.Equal(5.0, Assert.IsType<Setter>(style.Setters[2]).Value);
        var fill = Assert.IsType<Setter>(style.Setters[3]);
        Assert.Same(Shape.FillProperty, fill.Property);
        Assert.Equal(Color.FromArgb(255, 30, 144, 255), Assert.IsType<SolidColorBrush>(fill.Value).Color);
        Assert.Equal(HorizontalAlignment.Left, Assert.IsType<Setter>(style.Setters[4]).Value);
        Assert.Equal(VerticalAlignment.Top, Assert.IsType<Setter>(style.Setters[5]).Value);
        Assert.Equal(Visibility.Collapsed, Assert.IsType<Setter>(style.Setters[6]).Value);
        // Applying the whole style fires the converted Circular callback
        // (pinned by Staged_circular_apply_reaches_converted_callback below),
        // so the flat run pins landing here.
    }

    [Fact]
    public void Staged_circular_apply_reaches_converted_callback()
    {
        // The staged #108 Circular setter lands the converted attached DP,
        // and applying it fires the converted OnCircularChanged callback
        // (IL-verified: MultiBinding over ActualWidth/ActualHeight driving
        // RadiusX/RadiusY through RectangleCircularConverter): the engine
        // now owns the ActualWidth/ActualHeight/RadiusX/RadiusY identities
        // plus the composite transfer (slice-125), so the callback attaches
        // both transfers and the pre-layout radii read 0 (slice-125 pins
        // the layout refresh and the clear path).
        LoadStagedHandyControlEntry();
        var bytes = KeyedRectangleDictionary();
        var dictionary = BamlReader.LoadDictionary(bytes);
        var style = Assert.IsType<Style>(dictionary["SnapRect4ScreenshotStyle"]);
        var rectangle = new Rectangle();
        style.ApplyTo(rectangle);
        Assert.IsType<MultiBindingExpression>(
            rectangle.GetBindingExpressionBase(Rectangle.RadiusXProperty));
        Assert.IsType<MultiBindingExpression>(
            rectangle.GetBindingExpressionBase(Rectangle.RadiusYProperty));
        Assert.Equal(0.0, rectangle.RadiusX);
        Assert.Equal(0.0, rectangle.RadiusY);
    }

    /// <summary>Builds a single-entry implicit-style dictionary over the
    /// engine Window TypeInfo row with Window-qualified (-739) setters.
    /// </summary>
    private static byte[] QualifiedWindowDictionary(params (string Name, string Text, short Converter)[] setters)
    {
        var entry = new B()
            .Raw(0x03).I16(-620).Raw(0x00)
            .Raw(0x22).I16(0).I16(0)
            .Raw(0x2E).I16(-238);
        foreach (var (name, text, converter) in setters)
            entry
                .Raw(0x03).I16(-556).Raw(0x00)
                .Sized(0x06, q => q.I16(1).U16(16521).I16(-739).Str(name))
                .Sized(0x24, q => q.I16(2).Str(text).I16(converter))
                .Raw(0x04);
        entry.Raw(0x04);
        return ImplicitDictionary(entry.ToArray(), 0,
            [("DesktopWasm.Wpf.Window", (ushort)0)],
            [(0, "TargetType", -620), (1, "Property", -556), (2, "Value", -556)]);
    }

    /// <summary>Builds a single-entry implicit-style dictionary with one
    /// short-form setter carrying a packed-ARGB brush value (the staged
    /// #107/#108 shape).</summary>
    private static byte[] BrushDictionary(string fullName, short member, params byte[] payload)
    {
        var content = new B()
            .Raw(0x03).I16(-620).Raw(0x00)
            .Raw(0x22).I16(0).I16(0)
            .Raw(0x2E).I16(-238)
            .Raw(0x03).I16(-556).Raw(0x00)
            .Sized(0x06, q => q.I16(1).U16(137).I16(member))
            .Sized(0x06, q => q.I16(2).U16(744).Raw(payload))
            .Raw(0x04)
            .Raw(0x04).ToArray();
        return ImplicitDictionary(content, 0,
            [(fullName, (ushort)0)],
            [(0, "TargetType", -620), (1, "Property", -556), (2, "Value", -556)]);
    }

    /// <summary>Builds a single-entry implicit-style dictionary with one
    /// short-form setter carrying a text value.</summary>
    private static byte[] ShortDictionary(string fullName, short member, string text, short converter)
    {
        var content = new B()
            .Raw(0x03).I16(-620).Raw(0x00)
            .Raw(0x22).I16(0).I16(0)
            .Raw(0x2E).I16(-238)
            .Raw(0x03).I16(-556).Raw(0x00)
            .Sized(0x06, q => q.I16(1).U16(137).I16(member))
            .Sized(0x24, q => q.I16(2).Str(text).I16(converter))
            .Raw(0x04)
            .Raw(0x04).ToArray();
        return ImplicitDictionary(content, 0,
            [(fullName, (ushort)0)],
            [(0, "TargetType", -620), (1, "Property", -556), (2, "Value", -556)]);
    }

    /// <summary>Builds the staged #108 flat run over the well-known
    /// Rectangle target: Height/Width ("5"/LengthConverter), Fill (744
    /// DodgerBlue), HorizontalAlignment/VerticalAlignment
    /// (Left/Top/EnumConverter) and Visibility (Collapsed/EnumConverter).
    /// </summary>
    private static byte[] RectangleFlatDictionary()
    {
        B Short(B entry, short member) => entry
            .Raw(0x03).I16(-556).Raw(0x00)
            .Sized(0x06, q => q.I16(1).U16(137).I16(member));
        var entry = new B()
            .Raw(0x03).I16(-620).Raw(0x00)
            .Raw(0x22).I16(0).I16(-514)
            .Raw(0x2E).I16(-238);
        Short(entry, -47);
        entry.Sized(0x24, q => q.I16(2).Str("5").I16(-348)).Raw(0x04);
        Short(entry, -57);
        entry.Sized(0x24, q => q.I16(2).Str("5").I16(-348)).Raw(0x04);
        Short(entry, -100);
        entry.Sized(0x06, q => q.I16(2).U16(744).Raw(0x01, 0xFF, 0x90, 0x1E, 0xFF)).Raw(0x04);
        Short(entry, -48);
        entry.Sized(0x24, q => q.I16(2).Str("Left").I16(-195)).Raw(0x04);
        Short(entry, -56);
        entry.Sized(0x24, q => q.I16(2).Str("Top").I16(-195)).Raw(0x04);
        Short(entry, -135);
        entry.Sized(0x24, q => q.I16(2).Str("Collapsed").I16(-195)).Raw(0x04);
        entry.Raw(0x04);
        return ImplicitDictionary(entry.ToArray(), -514, null,
            [(0, "TargetType", -620), (1, "Property", -556), (2, "Value", -556)]);
    }

    /// <summary>Builds a single-entry implicit-style dictionary with a
    /// Template setter holding a ControlTemplate with a ContentPresenter
    /// carrying the Margin TemplateBinding plus Center alignment PWCs (the
    /// staged #107 template shape).</summary>
    private static byte[] PresenterCenterDictionary(string fullName)
    {
        var entry = new B()
            .Raw(0x03).I16(-620).Raw(0x00)
            .Raw(0x22).I16(0).I16(0)
            .Raw(0x2E).I16(-238)
            .Raw(0x03).I16(-556).Raw(0x00)
            .Sized(0x06, q => q.I16(1).U16(137).I16(-37))
            .Raw(0x07).I16(2)
            .Raw(0x03).I16(-108).Raw(0x00)
            .Raw(0x22).I16(3).I16(0)
            .Raw(0x2E).I16(-174)
            .Raw(0x03).I16(-102).Raw(0x00)
            .Raw(0x23).I16(-49).U16(634).I16(-35)
            .Sized(0x24, q => q.I16(-48).Str("Center").I16(-195))
            .Sized(0x24, q => q.I16(-56).Str("Center").I16(-195))
            .Raw(0x04)
            .Raw(0x04)
            .Raw(0x08)
            .Raw(0x04)
            .Raw(0x04);
        return ImplicitDictionary(entry.ToArray(), 0,
            [(fullName, (ushort)0)],
            [(0, "TargetType", -620), (1, "Property", -556), (2, "Value", -556), (3, "TargetType", -108)]);
    }

    /// <summary>Builds the staged #107 entry shape: an implicit style over
    /// the converted Notification TypeInfo row plus the WindowAttach
    /// TypeInfo row, with the two attached setters, five Window-qualified
    /// setters, the Transparent Background brush value and the
    /// Margin/Padding plus Center-alignment template.</summary>
    private static byte[] NotificationDictionary()
    {
        var entry = new B()
            .Raw(0x03).I16(-620).Raw(0x00)
            .Raw(0x22).I16(0).I16(0)
            .Raw(0x2E).I16(-238);
        void Qualified(short owner, string name, string text, short converter)
        {
            entry
                .Raw(0x03).I16(-556).Raw(0x00)
                .Sized(0x06, q => q.I16(1).U16(16521).I16(owner).Str(name))
                .Sized(0x24, q => q.I16(2).Str(text).I16(converter))
                .Raw(0x04);
        }
        Qualified(1, "IgnoreAltF4", "True", -46);
        Qualified(1, "ShowInTaskManager", "False", -46);
        Qualified(-739, "SizeToContent", "WidthAndHeight", -195);
        Qualified(-739, "ResizeMode", "NoResize", -195);
        entry
            .Raw(0x03).I16(-556).Raw(0x00)
            .Sized(0x06, q => q.I16(1).U16(137).I16(-24))
            .Sized(0x06, q => q.I16(2).U16(744).Raw(0x01, 0xFF, 0xFF, 0xFF, 0x00))
            .Raw(0x04);
        Qualified(-739, "ShowActivated", "False", -46);
        Qualified(-739, "ShowInTaskbar", "False", -46);
        Qualified(-739, "Topmost", "True", -46);
        entry
            .Raw(0x03).I16(-556).Raw(0x00)
            .Sized(0x06, q => q.I16(1).U16(137).I16(-37))
            .Raw(0x07).I16(2)
            .Raw(0x03).I16(-108).Raw(0x00)
            .Raw(0x22).I16(3).I16(0)
            .Raw(0x2E).I16(-174)
            .Raw(0x03).I16(-102).Raw(0x00)
            .Raw(0x23).I16(-49).U16(634).I16(-35)
            .Sized(0x24, q => q.I16(-48).Str("Center").I16(-195))
            .Sized(0x24, q => q.I16(-56).Str("Center").I16(-195))
            .Raw(0x04)
            .Raw(0x04)
            .Raw(0x08)
            .Raw(0x04)
            .Raw(0x04);
        return ImplicitDictionary(entry.ToArray(), 0,
            [("HandyControl.Controls.Notification", (ushort)0), ("HandyControl.Controls.WindowAttach", (ushort)1)],
            [(0, "TargetType", -620), (1, "Property", -556), (2, "Value", -556), (3, "TargetType", -108)]);
    }

    /// <summary>Builds the staged #108 flat-run shape: a keyed
    /// 'SnapRect4ScreenshotStyle' style over well-known Rectangle with the
    /// converted RectangleAttach.Circular attached setter plus the
    /// Height/Width/Fill/HorizontalAlignment/VerticalAlignment/Visibility
    /// flat run.</summary>
    private static byte[] KeyedRectangleDictionary()
    {
        var content = new B()
            .Raw(0x03).I16(-620).Raw(0x00)
            .Raw(0x22).I16(0).I16(-514)
            .Raw(0x2E).I16(-238)
            .Raw(0x03).I16(-556).Raw(0x00)
            .Sized(0x06, q => q.I16(1).U16(16521).I16(0).Str("Circular"))
            .Sized(0x24, q => q.I16(2).Str("True").I16(-46))
            .Raw(0x04);
        void Short(short member, Action<B> value)
        {
            content
                .Raw(0x03).I16(-556).Raw(0x00)
                .Sized(0x06, q => q.I16(1).U16(137).I16(member));
            value(content);
            content.Raw(0x04);
        }
        Short(-47, q => q.Sized(0x24, r => r.I16(2).Str("5").I16(-348)));
        Short(-57, q => q.Sized(0x24, r => r.I16(2).Str("5").I16(-348)));
        Short(-100, q => q.Sized(0x06, r => r.I16(2).U16(744).Raw(0x01, 0xFF, 0x90, 0x1E, 0xFF)));
        Short(-48, q => q.Sized(0x24, r => r.I16(2).Str("Left").I16(-195)));
        Short(-56, q => q.Sized(0x24, r => r.I16(2).Str("Top").I16(-195)));
        Short(-135, q => q.Sized(0x24, r => r.I16(2).Str("Collapsed").I16(-195)));
        content.Raw(0x04);
        var head = new B().Raw(Header()).Raw(0x01)
            .Raw(0x03).I16(-524).Raw(0x00)
            .Sized(0x1C, p => p.U16(0).Str("Engine, Version=1.0.0.0"))
            .Sized(0x1D, p => p.U16(0).U16(0).Str("HandyControl.Controls.RectangleAttach"))
            .Sized(0x1F, p => p.I16(0).I16(-620).Raw(0x00).Str("TargetType"))
            .Sized(0x1F, p => p.I16(1).I16(-556).Raw(0x00).Str("Property"))
            .Sized(0x1F, p => p.I16(2).I16(-556).Raw(0x00).Str("Value"))
            .Sized(0x20, p => p.U16(0).Str("SnapRect4ScreenshotStyle"))
            .Deferable()
            .Sized(0x26, p => p.U16(0).U32(0).U16(0));
        return head.Raw(content.ToArray()).FinishDictionary().ToArray();
    }

    /// <summary>Builds a single-entry implicit-style dictionary (TypeKey
    /// 0x27) over the given TypeInfo/AttributeInfo tables.</summary>
    private static byte[] ImplicitDictionary(byte[] entry, short keyType, (string FullName, ushort Id)[]? types, (int Id, string Name, short Owner)[] attrs)
    {
        var head = new B().Raw(Header()).Raw(0x01)
            .Raw(0x03).I16(-524).Raw(0x00)
            .Sized(0x1C, p => p.U16(0).Str("Engine, Version=1.0.0.0"));
        if (types is not null)
            foreach (var (fullName, id) in types)
                head.Sized(0x1D, p => p.U16(id).U16(0).Str(fullName));
        foreach (var (id, name, owner) in attrs)
            head.Sized(0x1F, p => p.I16((short)id).I16(owner).Raw(0x00).Str(name));
        return head
            .Raw(0x25).U32((uint)(entry.Length + 10))
            .Raw(0x27).I16(keyType).Raw(0x00).U32(0).Raw(0x00, 0x00)
            .Raw(entry)
            .Raw(0x04, 0x02).ToArray();
    }

    public sealed class Slice123OwnerStub
    {
        public static readonly DependencyProperty AlphaProperty =
            DependencyProperty.RegisterAttached("Alpha", typeof(bool), typeof(Slice123OwnerStub), new PropertyMetadata(false));

        public static readonly DependencyProperty BetaProperty =
            DependencyProperty.RegisterAttached("Beta", typeof(bool), typeof(Slice123OwnerStub), new PropertyMetadata(false));
    }

    private sealed class B
    {
        private readonly List<byte> _b = new();
        private int _deferPos = -1;
        public B Raw(params byte[] b) { _b.AddRange(b); return this; }
        public B U16(ushort v) { _b.AddRange(BitConverter.GetBytes(v)); return this; }
        public B I16(short v) { _b.AddRange(BitConverter.GetBytes(v)); return this; }
        public B U32(uint v) { _b.AddRange(BitConverter.GetBytes(v)); return this; }
        public B Str(string s)
        {
            var ascii = Encoding.ASCII.GetBytes(s);
            Seven(ascii.Length);
            _b.AddRange(ascii);
            return this;
        }
        public B Seven(int v)
        {
            uint u = (uint)v;
            while (u >= 0x80) { _b.Add((byte)(u | 0x80)); u >>= 7; }
            _b.Add((byte)u);
            return this;
        }
        public B Sized(byte type, Action<B> payload)
        {
            var inner = new B();
            payload(inner);
            var p = inner._b;
            if (p.Count + 1 >= 128) throw new InvalidOperationException("test record too large");
            _b.Add(type);
            _b.Add((byte)(p.Count + 1));
            _b.AddRange(p);
            return this;
        }
        public B Deferable()
        {
            _b.Add(0x25);
            _deferPos = _b.Count;
            _b.AddRange(new byte[4]);
            return this;
        }
        public B FinishDictionary()
        {
            if (_deferPos < 0) throw new InvalidOperationException("no open deferable");
            var endPos = _b.Count;
            _b.Add(0x04);
            var skip = (uint)(endPos - (_deferPos + 4));
            var patch = BitConverter.GetBytes(skip);
            for (var i = 0; i < 4; i++) _b[_deferPos + i] = patch[i];
            _b.Add(0x02);
            _deferPos = -1;
            return this;
        }
        public byte[] ToArray() => _b.ToArray();
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
                XamlLoader.CustomElementSource = hit;
                return hit;
            }
            if (_stagedEntry is not null)
            {
                XamlLoader.CustomElementSource = _stagedEntry;
                return _stagedEntry;
            }
            var apps = StagedHandyAppsDirectory();
            using var document = JsonDocument.Parse(
                File.ReadAllText(System.IO.Path.Combine(apps, "app-manifest.json")));
            var images = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var assembly in document.RootElement.GetProperty("assemblies").EnumerateArray())
            {
                var file = assembly.GetProperty("file").GetString()!;
                images[assembly.GetProperty("name").GetString()!] = File.ReadAllBytes(System.IO.Path.Combine(apps, file));
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
            XamlLoader.CustomElementSource = _stagedEntry;
            return _stagedEntry;
        }
    }
}
