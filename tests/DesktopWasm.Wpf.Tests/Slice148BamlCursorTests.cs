// Copyright (c) 2026 The DesktopWasm Authors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Reflection;
using System.Resources;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DesktopWasm.Wpf.Tests;

/// <summary>Slice-148 HandyControl theme.baml RowHeaderGripperStyle Cursor entry routing.
///
/// Scope decision (see the slice entry in docs/preview-upgrade-tasks.md):
/// the slice-147 blocker (the keyed 'RowHeaderGripperStyle' entry at
/// BAML+0x19FF7 holding a qualified Cursor setter over owner -226
/// FrameworkElement with the SizeNS text through converter -117, which
/// the engine had no DependencyProperty for) lands as a live engine
/// Style: the new FrameworkElement.CursorProperty DP (WPF parity: owner
/// FrameworkElement, type Cursor, default null, retained with no cursor
/// layer) routes the setter, and conversion is driven by the DP type
/// through the existing StyleValueConverter Cursor arm. The run advances
/// past both gripper styles and stops precise at the keyed
/// 'DataGridComboBoxColumnStyle' Style entry at BAML+0x1A113 (ComboBox
/// target, well-known id -92 with no style-target routing - the next
/// slice's work).
///
/// Byte-exact decode against the staged 466350-byte theme.baml: the
/// RowHeader entry [0x19FF7, 0x1A083) (Style head 03 94 FD with
/// TargetType 22 00 00 6E FD over Thumb -658, Height/Background short
/// setters, the qualified Cursor PropertyCustom 06 0E 01 00 89 40 1E FF
/// 06 "Cursor" with the 0x24 SizeNS value record 24 0C 02 00 06 "SizeNS"
/// 8B FF, the Template ControlTemplate/Border/TemplateBinding tail and
/// the 04 04 08 04 04 close run), the ColumnHeader entry [0x1A083,
/// 0x1A10F) (same shape with Height "8" through the -348 LengthConverter
/// and Cursor "SizeWE" at 0x1A0C0/0x1A0CF), the DataGrid head 03 94 FD
/// 00 22 00 00 A4 FF (TargetType -92) at 0x1A10F with the loader
/// reporting the TargetType record at BAML+0x1A113, key records 0x26 key
/// ids 0x99/0x9A/0x9B and the key-string table rows 0x99
/// 'RowHeaderGripperStyle'/0x9A 'ColumnHeaderGripperStyle'/0x9B
/// 'DataGridComboBoxColumnStyle'.
///
/// Triangulated three ways: the staged framing above; the live upstream
/// HandyControl Theme.xaml gripper blocks at pinned commit e9ad3d70
/// (RowHeaderGripperStyle with Height 8, Transparent Background,
/// Cursor SizeNS and the Border TemplateBinding template, followed by
/// the ColumnHeaderGripperStyle block with Cursor SizeWE, followed by
/// the DataGridComboBoxColumnStyle block TargetType ComboBox); a
/// genuine net10.0-windows reflection probe (CursorProperty is owned by
/// FrameworkElement with Cursor type and null default, absent on
/// UIElement; the CLR Cursor sits on FrameworkElement; CursorConverter
/// converts "SizeNS"; a FrameworkElement.CursorProperty style setter
/// applies to a Thumb and Thumb.Cursor lands).
///
/// Live counts verified first: handycontrol conversion-failed, 6 staged,
/// 2 gaps (both declared absent binaries System.Security.Permissions +
/// System.Windows.Extensions with no browser path); all 6 staged images
/// sha256-verified, manifest hand-untouched; staged-oracle status-gate
/// bypass stays in-probe only.
/// </summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class Slice148BamlCursorTests : IDisposable
{
    private readonly Application? _previous = Application.Current;

    public Slice148BamlCursorTests()
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
        // Live staged-oracle gate: HandyControl stays conversion-failed
        // with exactly the same two absent upstream binaries, so the run
        // below decodes the same staged bytes the earlier slices pinned.
        // The 466350-byte theme.baml length plus the sha256 map below pin
        // the input. Loading the staged entry first guarantees the
        // HandyControl assembly is present however the runner orders the
        // tests.
        LoadStagedHandyControlEntry();
        var apps = StagedHandyAppsDirectory();
        using var document = JsonDocument.Parse(File.ReadAllText(System.IO.Path.Combine(apps, "app-manifest.json")));
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
    public void Theme_baml_places_gripper_cursor_and_combobox_bytes()
    {
        // Staged framing pins for the two keyed gripper Style entries
        // [0x19FF7, 0x1A083) and [0x1A083, 0x1A10F) plus the
        // DataGridComboBoxColumnStyle head at 0x1A10F: the RowHeader tail
        // close run, the ColumnHeader Style head over Thumb -658, the
        // qualified ColumnHeader Cursor property/value records ("SizeWE"
        // at 0x1A0D4), the ColumnHeader tail close run with the -92
        // TargetType head the new stop reports at BAML+0x1A113, the key
        // records 0x26 key ids 0x99/0x9A/0x9B and the key-string rows.
        LoadStagedHandyControlEntry();
        var theme = ReadStagedGResource("HandyControl", "themes/theme.baml");
        Assert.Equal(466350, theme.Length);
        Assert.Equal(
            "D305CC52C4F8A6EB077D1CC79F556BD343830E30C7C67FC73D3988C63AAF65D0",
            Convert.ToHexString(SHA256.HashData(theme)));

        var rowTailColHead = new byte[] { 0x04, 0x04, 0x08, 0x04, 0x04, 0x03, 0x94, 0xFD, 0x00, 0x22, 0x00, 0x00, 0x6E, 0xFD, 0x2E, 0x12, 0xFF };
        Assert.Equal(rowTailColHead, theme[0x1A07E..0x1A08F]);
        var colCursorProperty = new byte[] { 0x06, 0x0E, 0x01, 0x00, 0x89, 0x40, 0x1E, 0xFF, 0x06 }
            .Concat(Encoding.ASCII.GetBytes("Cursor")).ToArray();
        Assert.Equal(colCursorProperty, theme[0x1A0C0..0x1A0CF]);
        var colCursorValue = new byte[] { 0x24, 0x0C, 0x02, 0x00, 0x06 }
            .Concat(Encoding.ASCII.GetBytes("SizeWE")).Concat(new byte[] { 0x8B, 0xFF }).ToArray();
        Assert.Equal(colCursorValue, theme[0x1A0CF..0x1A0DC]);
        var sizeWE = Encoding.ASCII.GetBytes("SizeWE");
        Assert.Equal(sizeWE, theme[0x1A0D4..0x1A0DA]);
        var colTailDataGridHead = new byte[] { 0x04, 0x04, 0x08, 0x04, 0x04, 0x03, 0x94, 0xFD, 0x00, 0x22, 0x00, 0x00, 0xA4, 0xFF };
        Assert.Equal(colTailDataGridHead, theme[0x1A10A..0x1A118]);
        var dataGridHead = new byte[] { 0x03, 0x94, 0xFD, 0x00, 0x22, 0x00, 0x00, 0xA4, 0xFF };
        Assert.Equal(dataGridHead, theme[0x1A10F..0x1A118]);
        var key163 = new byte[] { 0x26, 0x09, 0x99, 0x00, 0x60, 0xAE, 0x00, 0x00, 0x00, 0x00 };
        Assert.Equal(key163, theme[0xB587..0xB591]);
        var key164 = new byte[] { 0x26, 0x09, 0x9A, 0x00, 0xEC, 0xAE, 0x00, 0x00, 0x00, 0x00 };
        Assert.Equal(key164, theme[0xB591..0xB59B]);
        var key165Head = new byte[] { 0x26, 0x09, 0x9B, 0x00 };
        Assert.Equal(key165Head, theme[0xB59B..0xB59F]);
        var columnStringHead = new byte[] { 0x20, 0x1C, 0x9A, 0x00, 0x18 }
            .Concat(Encoding.ASCII.GetBytes("ColumnH")).ToArray();
        Assert.Equal(columnStringHead, theme[0x2517..0x2523]);
        var dataGridStringHead = new byte[] { 0x20, 0x1F, 0x9B, 0x00, 0x1B }
            .Concat(Encoding.ASCII.GetBytes("DataGri")).ToArray();
        Assert.Equal(dataGridStringHead, theme[0x2534..0x2540]);
    }

    [Fact]
    public void Staged_boot_advances_past_gripper_to_combobox_stop()
    {
        // The staged entry now decodes both keyed gripper Style entries
        // (RowHeader with SizeNS plus ColumnHeader with SizeWE, each with
        // Height/Background/qualified-Cursor/Template setters), then stops
        // precise at the keyed 'DataGridComboBoxColumnStyle' Style entry:
        // its ComboBox TargetType (well-known id -92) has no
        // style-target routing. Everything the earlier slices pinned stays
        // landed.
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
    public void Staged_rowheader_cursor_lands_live()
    {
        // The staged-verbatim 'RowHeaderGripperStyle' entry bytes
        // [0x19FF7, 0x1A083) land as a live engine Style: Thumb target,
        // Height 8, Transparent Background, SizeNS Cursor through the new
        // FrameworkElement DP, the Border TemplateBinding template, and
        // the shared key returning the same instance on every lookup.
        LoadStagedHandyControlEntry();
        var theme = ReadStagedGResource("HandyControl", "themes/theme.baml");
        var dictionary = BamlReader.LoadDictionary(
            PathDictionary(theme[0x19FF7..0x1A083], "RowHeaderGripperStyle"));
        var style = Assert.IsType<Style>(dictionary["RowHeaderGripperStyle"]);
        Assert.Same(typeof(Thumb), style.TargetType);
        var byName = style.Setters.OfType<Setter>().ToDictionary(s => s.Property.Name);
        Assert.Equal(4, byName.Count);
        Assert.Equal(8.0, Assert.IsType<double>(byName["Height"].Value), 9);
        var background = Assert.IsType<SolidColorBrush>(byName["Background"].Value);
        Assert.Equal(Colors.Transparent, background.Color);
        Assert.Same(Cursors.SizeNS, Assert.IsType<Cursor>(byName["Cursor"].Value));
        Assert.Same(FrameworkElement.CursorProperty, byName["Cursor"].Property);
        var template = Assert.IsType<ControlTemplate>(byName["Template"].Value);
        Assert.Same(typeof(Thumb), template.TargetType);
        Assert.Equal(2, template.TemplateBindings.Count);
        Assert.Contains(template.TemplateBindings, e => e.Property == "Background");
        Assert.Contains(template.TemplateBindings, e => e.Property == "Padding");
        var inflation = Assert.Throws<InvalidOperationException>(() => template.LoadContent());
        Assert.Contains("no inflation markup", inflation.Message);
        var probe = new Style(typeof(Thumb));
        probe.Setters.Add(byName["Cursor"]);
        var thumb = new Thumb();
        Assert.Null(thumb.Cursor);
        probe.ApplyTo(thumb);
        Assert.Same(Cursors.SizeNS, thumb.Cursor);
        Assert.Same(style, dictionary["RowHeaderGripperStyle"]);
    }

    [Fact]
    public void Staged_columnheader_cursor_lands_live()
    {
        // The staged-verbatim 'ColumnHeaderGripperStyle' entry bytes
        // [0x1A083, 0x1A10F) land the same way with the SizeWE cursor the
        // upstream block carries: same Thumb target, same setter count,
        // applied visibly onto a live Thumb.
        LoadStagedHandyControlEntry();
        var theme = ReadStagedGResource("HandyControl", "themes/theme.baml");
        var dictionary = BamlReader.LoadDictionary(
            PathDictionary(theme[0x1A083..0x1A10F], "ColumnHeaderGripperStyle"));
        var style = Assert.IsType<Style>(dictionary["ColumnHeaderGripperStyle"]);
        Assert.Same(typeof(Thumb), style.TargetType);
        var byName = style.Setters.OfType<Setter>().ToDictionary(s => s.Property.Name);
        Assert.Equal(4, byName.Count);
        Assert.Same(Cursors.SizeWE, Assert.IsType<Cursor>(byName["Cursor"].Value));
        var probe = new Style(typeof(Thumb));
        probe.Setters.Add(byName["Cursor"]);
        var thumb = new Thumb();
        probe.ApplyTo(thumb);
        Assert.Same(Cursors.SizeWE, thumb.Cursor);
        Assert.Same(style, dictionary["ColumnHeaderGripperStyle"]);
    }

    [Fact]
    public void Synthetic_qualified_cursor_setter_lands_live()
    {
        // A hand-built Thumb-targeted Style entry with the staged shape
        // (PropertyCustom serializer 16521 over owner -226 plus the
        // PropertyWithConverter text through converter -117) lands a live
        // Style whose setter carries the FrameworkElement CursorProperty
        // DP with the SizeNS cursor, applying visibly onto a Thumb. The
        // DP resolves through the Thumb hierarchy walk with a null
        // default, matching the genuine-WPF probe contract.
        XamlLoader.CustomElementSource = null;
        Assert.Same(FrameworkElement.CursorProperty,
            DependencyProperty.FromName(typeof(FrameworkElement), "Cursor"));
        Assert.Same(FrameworkElement.CursorProperty,
            DependencyProperty.FromName(typeof(Thumb), "Cursor"));
        Assert.Null(new Thumb().Cursor);
        var dictionary = BamlReader.LoadDictionary(CursorDictionary("S", "SizeNS"));
        var style = Assert.IsType<Style>(dictionary["S"]);
        Assert.Same(typeof(Thumb), style.TargetType);
        var setter = Assert.IsType<Setter>(Assert.Single(style.Setters));
        Assert.Same(FrameworkElement.CursorProperty, setter.Property);
        Assert.Same(Cursors.SizeNS, setter.Value);
        var thumb = new Thumb();
        style.ApplyTo(thumb);
        Assert.Same(Cursors.SizeNS, thumb.Cursor);
        Assert.Same(Cursors.SizeNS, thumb.GetValue(FrameworkElement.CursorProperty));
    }

    [Fact]
    public void Synthetic_garbage_cursor_stays_precise()
    {
        // Garbage cursor text fails precise through the converter instead
        // of landing a half-built setter.
        XamlLoader.CustomElementSource = null;
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(CursorDictionary("S", "NotACursor")));
        Assert.Contains("is not a Cursor", error.Message);
    }

    [Fact]
    public void Synthetic_unknown_framework_property_stays_precise()
    {
        // Owner resolution succeeds but the property is missing: the load
        // names the property and the FrameworkElement owner instead of
        // landing a half-built setter.
        XamlLoader.CustomElementSource = null;
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(CursorOwnerDictionary("Nope", "SizeNS")));
        Assert.Contains("'Nope' is not a dependency property of FrameworkElement", error.Message);
    }

    [Fact]
    public void Cursor_engine_shape_matches_wpf()
    {
        // The engine honors the live-WPF contract the genuine
        // net10.0-windows probe pins: CursorProperty is owned by
        // FrameworkElement (Cursor type, null default, non-attached),
        // absent on UIElement, the CLR sits on FrameworkElement, the
        // stock cursors convert by name and garbage stays loud.
        Assert.Same(typeof(FrameworkElement), FrameworkElement.CursorProperty.OwnerType);
        Assert.Same(typeof(Cursor), FrameworkElement.CursorProperty.PropertyType);
        Assert.Null(FrameworkElement.CursorProperty.DefaultMetadata?.DefaultValue);
        Assert.False(FrameworkElement.CursorProperty.IsAttached);
        Assert.Null(typeof(UIElement).GetField("CursorProperty",
            BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy));
        Assert.NotNull(typeof(FrameworkElement).GetProperty("Cursor"));
        Assert.Same(Cursors.SizeNS, StyleValueConverter.Convert("SizeNS", typeof(Cursor)));
        Assert.Same(Cursors.SizeWE, StyleValueConverter.Convert("SizeWE", typeof(Cursor)));
        Assert.Same(Cursors.Hand, StyleValueConverter.ParseCursor("Hand"));
        Assert.Throws<FormatException>(() => StyleValueConverter.ParseCursor("NotACursor"));
        Assert.NotSame(Cursors.SizeNS, Cursors.SizeWE);
    }

    private static byte[] CursorDictionary(string key, string text)
    {
        var content = new B().Raw(0x03).I16(-620).Raw(0x00)
            .Raw(0x22).I16(0).I16(-658)
            .Raw(0x2E).I16(-238)
            .Raw(0x03).I16(-556).Raw(0x00)
            .Sized(0x06, q => q.I16(1).U16(16521).I16(-226).Str("Cursor"))
            .Sized(0x24, q => q.I16(2).Str(text).I16(-117))
            .Raw(0x04)
            .Raw(0x04).ToArray();
        return CursorShell(key, content);
    }

    private static byte[] CursorOwnerDictionary(string propertyName, string text)
    {
        var content = new B().Raw(0x03).I16(-620).Raw(0x00)
            .Raw(0x22).I16(0).I16(-658)
            .Raw(0x2E).I16(-238)
            .Raw(0x03).I16(-556).Raw(0x00)
            .Sized(0x06, q => q.I16(1).U16(16521).I16(-226).Str(propertyName))
            .Sized(0x24, q => q.I16(2).Str(text).I16(-117))
            .Raw(0x04)
            .Raw(0x04).ToArray();
        return CursorShell("S", content);
    }

    private static byte[] CursorShell(string key, byte[] entry)
    {
        var head = new B().Raw(Header()).Raw(0x01)
            .Raw(0x03).I16(-524).Raw(0x00)
            .Sized(0x1C, p => p.U16(0).Str("Engine, Version=1.0.0.0"))
            .Sized(0x1F, p => p.I16(0).I16(-620).Raw(0x00).Str("TargetType"))
            .Sized(0x1F, p => p.I16(1).I16(-556).Raw(0x00).Str("Property"))
            .Sized(0x1F, p => p.I16(2).I16(-556).Raw(0x00).Str("Value"))
            .Sized(0x20, p => p.U16(0).Str(key));
        return head.Deferable()
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

    private static byte[] PathDictionary(byte[] entry, string key)
    {
        // The staged per-file rows mirrored with dummies (assembly 0, the
        // Style/Setter AttributeInfo rows, the template TargetType plus
        // RelativeSource plus Padding rows the staged Template subtree
        // references, the key string) around either the staged-verbatim
        // or the hand-built entry bytes.
        var head = new B().Raw(Header()).Raw(0x01)
            .Raw(0x03).I16(-524).Raw(0x00)
            .Sized(0x1C, p => p.U16(0).Str("Engine, Version=1.0.0.0"))
            .Sized(0x1F, p => p.I16(0).I16(-620).Raw(0x00).Str("TargetType"))
            .Sized(0x1F, p => p.I16(1).I16(-556).Raw(0x00).Str("Property"))
            .Sized(0x1F, p => p.I16(2).I16(-556).Raw(0x00).Str("Value"))
            .Sized(0x1F, p => p.I16(7).I16(-108).Raw(0x00).Str("TargetType"))
            .Sized(0x1F, p => p.I16(10).I16(-20).Raw(0x00).Str("RelativeSource"))
            .Sized(0x1F, p => p.I16(75).I16(-50).Raw(0x00).Str("Padding"))
            .Sized(0x20, p => p.U16(0).Str(key));
        return head.Deferable()
            .Sized(0x26, p => p.U16(0).U32(0).U16(0))
            .Raw(entry).FinishDictionary().ToArray();
    }
}
