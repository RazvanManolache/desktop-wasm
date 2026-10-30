// Copyright (c) 2026 The WPF on WASM Contributors.
// Licensed under the MIT License. See LICENSE in the project root.

using System.Reflection;
using System.Resources;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DesktopWasm.Wpf.Tests;

/// <summary>Slice-153: the keyed 'ListBoxItemCustom' Style entry (keyidx 158
/// at BAML+0x1A364, ListBoxItem target well-known type id -374) routes as a
/// live engine Style, so the staged boot stops precise at the implicit
/// RibbonTab style entry at BAML+0x1A8EA (short-form ItemsPanel member
/// well-known id -81 with no short-form routing - the next slice's work).
/// Live counts verified first: handycontrol conversion-failed/6 staged/2 gaps
/// (both declared absent binaries System.Security.Permissions +
/// System.Windows.Extensions with no browser path; Convert map untouched,
/// all 6 staged images sha256-verified, manifest hand-untouched;
/// staged-oracle status-gate bypass stays in-probe only). Triangulation three
/// ways: 22 staged framing spans against the live 466350-byte theme.baml
/// (sha256 D305CC52C4F8A6EB077D1CC79F556BD343830E30C7C67FC73D3988C63AAF65D0,
/// verified live) - the ListBoxItemCustom entry [0x1A364, 0x1A4BC) head
/// 03 94 FD 00 22 00 00 8A FE (TargetType record at BAML+0x1A368), qualified
/// UIElement SnapsToDevicePixels True/BooleanConverter, qualified
/// FrameworkElement FocusVisualStyle x:Null, short-form Padding -35 with the
/// 0/ThicknessConverter -143 value, short-form HorizontalContentAlignment -33
/// with the ItemTemplate-style Binding (Path plus AncestorType ItemsControl
/// RelativeSource), short-form VerticalContentAlignment -38 Top/EnumConverter,
/// short-form Background -24 plus BorderBrush -25 Transparent packed-ARGB
/// brush values, short-form BorderThickness -26 with the 0/-143 value, short-
/// form Template -37 with the ControlTemplate/ListBoxItem TargetType/
/// ContentPresenter root carrying the three TemplateBinding triples
/// (HorizontalAlignment/SnapsToDevicePixels/VerticalAlignment), the next
/// entry head 03 3A 00 00 04, key record 0x26 key id 0x9E at 0xB5C3, key-string
/// row 0x9E 'ListBoxItemCustom' at 0x2613, TypeInfo 57 GrowlWindow row at
/// 0x25EB, and the implicit RibbonTab head 03 94 FD 00 22 00 00 3D 00 at
/// BAML+0x1A8EA with the failing short-form ItemsPanel -81 at BAML+0x1A92D,
/// the implicit key record 0x27 type 0x3D at 0xB5FD and the TypeInfo 61
/// RibbonTab row at 0x2795; the live upstream HandyControl ListBox.xaml
/// ListBoxItemCustom block at pinned commit e9ad3d70 (Snaps/FocusVisualStyle/
/// Padding/HContent-AncestorType/VContent/Background/BorderBrush/
/// BorderThickness/Template setters with the ContentPresenter template,
/// fetched live as raw ListBox.xaml); a genuine net10.0-windows
/// ResourceDictionary fixture compiling the exact upstream block
/// (ListBoxItem.baml, byte-identical in every staged record shape modulo
/// per-file numbering and line markers: TargetType 8A FE, short-form
/// Padding/HContent/VContent/Background/BorderBrush/BorderThickness/Template
/// members, ThicknessConverter -143, AncestorType Property 0x05 with a
/// per-file RelativeSource-owned row, TemplateBindingExtension 634 triples;
/// fixture retained at E:/tmp/bamlfix/ListBoxItem.xaml outside the tree) plus
/// a genuine net10.0-windows reflection probe (Control.Padding/BorderBrush/
/// BorderThickness Control-owned, Thickness/Brush-typed, 0/0,0,0,0 defaults;
/// FrameworkElement.FocusVisualStyle Style-typed read-write;
/// ListBoxItem : ContentControl; ItemsControl : Control). Two mid-slice
/// corrections: the first probe harness re-loaded staged images on every
/// AssemblyResolve (a same-type duplicate LangProvider error at BAML+0xF197 -
/// a harness artifact, fixed same-session to the established resolve-first
/// loader, after which the true RibbonTab stop surfaced); the entry end first
/// computed as 0x1A4BE (fixed same-session to 0x1A4BC from the exact
/// 04 04 08 04 04 close run). Real-behavior engine work, never fabricated:
/// BamlReader gains KnownListBoxItem -374 (ResolveTargetTypeId plus
/// WellKnownName arms; ListBoxItem routes to the engine item container as a
/// shim, never ported) and explicit KnownBorderBrushSourceMember (-25) plus
/// KnownBorderThicknessSourceMember (-26) short-form arms resolving through
/// DependencyProperty.FromName against the style TargetType (explicit
/// branches like MaxWidth, so ShortMemberDisplay/ShortMemberList and the
/// trigger surface stay untouched), a NullExtension branch in
/// ParseComplexSetterValue landing null (the FocusVisualStyle x:Null), and an
/// AncestorType 0x05 branch in ParseStyleRelativeSource reusing the template
/// RelativeSource helpers (the HContent ItemsControl binding); Padding (-35),
/// HorizontalContentAlignment (-33) and VerticalContentAlignment (-38) ride
/// the existing ShortMemberDisplay hierarchy walk onto the shared Control
/// DPs, and the ContentPresenter TemplateBindings ride the existing
/// ReadTemplateExtension arms. The entry lands as a live engine Style
/// (ListBoxItem target, 9 setters with exact DP identity and values, shared-
/// key singleton). Re-package skipped by evidence (Convert map untouched,
/// staged bins byte-identical). No browser proof - package stays
/// conversion-failed. Full gate: dotnet test DesktopWasm.slnx green plus
/// node --test tests/studio/core.test.mjs (trx-captured, flakes rerun in
/// isolation); docs/application-compatibility-status.md handycontrol row
/// verified current against the live manifest. Precise next blockers: the
/// implicit RibbonTab style entry (short-form ItemsPanel -81 holding the
/// ItemsPanelTemplate complex at BAML+0x1A92D), then the keyed Growl style,
/// the CarouselPateButton/implicit Carousel styles (BasedOn/StaticResource
/// and DynamicResource setters, EventTriggers/Storyboard/TargetName), the
/// GroupBox styles (BasedOn/DynamicResource setters, TitleElement triggers),
/// EventTriggers, the line-info trailer, the per-file (Type,Name) x:Name rows
/// and the converted-lib elements (CircleProgressBar/GifImage/Watermark/
/// Drawer/ElementGroup/SearchBar) behind the line-17 SkinDefault stop.</summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class Slice153BamlListBoxItemTests : IDisposable
{
    private readonly Application? _previous = Application.Current;

    public Slice153BamlListBoxItemTests()
    {
        XamlLoader.CustomElementSource = null;
    }

    public void Dispose()
    {
        XamlLoader.CustomElementSource = null;
        Application.Current = _previous;
    }

    [Fact]
    public void Staged_manifest_keeps_six_staged_two_absent()
    {
        LoadStagedHandyControlEntry();
        var apps = StagedHandyAppsDirectory();
        using var document = JsonDocument.Parse(File.ReadAllBytes(System.IO.Path.Combine(apps, "app-manifest.json")));
        var root = document.RootElement;
        Assert.Equal("conversion-failed", root.GetProperty("status").GetString());
        var assemblies = root.GetProperty("assemblies").EnumerateArray().ToArray();
        Assert.Equal(6, assemblies.Length);
        var names = assemblies.Select(a => a.GetProperty("name").GetString()).ToArray();
        Assert.Equal(
            new[] { "HandyControlDemo", "HandyControl", "GalaSoft.MvvmLight", "ICSharpCode.AvalonEdit", "HandyControlDemoCode", "Newtonsoft.Json" },
            names);
        foreach (var assembly in assemblies)
        {
            var file = assembly.GetProperty("file").GetString()!;
            var expected = assembly.GetProperty("sha256").GetString()!;
            var actual = Convert.ToHexString(SHA256.HashData(
                File.ReadAllBytes(System.IO.Path.Combine(apps, file))));
            Assert.True(string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase),
                $"hash mismatch in staged '{file}': {actual} != {expected}");
        }
        var gaps = root.GetProperty("gaps").EnumerateArray()
            .Select(g => g.GetProperty("detail").GetString()).ToArray();
        Assert.Equal(2, gaps.Length);
        Assert.Contains(gaps, g => g!.Contains("System.Security.Permissions", StringComparison.Ordinal));
        Assert.Contains(gaps, g => g!.Contains("System.Windows.Extensions", StringComparison.Ordinal));
        var theme = ReadStagedGResource("HandyControl", "themes/theme.baml");
        Assert.Equal(466350, theme.Length);
    }

    [Fact]
    public void Theme_baml_places_listbox_and_ribbontab_bytes()
    {
        LoadStagedHandyControlEntry();
        var theme = ReadStagedGResource("HandyControl", "themes/theme.baml");
        Assert.Equal(466350, theme.Length);
        Assert.Equal(
            "D305CC52C4F8A6EB077D1CC79F556BD343830E30C7C67FC73D3988C63AAF65D0",
            Convert.ToHexString(SHA256.HashData(theme)));

        var listBoxHead = new byte[] { 0x03, 0x94, 0xFD, 0x00, 0x22, 0x00, 0x00, 0x8A, 0xFE, 0x2E, 0x12, 0xFF };
        Assert.Equal(listBoxHead, theme[0x1A364..0x1A370]);
        var snapsSetter = new byte[] { 0x03, 0xD4, 0xFD, 0x00 };
        Assert.Equal(snapsSetter, theme[0x1A370..0x1A374]);
        var snapsQualified = new byte[] { 0x06, 0x1B, 0x01, 0x00, 0x89, 0x40, 0x4A, 0xFD, 0x13 };
        Assert.Equal(snapsQualified, theme[0x1A374..0x1A37D]);
        Assert.Equal(Encoding.ASCII.GetBytes("SnapsToDevicePixels"), theme[0x1A37D..0x1A390]);
        var snapsValue = new byte[] { 0x24, 0x0A, 0x02, 0x00, 0x04, 0x54, 0x72, 0x75, 0x65, 0xD2, 0xFF };
        Assert.Equal(snapsValue, theme[0x1A390..0x1A39B]);
        Assert.Equal(snapsSetter, theme[0x1A39C..0x1A3A0]);
        var focusQualified = new byte[] { 0x06, 0x18, 0x01, 0x00, 0x89, 0x40, 0x1E, 0xFF, 0x10 };
        Assert.Equal(focusQualified, theme[0x1A3A0..0x1A3A9]);
        Assert.Equal(Encoding.ASCII.GetBytes("FocusVisualStyle"), theme[0x1A3A9..0x1A3B9]);
        var nullComplex = new byte[] { 0x07, 0x02, 0x00, 0x03, 0x5A, 0xFE, 0x00, 0x04, 0x08, 0x04 };
        Assert.Equal(nullComplex, theme[0x1A3B9..0x1A3C3]);
        Assert.Equal(snapsSetter, theme[0x1A3C3..0x1A3C7]);
        var paddingShort = new byte[] { 0x06, 0x07, 0x01, 0x00, 0x89, 0x00, 0xDD, 0xFF };
        Assert.Equal(paddingShort, theme[0x1A3C7..0x1A3CF]);
        var paddingValue = new byte[] { 0x24, 0x07, 0x02, 0x00, 0x01, 0x30, 0x71, 0xFD };
        Assert.Equal(paddingValue, theme[0x1A3CF..0x1A3D7]);
        Assert.Equal(0x04, theme[0x1A3D7]);
        Assert.Equal(snapsSetter, theme[0x1A3D8..0x1A3DC]);
        var hcontentShort = new byte[] { 0x06, 0x07, 0x01, 0x00, 0x89, 0x00, 0xDF, 0xFF };
        Assert.Equal(hcontentShort, theme[0x1A3DC..0x1A3E4]);
        var bindingHead = new byte[] { 0x07, 0x02, 0x00, 0x03, 0xEC, 0xFF, 0x00, 0x2A, 0x10, 0x1C, 0x1A };
        Assert.Equal(bindingHead, theme[0x1A3E4..0x1A3EF]);
        Assert.Equal(Encoding.ASCII.GetBytes("HorizontalContentAlignment"), theme[0x1A3EF..0x1A409]);
        var relativeHead = new byte[] { 0x2B, 0x07, 0x0A, 0x00, 0x03, 0xFC, 0xFD, 0x00 };
        Assert.Equal(relativeHead, theme[0x1A409..0x1A411]);
        var ancestorProperty = new byte[] { 0x05, 0x10, 0x52, 0x00, 0x0C };
        Assert.Equal(ancestorProperty, theme[0x1A411..0x1A416]);
        var rsAttrHead = new byte[] { 0x1F, 0x15, 0x0A, 0x00, 0xEC, 0xFF, 0x00, 0x0E };
        Assert.Equal(rsAttrHead, theme[0x163E..0x1646]);
        Assert.Equal(Encoding.ASCII.GetBytes("RelativeSource"), theme[0x1646..0x1654]);
        var snapsAttrHead = new byte[] { 0x1F, 0x1A, 0x20, 0x00, 0x4A, 0xFD, 0x00, 0x13 };
        Assert.Equal(snapsAttrHead, theme[0x1BDB..0x1BE3]);
        Assert.Equal(Encoding.ASCII.GetBytes("SnapsToDevicePixels"), theme[0x1BE3..0x1BF6]);
        Assert.Equal(Encoding.ASCII.GetBytes("ItemsControl"), theme[0x1A416..0x1A422]);
        var bindingClose = new byte[] { 0x04, 0x08, 0x04, 0x08, 0x04 };
        Assert.Equal(bindingClose, theme[0x1A422..0x1A427]);
        Assert.Equal(snapsSetter, theme[0x1A427..0x1A42B]);
        var vcontentShort = new byte[] { 0x06, 0x07, 0x01, 0x00, 0x89, 0x00, 0xDA, 0xFF };
        Assert.Equal(vcontentShort, theme[0x1A42B..0x1A433]);
        var topValue = new byte[] { 0x24, 0x09, 0x02, 0x00, 0x03, 0x54, 0x6F, 0x70, 0x3D, 0xFF };
        Assert.Equal(topValue, theme[0x1A433..0x1A43D]);
        Assert.Equal(0x04, theme[0x1A43D]);
        Assert.Equal(snapsSetter, theme[0x1A43E..0x1A442]);
        var backgroundShort = new byte[] { 0x06, 0x07, 0x01, 0x00, 0x89, 0x00, 0xE8, 0xFF };
        Assert.Equal(backgroundShort, theme[0x1A442..0x1A44A]);
        var transparentBrush = new byte[] { 0x06, 0x0A, 0x02, 0x00, 0xE8, 0x02, 0x01, 0xFF, 0xFF, 0xFF, 0x00 };
        Assert.Equal(transparentBrush, theme[0x1A44A..0x1A455]);
        Assert.Equal(0x04, theme[0x1A455]);
        Assert.Equal(snapsSetter, theme[0x1A456..0x1A45A]);
        var borderBrushShort = new byte[] { 0x06, 0x07, 0x01, 0x00, 0x89, 0x00, 0xE7, 0xFF };
        Assert.Equal(borderBrushShort, theme[0x1A45A..0x1A462]);
        Assert.Equal(transparentBrush, theme[0x1A462..0x1A46D]);
        Assert.Equal(0x04, theme[0x1A46D]);
        Assert.Equal(snapsSetter, theme[0x1A46E..0x1A472]);
        var borderThicknessShort = new byte[] { 0x06, 0x07, 0x01, 0x00, 0x89, 0x00, 0xE6, 0xFF };
        Assert.Equal(borderThicknessShort, theme[0x1A472..0x1A47A]);
        Assert.Equal(paddingValue, theme[0x1A47A..0x1A482]);
        Assert.Equal(0x04, theme[0x1A482]);
        Assert.Equal(snapsSetter, theme[0x1A483..0x1A487]);
        var templateShort = new byte[] { 0x06, 0x07, 0x01, 0x00, 0x89, 0x00, 0xDB, 0xFF };
        Assert.Equal(templateShort, theme[0x1A487..0x1A48F]);
        var templateHead = new byte[] { 0x07, 0x02, 0x00, 0x03, 0x94, 0xFF, 0x00, 0x22, 0x07, 0x00, 0x8A, 0xFE, 0x2E, 0x52, 0xFF, 0x03, 0x9A, 0xFF, 0x00 };
        Assert.Equal(templateHead, theme[0x1A48F..0x1A4A2]);
        var halignBinding = new byte[] { 0x23, 0xD0, 0xFF, 0x7A, 0x02, 0xDF, 0xFF };
        Assert.Equal(halignBinding, theme[0x1A4A2..0x1A4A9]);
        var snapsBinding = new byte[] { 0x23, 0x20, 0x00, 0x7A, 0x02, 0x20, 0x00 };
        Assert.Equal(snapsBinding, theme[0x1A4A9..0x1A4B0]);
        var valignBinding = new byte[] { 0x23, 0xC8, 0xFF, 0x7A, 0x02, 0xDA, 0xFF };
        Assert.Equal(valignBinding, theme[0x1A4B0..0x1A4B7]);
        var templateClose = new byte[] { 0x04, 0x04, 0x08, 0x04, 0x04 };
        Assert.Equal(templateClose, theme[0x1A4B7..0x1A4BC]);
        var nextHead = new byte[] { 0x03, 0x3A, 0x00, 0x00, 0x04 };
        Assert.Equal(nextHead, theme[0x1A4BC..0x1A4C1]);
        var ribbonHead = new byte[] { 0x03, 0x94, 0xFD, 0x00, 0x22, 0x00, 0x00, 0x3D, 0x00, 0x2E, 0x12, 0xFF };
        Assert.Equal(ribbonHead, theme[0x1A8EA..0x1A8F6]);
        Assert.Equal(snapsSetter, theme[0x1A8F6..0x1A8FA]);
        var ribbonFocusable = new byte[] { 0x06, 0x07, 0x01, 0x00, 0x89, 0x00, 0x7C, 0xFF };
        Assert.Equal(ribbonFocusable, theme[0x1A8FA..0x1A902]);
        var ribbonFailSite = new byte[] { 0x06, 0x07, 0x01, 0x00, 0x89, 0x00, 0xAF, 0xFF };
        Assert.Equal(ribbonFailSite, theme[0x1A92D..0x1A935]);
        var ribbonImplicitKey = new byte[] { 0x27, 0x3D, 0x00, 0x00, 0x53, 0xB7, 0x00, 0x00, 0x00, 0x00 };
        Assert.Equal(ribbonImplicitKey, theme[0xB5FF..0xB609]);
        var ribbonTypeHead = new byte[] { 0x1D, 0x25, 0x3D, 0x00, 0x00, 0x00, 0x1F };
        Assert.Equal(ribbonTypeHead, theme[0x2793..0x279A]);
        Assert.Equal(Encoding.ASCII.GetBytes("HandyControl.Controls.RibbonTab"), theme[0x279A..0x27B9]);
        var listBoxKey = new byte[] { 0x26, 0x09, 0x9E, 0x00, 0xCD, 0xB1, 0x00, 0x00, 0x00, 0x00 };
        Assert.Equal(listBoxKey, theme[0xB5C3..0xB5CD]);
        var listBoxKeyString = new byte[] { 0x20, 0x15, 0x9E, 0x00, 0x11 };
        Assert.Equal(listBoxKeyString, theme[0x2613..0x2618]);
        Assert.Equal(Encoding.ASCII.GetBytes("ListBoxItemCustom"), theme[0x2618..0x2629]);
        var growlTypeHead = new byte[] { 0x1D, 0x27, 0x39, 0x00, 0x00, 0x00, 0x21 };
        Assert.Equal(growlTypeHead, theme[0x25EB..0x25F2]);
        Assert.Equal(Encoding.ASCII.GetBytes("HandyControl"), theme[0x25F2..0x25FE]);
    }

    [Fact]
    public void Staged_boot_advances_past_ribbontab_to_template_root_stop()
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
        Assert.DoesNotContain("setter 'Template'", inner.Message);
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
    public void Synthetic_listboxitem_target_lands_live()
    {
        XamlLoader.CustomElementSource = null;
        var dictionary = BamlReader.LoadDictionary(
            ListBoxDictionary(FocusableEntry(-374, "False"), "S"));
        var style = Assert.IsType<Style>(dictionary["S"]);
        Assert.Same(typeof(ListBoxItem), style.TargetType);
        var setter = Assert.Single(style.Setters.OfType<Setter>());
        Assert.Same(UIElement.FocusableProperty, setter.Property);
        Assert.Equal(false, setter.Value);
        Assert.Same(style, dictionary["S"]);
    }

    [Fact]
    public void Synthetic_border_brush_and_thickness_land_on_listboxitem()
    {
        XamlLoader.CustomElementSource = null;
        var dictionary = BamlReader.LoadDictionary(
            ListBoxDictionary(BorderChromeEntry(), "S"));
        var style = Assert.IsType<Style>(dictionary["S"]);
        Assert.Same(typeof(ListBoxItem), style.TargetType);
        var byName = style.Setters.OfType<Setter>().ToDictionary(s => s.Property.Name);
        Assert.Equal(2, byName.Count);
        Assert.Same(Control.BorderBrushProperty, byName["BorderBrush"].Property);
        Assert.Equal(Color.FromArgb(0, 255, 255, 255), Assert.IsType<SolidColorBrush>(byName["BorderBrush"].Value).Color);
        Assert.Same(Control.BorderThicknessProperty, byName["BorderThickness"].Property);
        Assert.Equal(new Thickness(0), Assert.IsType<Thickness>(byName["BorderThickness"].Value));
        var probe = new Style(typeof(ListBoxItem));
        foreach (var setter in style.Setters.OfType<Setter>()) probe.Setters.Add(setter);
        var item = new ListBoxItem();
        probe.ApplyTo(item);
        Assert.Equal(Color.FromArgb(0, 255, 255, 255), Assert.IsType<SolidColorBrush>(item.BorderBrush).Color);
        Assert.Equal(new Thickness(0), item.BorderThickness);
        Assert.Same(style, dictionary["S"]);
    }

    [Fact]
    public void Synthetic_null_focus_visual_style_lands_live()
    {
        XamlLoader.CustomElementSource = null;
        var dictionary = BamlReader.LoadDictionary(
            ListBoxDictionary(NullFocusEntry(), "S"));
        var style = Assert.IsType<Style>(dictionary["S"]);
        Assert.Same(typeof(ListBoxItem), style.TargetType);
        var setter = Assert.Single(style.Setters.OfType<Setter>());
        Assert.Same(FrameworkElement.FocusVisualStyleProperty, setter.Property);
        Assert.Null(setter.Value);
        var probe = new Style(typeof(ListBoxItem));
        probe.Setters.Add(setter);
        var item = new ListBoxItem();
        probe.ApplyTo(item);
        Assert.Null(item.FocusVisualStyle);
        Assert.Same(style, dictionary["S"]);
    }

    [Fact]
    public void Synthetic_garbage_short_member_stays_precise()
    {
        XamlLoader.CustomElementSource = null;
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(
            ListBoxDictionary(PaddingEntry(-50, "0", memberId: -777), "S")));
        Assert.Contains("well-known member id -777", error.Message);
        Assert.Contains("short-form properties beyond FlowDirection/Template/", error.Message);
    }

    [Fact]
    public void Synthetic_border_brush_wrong_target_stays_precise()
    {
        XamlLoader.CustomElementSource = null;
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(
            ListBoxDictionary(BorderBrushEntry(-638), "S")));
        Assert.Contains("'BorderBrush' is not a dependency property of TextBlock (setter in style entry 'S')", error.Message);
    }

    [Fact]
    public void Synthetic_setter_relativesource_ancestortype_lands_live()
    {
        XamlLoader.CustomElementSource = null;
        var dictionary = BamlReader.LoadDictionary(
            ListBoxDictionary(HContentBindingEntry("ItemsControl"), "S"));
        var style = Assert.IsType<Style>(dictionary["S"]);
        Assert.Same(typeof(ListBoxItem), style.TargetType);
        var setter = Assert.Single(style.Setters.OfType<Setter>());
        Assert.Same(Control.HorizontalContentAlignmentProperty, setter.Property);
        var binding = Assert.IsType<Binding>(setter.Value);
        Assert.Equal("HorizontalContentAlignment", binding.Path.Path);
        Assert.NotNull(binding.RelativeSource);
        Assert.Equal(RelativeSourceMode.FindAncestor, binding.RelativeSource.Mode);
        Assert.Same(typeof(ItemsControl), binding.RelativeSource.AncestorType);
        Assert.Equal(1, binding.RelativeSource.AncestorLevel);
        var probe = new Style(typeof(ListBoxItem));
        probe.Setters.Add(setter);
        var item = new ListBoxItem();
        probe.ApplyTo(item);
        var live = Assert.IsType<Binding>(BindingOperations.GetBinding(item, Control.HorizontalContentAlignmentProperty));
        Assert.Equal("HorizontalContentAlignment", live.Path.Path);
        Assert.Same(style, dictionary["S"]);
    }

    [Fact]
    public void Synthetic_setter_relativesource_without_ancestortype_lands_live()
    {
        XamlLoader.CustomElementSource = null;
        var dictionary = BamlReader.LoadDictionary(
            ListBoxDictionary(HContentModeOnlyEntry(), "S"));
        var style = Assert.IsType<Style>(dictionary["S"]);
        var setter = Assert.Single(style.Setters.OfType<Setter>());
        var binding = Assert.IsType<Binding>(setter.Value);
        Assert.Equal("HorizontalContentAlignment", binding.Path.Path);
        Assert.NotNull(binding.RelativeSource);
        Assert.Equal(RelativeSourceMode.FindAncestor, binding.RelativeSource.Mode);
        Assert.Null(binding.RelativeSource.AncestorType);
        Assert.Same(style, dictionary["S"]);
    }

    [Fact]
    public void Synthetic_setter_ancestortype_duplicate_fails()
    {
        XamlLoader.CustomElementSource = null;
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(
            ListBoxDictionary(HContentDuplicateAncestorEntry(), "S")));
        Assert.Contains("duplicate AncestorType on the style-setter RelativeSource", error.Message);
    }

    [Fact]
    public void Synthetic_setter_ancestortype_unknown_prefix_fails()
    {
        XamlLoader.CustomElementSource = null;
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(
            ListBoxDictionary(HContentBindingEntry("zz:Border"), "S")));
        Assert.Contains("names unknown xmlns prefix 'zz'", error.Message);
    }

    [Fact]
    public void ListBoxItem_engine_shape_matches_wpf()
    {
        // The new style target routes through the genuine Control lineage:
        // ListBoxItem inherits every landed DP from Control/FrameworkElement.
        Assert.True(typeof(Control).IsAssignableFrom(typeof(ListBoxItem)));
        Assert.Same(typeof(Control), Control.PaddingProperty.OwnerType);
        Assert.Same(typeof(Thickness), Control.PaddingProperty.PropertyType);
        Assert.Equal(new Thickness(0), Control.PaddingProperty.DefaultMetadata?.DefaultValue);
        Assert.Same(Control.PaddingProperty, DependencyProperty.FromName(typeof(ListBoxItem), "Padding"));
        Assert.Same(typeof(Control), Control.BorderBrushProperty.OwnerType);
        Assert.Same(Control.BorderBrushProperty, DependencyProperty.FromName(typeof(ListBoxItem), "BorderBrush"));
        Assert.Same(typeof(Control), Control.BorderThicknessProperty.OwnerType);
        Assert.Same(Control.BorderThicknessProperty, DependencyProperty.FromName(typeof(ListBoxItem), "BorderThickness"));
        Assert.Same(Control.BackgroundProperty, DependencyProperty.FromName(typeof(ListBoxItem), "Background"));
        Assert.Same(Control.HorizontalContentAlignmentProperty, DependencyProperty.FromName(typeof(ListBoxItem), "HorizontalContentAlignment"));
        Assert.Same(Control.VerticalContentAlignmentProperty, DependencyProperty.FromName(typeof(ListBoxItem), "VerticalContentAlignment"));
        Assert.Same(Control.TemplateProperty, DependencyProperty.FromName(typeof(ListBoxItem), "Template"));
        Assert.Same(UIElement.FocusableProperty, DependencyProperty.FromName(typeof(ListBoxItem), "Focusable"));
        Assert.Same(UIElement.SnapsToDevicePixelsProperty, DependencyProperty.FromName(typeof(ListBoxItem), "SnapsToDevicePixels"));
        Assert.Same(FrameworkElement.FocusVisualStyleProperty, DependencyProperty.FromName(typeof(ListBoxItem), "FocusVisualStyle"));
        Assert.Equal(HorizontalAlignment.Left, new ListBoxItem().HorizontalContentAlignment);
        Assert.Equal(VerticalAlignment.Top, new ListBoxItem().VerticalContentAlignment);
        Assert.False(new ListBoxItem().SnapsToDevicePixels);
        Assert.Null(new ListBoxItem().FocusVisualStyle);
        Assert.Equal(new Thickness(0), new ListBoxItem().Padding);
    }

    private static byte[] FocusableEntry(short targetId, string text, short memberId = -132, short converter = -46)
    {
        return new B()
            .Raw(0x03).I16(-620).Raw(0x00)
            .Raw(0x22).I16(0).I16(targetId)
            .Raw(0x2E).I16(-238)
            .Raw(0x03).I16(-556).Raw(0x00)
            .Sized(0x06, q => q.I16(1).U16(137).I16(memberId))
            .Sized(0x24, q => q.I16(2).Str(text).I16(converter))
            .Raw(0x04)
            .Raw(0x04).ToArray();
    }

    private static byte[] PaddingEntry(short targetId, string text, short memberId = -35, short converter = -143)
    {
        return FocusableEntry(targetId, text, memberId, converter);
    }

    private static byte[] BorderBrushEntry(short targetId)
    {
        return new B()
            .Raw(0x03).I16(-620).Raw(0x00)
            .Raw(0x22).I16(0).I16(targetId)
            .Raw(0x2E).I16(-238)
            .Raw(0x03).I16(-556).Raw(0x00)
            .Sized(0x06, q => q.I16(1).U16(137).I16(-25))
            .Sized(0x06, q => q.I16(2).Raw(0xE8, 0x02, 0x01, 0xFF, 0xFF, 0xFF, 0x00))
            .Raw(0x04)
            .Raw(0x04).ToArray();
    }

    private static byte[] BorderChromeEntry()
    {
        return new B()
            .Raw(0x03).I16(-620).Raw(0x00)
            .Raw(0x22).I16(0).I16(-374)
            .Raw(0x2E).I16(-238)
            .Raw(0x03).I16(-556).Raw(0x00)
            .Sized(0x06, q => q.I16(1).U16(137).I16(-25))
            .Sized(0x06, q => q.I16(2).Raw(0xE8, 0x02, 0x01, 0xFF, 0xFF, 0xFF, 0x00))
            .Raw(0x04)
            .Raw(0x03).I16(-556).Raw(0x00)
            .Sized(0x06, q => q.I16(1).U16(137).I16(-26))
            .Sized(0x24, q => q.I16(2).Str("0").I16(-143))
            .Raw(0x04)
            .Raw(0x04).ToArray();
    }

    private static byte[] NullFocusEntry()
    {
        return new B()
            .Raw(0x03).I16(-620).Raw(0x00)
            .Raw(0x22).I16(0).I16(-374)
            .Raw(0x2E).I16(-238)
            .Raw(0x03).I16(-556).Raw(0x00)
            .Sized(0x06, q => q.I16(1).U16(16521).I16(-226).Str("FocusVisualStyle"))
            .Raw(0x07).I16(2)
            .Raw(0x03).I16(-422).Raw(0x00)
            .Raw(0x04)
            .Raw(0x08)
            .Raw(0x04)
            .Raw(0x04).ToArray();
    }

    private static byte[] HContentBindingEntry(string ancestorText)
    {
        return new B()
            .Raw(0x03).I16(-620).Raw(0x00)
            .Raw(0x22).I16(0).I16(-374)
            .Raw(0x2E).I16(-238)
            .Raw(0x03).I16(-556).Raw(0x00)
            .Sized(0x06, q => q.I16(1).U16(137).I16(-33))
            .Raw(0x07).I16(2)
            .Raw(0x03).I16(-20).Raw(0x00)
            .Raw(0x2A).Sized(0x10, q => q.Str("HorizontalContentAlignment")).Raw(0x2B)
            .Raw(0x07).I16(3)
            .Raw(0x03).I16(-516).Raw(0x00)
            .Sized(0x05, q => q.I16(4).Str(ancestorText))
            .Raw(0x04)
            .Raw(0x08)
            .Raw(0x04)
            .Raw(0x08)
            .Raw(0x04).Raw(0x04).ToArray();
    }

    private static byte[] HContentModeOnlyEntry()
    {
        return new B()
            .Raw(0x03).I16(-620).Raw(0x00)
            .Raw(0x22).I16(0).I16(-374)
            .Raw(0x2E).I16(-238)
            .Raw(0x03).I16(-556).Raw(0x00)
            .Sized(0x06, q => q.I16(1).U16(137).I16(-33))
            .Raw(0x07).I16(2)
            .Raw(0x03).I16(-20).Raw(0x00)
            .Raw(0x2A).Sized(0x10, q => q.Str("HorizontalContentAlignment")).Raw(0x2B)
            .Raw(0x07).I16(3)
            .Raw(0x03).I16(-516).Raw(0x00)
            .Raw(0x2A).Sized(0x10, q => q.Str("FindAncestor")).Raw(0x2B)
            .Raw(0x04)
            .Raw(0x08)
            .Raw(0x04)
            .Raw(0x08)
            .Raw(0x04).Raw(0x04).ToArray();
    }

    private static byte[] HContentDuplicateAncestorEntry()
    {
        return new B()
            .Raw(0x03).I16(-620).Raw(0x00)
            .Raw(0x22).I16(0).I16(-374)
            .Raw(0x2E).I16(-238)
            .Raw(0x03).I16(-556).Raw(0x00)
            .Sized(0x06, q => q.I16(1).U16(137).I16(-33))
            .Raw(0x07).I16(2)
            .Raw(0x03).I16(-20).Raw(0x00)
            .Raw(0x2A).Sized(0x10, q => q.Str("HorizontalContentAlignment")).Raw(0x2B)
            .Raw(0x07).I16(3)
            .Raw(0x03).I16(-516).Raw(0x00)
            .Sized(0x05, q => q.I16(4).Str("ItemsControl"))
            .Sized(0x05, q => q.I16(4).Str("ItemsControl"))
            .Raw(0x04)
            .Raw(0x08)
            .Raw(0x04)
            .Raw(0x08)
            .Raw(0x04).Raw(0x04).ToArray();
    }

    private static byte[] ListBoxDictionary(byte[] entry, string key)
    {
        var head = new B().Raw(Header()).Raw(0x01)
            .Raw(0x03).I16(-524).Raw(0x00)
            .Sized(0x1C, p => p.U16(0).Str("Engine, Version=1.0.0.0"))
            .Sized(0x1F, p => p.I16(0).I16(-620).Raw(0x00).Str("TargetType"))
            .Sized(0x1F, p => p.I16(1).I16(-556).Raw(0x00).Str("Property"))
            .Sized(0x1F, p => p.I16(2).I16(-556).Raw(0x00).Str("Value"))
            .Sized(0x1F, p => p.I16(7).I16(-108).Raw(0x00).Str("TargetType"))
            .Sized(0x1F, p => p.I16(3).I16(-20).Raw(0x00).Str("RelativeSource"))
            .Sized(0x1F, p => p.I16(4).I16(-516).Raw(0x00).Str("AncestorType"));
        for (ushort i = 5; i < 82; i++)
        {
            if (i == 7) continue;
            var j = i;
            head.Sized(0x1F, p => p.I16((short)j).I16(-556).Raw(0x00).Str("Dummy" + j));
        }
        head.Sized(0x1F, p => p.I16(82).I16(-516).Raw(0x00).Str("AncestorType"));
        return head.Sized(0x20, p => p.U16(0).Str(key))
            .Deferable()
            .Sized(0x26, p => p.U16(0).U32(0).U16(0))
            .Raw(entry).FinishDictionary().ToArray();
    }

    private static byte[] Header() =>
        new B()
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
                File.ReadAllText(System.IO.Path.Combine(apps, "app-manifest.json")));
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
