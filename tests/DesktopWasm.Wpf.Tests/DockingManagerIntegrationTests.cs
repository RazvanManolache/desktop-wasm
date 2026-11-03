using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DesktopWasm.Graphics;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// RVW0900 runtime: real AvalonDock DockingManager integration boundary.
/// The converted joint-package type (assembly "AvalonDock", full name
/// "AvalonDock.DockingManager") resolves exactly and constructs as a real
/// engine UIElement; the BAML reader hosts it instead of stopping at the
/// generic v1 custom-type wall. No substitute panel, no swallowed failure.
/// </summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class DockingManagerIntegrationTests
{
    private const short TGrid = -254;
    private const short MContent = -14;
    private const short MChildren = -213;
    private const short CBool = -46;

    private sealed class B
    {
        private readonly List<byte> _b = new();
        public B Raw(params byte[] b) { _b.AddRange(b); return this; }
        public B U16(ushort v) { _b.AddRange(BitConverter.GetBytes(v)); return this; }
        public B I16(short v) { _b.AddRange(BitConverter.GetBytes(v)); return this; }
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
        public byte[] ToArray() => _b.ToArray();
    }

    private static byte[] Header() => new B()
        .Raw(0x0C, 0x00, 0x00, 0x00)
        .Raw(0x4D, 0x00, 0x53, 0x00, 0x42, 0x00, 0x41, 0x00, 0x4D, 0x00, 0x4C, 0x00)
        .U16(0).U16(0x60)
        .Raw(0x00, 0x00, 0x60, 0x00, 0x00, 0x00, 0x60, 0x00,
             0x01, 0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0x00)
        .ToArray();

    private static Window Load(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        return XamlLoader.LoadComponent<Window>(stream);
    }

    [Fact]
    public void Reader_applies_plain_clr_property_on_hosted_dockingmanager()
    {
        // Failing-first anchor for the real 0x1329 wall: IsVirtualizingAnchorable
        // is a settable bool CLR property on the converted type, not a DP.
        LoadJointPackage();
        var window = Load(DockingManagerWithAttribute("IsVirtualizingAnchorable", "False", CBool));
        var grid = Assert.IsType<Grid>(window.Content);
        var dock = Assert.Single(grid.LogicalChildren);
        Assert.Equal("AvalonDock.DockingManager", dock.GetType().FullName);
        var clr = dock.GetType().GetProperty("IsVirtualizingAnchorable", BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(clr);
        Assert.Equal(typeof(bool), clr!.PropertyType);
        Assert.True(clr.CanWrite);
        Assert.Equal(false, clr.GetValue(dock));
    }

    [Fact]
    public void Converted_dockingmanager_map_is_exact()
    {
        LoadJointPackage();
        var dm = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(SafeTypes)
            .FirstOrDefault(t => t.Name == "DockingManager");
        Assert.NotNull(dm);
        Assert.Equal("AvalonDock.DockingManager", dm!.FullName);
        Assert.Equal("AvalonDock", dm.Assembly.GetName().Name);
        Assert.False(dm.IsAbstract);
        Assert.NotNull(dm.GetConstructor(Type.EmptyTypes));
        Assert.True(typeof(UIElement).IsAssignableFrom(dm));
        var chain = new List<string>();
        for (var t = dm; t is not null; t = t.BaseType) chain.Add(t.FullName ?? t.Name);
        Assert.Equal(new[]
        {
            "AvalonDock.DockingManager",
            "DesktopWasm.Wpf.Control",
            "DesktopWasm.Wpf.FrameworkElement",
            "DesktopWasm.Wpf.UIElement",
            "DesktopWasm.Wpf.Visual",
            "DesktopWasm.Wpf.DependencyObject",
            "System.Object",
        }, chain);
        var dps = dm.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(f => f.FieldType == typeof(DependencyProperty))
            .Select(f => f.Name)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var required in new[]
        {
            "LayoutProperty", "ThemeProperty", "DocumentsSourceProperty",
            "AnchorablesSourceProperty", "ActiveContentProperty",
            "LayoutItemTemplateProperty", "LayoutItemTemplateSelectorProperty",
        })
            Assert.Contains(required, dps);
    }

    [Fact]
    public void Factory_constructs_real_converted_dockingmanager()
    {
        LoadJointPackage();
        var resolved = DockingIntegration.TryResolve("AvalonDock.DockingManager");
        Assert.NotNull(resolved);
        Assert.Equal("AvalonDock.DockingManager", resolved!.FullName);
        Assert.True(DockingIntegration.TryCreate(resolved, out var instance, out var failure));
        Assert.Null(failure);
        Assert.NotNull(instance);
        Assert.IsAssignableFrom<UIElement>(instance);
        Assert.Equal("AvalonDock", instance!.GetType().Assembly.GetName().Name);
    }

    [Fact]
    public void Factory_rejects_unknown_names_without_broad_scan()
    {
        var before = AppDomain.CurrentDomain.GetAssemblies().Length;
        Assert.Null(DockingIntegration.TryResolve("AvalonDock.FakePanel"));
        Assert.Null(DockingIntegration.TryResolve("System.Windows.Controls.Grid"));
        Assert.Null(DockingIntegration.TryResolve("AvalonDock.LayoutRootPanel"));
        Assert.Equal(before, AppDomain.CurrentDomain.GetAssemblies().Length);
        Assert.False(DockingIntegration.IsDockingManagerElement("AvalonDock.FakePanel", "AvalonDock"));
        Assert.False(DockingIntegration.IsDockingManagerElement("AvalonDock.DockingManager", "PresentationFramework"));
    }

    [Fact]
    public void Reader_builds_real_dockingmanager_inside_grid()
    {
        LoadJointPackage();
        var b = new B();
        b.Raw(Header()).Raw(0x01);
        b.Sized(0x1C, p => p.U16(0).Str("TestApp"));
        b.Sized(0x1C, p => p.U16(1).Str("AvalonDock"));
        b.Sized(0x1D, p => p.U16(0).U16(0).Str("TestApp.MainWindow"));
        b.Sized(0x1D, p => p.U16(1).U16(1).Str("AvalonDock.DockingManager"));
        b.Raw(0x03).I16(0).Raw(0x00);
        b.Raw(0x2E).I16(MContent);
        b.Raw(0x03).I16(TGrid).Raw(0x00);
        b.Raw(0x2E).I16(MChildren);
        b.Raw(0x03).I16(1).Raw(0x00);
        b.Raw(0x04).Raw(0x04).Raw(0x04).Raw(0x02);

        var window = Load(b.ToArray());
        var grid = Assert.IsType<Grid>(window.Content);
        var dock = Assert.Single(grid.LogicalChildren);
        Assert.Equal("AvalonDock.DockingManager", dock.GetType().FullName);
        Assert.Equal("AvalonDock", dock.GetType().Assembly.GetName().Name);
    }

    [Fact]
    public void Reader_names_datatemplate_engine_gap_precise()
    {
        // Well-known id -120 is the real framework DataTemplate (pinned by
        // the genuine net10.0-windows fixture): since UNSTALL-dw-runtime it
        // constructs as a retained-root engine DataTemplate instead of failing.
        // Attaching it where the converted Theme belongs still fails precise
        // at the converted type-check: no substitute, no swallowed mismatch.
        LoadJointPackage();
        var error = Assert.Throws<XamlLoadException>(() => Load(DockingManagerWithWellknownTemplate()));
        Assert.Contains("complex property 'Theme' on <DockingManager>", error.Message);
        Assert.Contains("(have DesktopWasm.Wpf.DataTemplate)", error.Message);
        Assert.DoesNotContain("unknown well-known BAML type id -120", error.Message);
    }

    [Fact]
    public void Real_mainwindow_boot_advances_past_dockingmanager_wall()
    {
        LoadJointPackage();
        var bytes = LoadRealMainWindowBaml();
        UIElement? root = null;
        XamlLoadException? error = null;
        try
        {
            root = BamlReader.LoadRoot(bytes);
        }
        catch (XamlLoadException ex)
        {
            error = ex;
        }
        Assert.DoesNotContain(
            "cannot instantiate custom CLR type 'AvalonDock.DockingManager' in the browser engine (v1 supports",
            error?.Message ?? string.Empty);
        if (error is null)
        {
            Assert.True(
                Descendants(root!).Any(e => e.GetType().FullName == "AvalonDock.DockingManager"),
                "booted TestApp MainWindow carries no converted DockingManager");
        }
        else
        {
            // Named next dependency (grant-sized follow-up): converted theme
            // values. The hosted DockingManager constructs with every
            // per-file attribute through 0x1392 applied (bool DPs like
            // AllowMixedOrientation plus plain CLR properties like
            // IsVirtualizingAnchorable); the converted theme object now hosts
            // through TryCreateValue/TryApplyConvertedValue (synthetic + real
            // bytes both green). Never a substitute control.
            Assert.DoesNotContain("BAML+0x12D3", error.Message);
            Assert.DoesNotContain("BAML+0x1329", error.Message);
            Assert.DoesNotContain("BAML+0x1392", error.Message);
            Assert.DoesNotContain("cannot instantiate custom CLR type 'AvalonDock.Themes.ArcDarkTheme'", error.Message);
            // UNSTALL-dw-runtime: the DataTemplate retained root plus the
            // VisualTree/StackPanel/Image/Source-binding content now parse.
            // FACTORY Job 1: the inline Image.Style (-620) with TargetType,
            // Triggers, Source=={x:Null} Trigger and Visibility Setter now lands
            // through the shared keyed-entry machinery, so the boot runs past
            // 0x13B9 and 0x142A through the whole icon row and stops at the
            // next grant-sized wall: the following DockingManager complex
            // property (member 26, converted layout value; Job 2 follow-up).
            Assert.DoesNotContain("BAML+0x13B9", error.Message);
            Assert.DoesNotContain("BAML+0x142A", error.Message);
            Assert.DoesNotContain("unknown well-known BAML type id -620", error.Message);
            Assert.DoesNotContain("BAML+0x14E4", error.Message);
            Assert.DoesNotContain("unknown BAML content member id 26 on <DockingManager>", error.Message);
            Assert.DoesNotContain("BAML+0x1525", error.Message);
            Assert.DoesNotContain("unknown BAML content member id 27 on <LayoutRoot>", error.Message);
            Assert.DoesNotContain("BAML+0x17D6", error.Message);
            Assert.DoesNotContain("BAML record 0x19 (DefAttribute) outside any entry", error.Message);
            // Granted seam FP-RT-NAME-REQ: both x:Name sites (0x17D6
            // WinFormsWindow, 0x1A45 LayoutDocumentPane) land in the
            // document namescope; the boot stops at the next wall below.
            // Granted seam FP-RT-OBJ-REQ: object-typed converted-model
            // text (ToolTip/-615) boxes the converted string, so the boot
            // runs past 0x189E and stops at the next wall below.
            Assert.DoesNotContain("BAML+0x189E", error.Message);
            Assert.DoesNotContain("BAML attribute 'ToolTip' on <LayoutAnchorable> uses converter -615 for Object", error.Message);
            Assert.Contains("BAML+0x18FF", error.Message);
            Assert.Contains("cannot instantiate custom CLR type 'System.Windows.Forms.Integration.WindowsFormsHost' (WindowsFormsHost bridge provider is not registered", error.Message);
        }
    }

    [Fact]
    public void Reader_applies_converted_bool_dp_on_hosted_dockingmanager()
    {
        LoadJointPackage();
        var window = Load(DockingManagerWithAttribute("AllowMixedOrientation", "True", CBool));
        var grid = Assert.IsType<Grid>(window.Content);
        var dock = Assert.Single(grid.LogicalChildren);
        Assert.Equal("AvalonDock.DockingManager", dock.GetType().FullName);
        var dp = DependencyProperty.FromName(dock.GetType(), "AllowMixedOrientation");
        Assert.NotNull(dp);
        Assert.Equal(true, ((DependencyObject)dock).GetValue(dp!));
    }

    [Fact]
    public void Reader_rejects_unknown_dp_on_hosted_dockingmanager()
    {
        LoadJointPackage();
        var error = Assert.Throws<XamlLoadException>(
            () => Load(DockingManagerWithAttribute("NoSuchKnob", "True", CBool)));
        Assert.Contains("BAML attribute 'NoSuchKnob' on <DockingManager>", error.Message);
        Assert.Contains("names no dependency property or settable CLR property", error.Message);
    }

    [Fact]
    public void Factory_resolves_and_constructs_converted_theme_value()
    {
        LoadJointPackage();
        Assert.True(DockingIntegration.TryCreateValue(
            "AvalonDock.Themes.ArcDarkTheme",
            "AvalonDock.Themes.Arc, Version=5.0.2.0, Culture=neutral, PublicKeyToken=null",
            out var value, out var failure));
        Assert.Null(failure);
        Assert.NotNull(value);
        Assert.Equal("AvalonDock.Themes.ArcDarkTheme", value!.GetType().FullName);
        Assert.Equal("AvalonDock.Themes.Arc", value.GetType().Assembly.GetName().Name);
        Assert.False(value is UIElement);
    }

    [Fact]
    public void Factory_rejects_unknown_theme_names_precise()
    {
        LoadJointPackage();
        Assert.False(DockingIntegration.TryCreateValue(
            "AvalonDock.Themes.NoSuchTheme", "AvalonDock",
            out var value, out var failure));
        Assert.Null(value);
        Assert.NotNull(failure);
        Assert.Contains("AvalonDock.Themes.NoSuchTheme", failure);
        Assert.Contains("no loadable converted type", failure);
        Assert.False(DockingIntegration.TryCreateValue(
            "AvalonDock.DockingManager", "PresentationFramework",
            out _, out var other));
        Assert.Null(other);
    }

    [Fact]
    public void Reader_applies_converted_theme_value_through_complex_property()
    {
        LoadJointPackage();
        var window = Load(DockingManagerWithTheme());
        var grid = Assert.IsType<Grid>(window.Content);
        var dock = Assert.Single(grid.LogicalChildren);
        Assert.Equal("AvalonDock.DockingManager", dock.GetType().FullName);
        var themeDp = DependencyProperty.FromName(dock.GetType(), "Theme");
        Assert.NotNull(themeDp);
        var themeValue = ((DependencyObject)dock).GetValue(themeDp!);
        Assert.NotNull(themeValue);
        Assert.Equal("AvalonDock.Themes.ArcDarkTheme", themeValue!.GetType().FullName);
        Assert.Equal("AvalonDock.Themes.Arc", themeValue.GetType().Assembly.GetName().Name);
    }


    private const short TDataTemplate = -120;
    private const short TStackPanel = -601;
    private const short TImage = -281;
    private const short TTextBlock = -638;
    private const short TBinding = -20;
    private const short MVisualTree = -174;
    private const short MSource = -75;
    private const short MText = -110;
    private const short MWidth = -57;
    private const short CLen = -348;
    private const short CInt = -315;
    private const short CThick = -655;
    private const short CStr = -615;
    private const short CEnum = -195;
    private const short CFw = -222;
    private const short TStyle = -620;
    private const short TTrigger = -688;
    private const short TSetter = -556;
    private const short TNull = -422;
    private const short MSetters = -256;
    private const short MStyle = -55;
    private const short MVisibility = -135;
    private const short MName = -54;
    private const short SerDP = 137;
    private const short MThemeAttr = 8;

    [Fact]
    public void DataTemplate_retained_root_lands_with_bindings()
    {
        // End-to-end proof of the UNSTALL-dw-runtime slice through real
        // record shapes (genuine-fixture byte shapes: -120 root, -174
        // VisualTree, -601 StackPanel, -281 Image, -20 Binding): the
        // DocumentHeaderTemplate complex child lands as a retained-root
        // engine DataTemplate with fresh parsed content, orientation,
        // size and store bindings preserved. No Style/Trigger surface
        // here; that is the named next wall.
        LoadJointPackage();
        var window = Load(DockingManagerWithTemplateContent());
        var grid = Assert.IsType<Grid>(window.Content);
        var dock = Assert.Single(grid.LogicalChildren);
        Assert.Equal("AvalonDock.DockingManager", dock.GetType().FullName);
        var dp = DependencyProperty.FromName(dock.GetType(), "DocumentHeaderTemplate");
        Assert.NotNull(dp);
        var template = Assert.IsType<DataTemplate>(dock.GetValue(dp));
        var panel = Assert.IsType<StackPanel>(template.VisualRoot);
        Assert.Equal(Orientation.Horizontal, panel.Orientation);
        Assert.Equal(2, panel.Children.Count);
        var image = Assert.IsType<Image>(panel.Children[0]);
        Assert.Equal(16, image.Width);
        var sourceBinding = Assert.IsType<Binding>(
            BindingOperations.GetBindingBase(image, Image.SourceProperty));
        Assert.Equal("IconSource", sourceBinding.Path.Path);
        var title = Assert.IsType<TextBlock>(panel.Children[1]);
        Assert.Equal("Header", title.Text);
    }

    [Fact]
    public void DataTemplate_retained_root_loadcontent_is_precise()
    {
        // The BAML-retained root is never shared across parents: markup-less
        // inflation throws the precise BAML-tree-inflation boundary (like the
        // BAML-retained ControlTemplate/ItemsPanelTemplate roots), and the
        // markup path keeps inflating fresh visuals (covered by the template
        // inflation suite).
        var root = new StackPanel();
        var template = new DataTemplate();
        Assert.Null(template.VisualRoot);
        template.SetVisualRoot(root);
        Assert.Same(root, template.VisualRoot);
        var error = Assert.Throws<InvalidOperationException>(() => template.LoadContent());
        Assert.Contains("only BAML-parsed templates inflate", error.Message);
        Assert.Throws<InvalidOperationException>(() => template.SetVisualRoot(new StackPanel()));
    }

    private static byte[] DockingManagerWithTemplateContent()
    {
        var b = new B();
        b.Raw(Header()).Raw(0x01);
        b.Sized(0x1C, p => p.U16(0).Str("TestApp"));
        b.Sized(0x1C, p => p.U16(1).Str("AvalonDock"));
        b.Sized(0x1D, p => p.U16(0).U16(0).Str("TestApp.MainWindow"));
        b.Sized(0x1D, p => p.U16(1).U16(1).Str("AvalonDock.DockingManager"));
        b.Sized(0x1F, p => p.U16(0).I16(1).Raw(0x00).Str("DocumentHeaderTemplate"));
        b.Sized(0x1F, p => p.U16(1).I16(TStackPanel).Raw(0x00).Str("Orientation"));
        b.Raw(0x03).I16(0).Raw(0x00);
        b.Raw(0x2E).I16(MContent);
        b.Raw(0x03).I16(TGrid).Raw(0x00);
        b.Raw(0x2E).I16(MChildren);
        b.Raw(0x03).I16(1).Raw(0x00);
        b.Raw(0x07).I16(0);
        b.Raw(0x03).I16(TDataTemplate).Raw(0x00);
        b.Raw(0x2E).I16(MVisualTree);
        b.Raw(0x03).I16(TStackPanel).Raw(0x00);
        b.Sized(0x24, p => p.I16(1).Str("Horizontal").I16(CEnum));
        b.Raw(0x2E).I16(MChildren);
        b.Raw(0x03).I16(TImage).Raw(0x00);
        b.Sized(0x24, p => p.I16(MWidth).Str("16").I16(CLen));
        b.Sized(0x24, p => p.I16(MName).Str("Icon").I16(CStr));
        b.Raw(0x07).I16(MSource);
        b.Raw(0x03).I16(TBinding).Raw(0x00);
        b.Raw(0x2A);
        b.Sized(0x10, p => p.Str("IconSource"));
        b.Raw(0x2B);
        b.Raw(0x04);
        b.Raw(0x08);
        b.Raw(0x04);
        b.Raw(0x03).I16(TTextBlock).Raw(0x00);
        b.Sized(0x24, p => p.I16(MText).Str("Header").I16(CStr));
        b.Raw(0x04);
        b.Raw(0x04);
        b.Raw(0x04);
        b.Raw(0x08);
        b.Raw(0x04).Raw(0x04).Raw(0x04).Raw(0x02);
        return b.ToArray();
    }

    [Fact]
    public void Inline_image_style_lands_trigger_with_null_value()
    {
        // Focused regression for the 0x142A wall: the inline Image.Style
        // (Style/TargetType/Triggers/Trigger/{x:Null}/Setter) parses through
        // the shared keyed-entry machinery with genuine record shapes and
        // lands on the image with trigger semantics intact (Source==null
        // hides the icon through the existing evaluation path).
        LoadJointPackage();
        var window = Load(DockingManagerWithInlineStyle());
        var grid = Assert.IsType<Grid>(window.Content);
        var dock = Assert.Single(grid.LogicalChildren);
        var dp = DependencyProperty.FromName(dock.GetType(), "DocumentHeaderTemplate");
        Assert.NotNull(dp);
        var template = Assert.IsType<DataTemplate>(dock.GetValue(dp));
        var panel = Assert.IsType<StackPanel>(template.VisualRoot);
        var image = Assert.IsType<Image>(panel.Children[0]);
        Assert.NotNull(image.Style);
        var style = image.Style!;
        Assert.Same(typeof(Image), style.TargetType);
        var propertyTrigger = Assert.IsType<Trigger>(Assert.Single(style.Triggers));
        Assert.Equal("Source", propertyTrigger.Property);
        Assert.Null(propertyTrigger.Value);
        var visibilitySetter = Assert.IsType<Setter>(Assert.Single(propertyTrigger.Setters));
        Assert.Same(UIElement.VisibilityProperty, visibilitySetter.Property);
        Assert.Equal(Visibility.Collapsed, visibilitySetter.Value);
    }

    [Fact]
    public void DataTemplate_loadcontent_inflates_fresh_instances_twice()
    {
        // Proves the Job 3 contract on genuine-shape bytes: two LoadContent
        // calls inflate two independent trees (distinct instances, same
        // values, separate names/binding state/lifetime). Mutating one does
        // not touch the other; DataContext stays per-instance.
        LoadJointPackage();
        var window = Load(DockingManagerWithInlineStyle());
        var dock = Assert.Single(Assert.IsType<Grid>(window.Content).LogicalChildren);
        var dp = DependencyProperty.FromName(dock.GetType(), "DocumentHeaderTemplate");
        Assert.NotNull(dp);
        var template = Assert.IsType<DataTemplate>(dock.GetValue(dp!));
        Assert.NotNull(template.TemplateSource);

        var first = template.LoadContent();
        var second = template.LoadContent();
        Assert.NotSame(first, second);
        var fp = Assert.IsType<StackPanel>(first);
        var sp = Assert.IsType<StackPanel>(second);
        Assert.Equal(Orientation.Horizontal, sp.Orientation);
        var fi = Assert.IsType<Image>(fp.Children[0]);
        var si = Assert.IsType<Image>(sp.Children[0]);
        Assert.NotSame(fi, si);
        Assert.Equal("Icon", si.Name);
        Assert.Equal(16, si.Width);
        foreach (var image in new[] { fi, si })
        {
            var sourceBinding = Assert.IsType<Binding>(
                BindingOperations.GetBindingBase(image, Image.SourceProperty));
            Assert.Equal("IconSource", sourceBinding.Path.Path);
        }
        Assert.NotSame(fi.Style, si.Style);
        var ft = Assert.IsType<TextBlock>(fp.Children[1]);
        var st = Assert.IsType<TextBlock>(sp.Children[1]);
        Assert.NotSame(ft, st);
        ft.Text = "changed";
        Assert.Equal("Header", st.Text);
        fp.DataContext = new object();
        Assert.Null(sp.DataContext);
    }

    private static byte[] DockingManagerWithInlineStyle()
    {
        var b = new B();
        b.Raw(Header()).Raw(0x01);
        b.Sized(0x1C, p => p.U16(0).Str("TestApp"));
        b.Sized(0x1C, p => p.U16(1).Str("AvalonDock"));
        b.Sized(0x1D, p => p.U16(0).U16(0).Str("TestApp.MainWindow"));
        b.Sized(0x1D, p => p.U16(1).U16(1).Str("AvalonDock.DockingManager"));
        b.Sized(0x1F, p => p.U16(0).I16(1).Raw(0x00).Str("DocumentHeaderTemplate"));
        b.Sized(0x1F, p => p.U16(1).I16(TStackPanel).Raw(0x00).Str("Orientation"));
        b.Sized(0x1F, p => p.U16(2).I16(TStyle).Raw(0x00).Str("TargetType"));
        b.Sized(0x1F, p => p.U16(3).I16(TStyle).Raw(0x00).Str("Triggers"));
        b.Sized(0x1F, p => p.U16(4).I16(TTrigger).Raw(0x00).Str("Property"));
        b.Sized(0x1F, p => p.U16(5).I16(TTrigger).Raw(0x00).Str("Value"));
        b.Sized(0x1F, p => p.U16(6).I16(TSetter).Raw(0x00).Str("Property"));
        b.Sized(0x1F, p => p.U16(7).I16(TSetter).Raw(0x00).Str("Value"));
        b.Raw(0x03).I16(0).Raw(0x00);
        b.Raw(0x2E).I16(MContent);
        b.Raw(0x03).I16(TGrid).Raw(0x00);
        b.Raw(0x2E).I16(MChildren);
        b.Raw(0x03).I16(1).Raw(0x00);
        b.Raw(0x07).I16(0);
        b.Raw(0x03).I16(TDataTemplate).Raw(0x00);
        b.Raw(0x2E).I16(MVisualTree);
        b.Raw(0x03).I16(TStackPanel).Raw(0x00);
        b.Sized(0x24, p => p.I16(1).Str("Horizontal").I16(CEnum));
        b.Raw(0x2E).I16(MChildren);
        b.Raw(0x03).I16(TImage).Raw(0x00);
        b.Sized(0x24, p => p.I16(MWidth).Str("16").I16(CLen));
        b.Sized(0x24, p => p.I16(MName).Str("Icon").I16(CStr));
        b.Raw(0x07).I16(MSource);
        b.Raw(0x03).I16(TBinding).Raw(0x00);
        b.Raw(0x2A);
        b.Sized(0x10, p => p.Str("IconSource"));
        b.Raw(0x2B);
        b.Raw(0x04);
        b.Raw(0x08);
        b.Raw(0x07).I16(MStyle);
        b.Raw(0x03).I16(TStyle).Raw(0x00);
        b.Raw(0x22).I16(2).I16(TImage);
        b.Raw(0x0B).U16(3);
        b.Raw(0x03).I16(TTrigger).Raw(0x00);
        b.Sized(0x06, p => p.I16(4).U16((ushort)SerDP).I16(MSource));
        b.Raw(0x07).I16(5);
        b.Raw(0x03).I16(TNull).Raw(0x00);
        b.Raw(0x04);
        b.Raw(0x08);
        b.Raw(0x2E).I16(MSetters);
        b.Raw(0x03).I16(TSetter).Raw(0x00);
        b.Sized(0x06, p => p.I16(6).U16((ushort)SerDP).I16(MVisibility));
        b.Sized(0x24, p => p.I16(7).Str("Collapsed").I16(CEnum));
        b.Raw(0x04);
        b.Raw(0x04);
        b.Raw(0x0C);
        b.Raw(0x04);
        b.Raw(0x08);
        b.Raw(0x04);
        b.Raw(0x03).I16(TTextBlock).Raw(0x00);
        b.Sized(0x24, p => p.I16(MText).Str("Header").I16(CStr));
        b.Raw(0x04);
        b.Raw(0x04);
        b.Raw(0x04);
        b.Raw(0x08);
        b.Raw(0x04).Raw(0x04).Raw(0x04).Raw(0x02);
        return b.ToArray();
    }

    [Fact]
    public void Converted_content_value_attaches_through_converted_dp()
    {
        // Proves the Job 2 general route: a per-file content member on a
        // hosted converted instance lands through TryApplyConvertedValue
        // (mirroring the complex-property route), with the same precise
        // type-check on mismatch. The real member-26 Layout attach is
        // exercised by the boot test.
        LoadJointPackage();
        var window = Load(DockingManagerWithContentValue());
        var grid = Assert.IsType<Grid>(window.Content);
        var dock = Assert.Single(grid.LogicalChildren);
        var themeDp = DependencyProperty.FromName(dock.GetType(), "Theme");
        Assert.NotNull(themeDp);
        var themeValue = ((DependencyObject)dock).GetValue(themeDp!);
        Assert.NotNull(themeValue);
        Assert.Equal("AvalonDock.Themes.ArcDarkTheme", themeValue!.GetType().FullName);
    }

    [Fact]
    public void LayoutRoot_model_parenting_lands_through_clr_setter()
    {
        // Granted seam FP-M27-GRANT: a converted layout-model parent takes
        // a content child through the real CLR setter. Real converted
        // LayoutRoot/LayoutPanel types via per-file tables; the synthetic
        // RootPanel attach mirrors member 27 of the real TestApp file.
        LoadJointPackage();
        var window = Load(DockingManagerWithLayoutModel());
        var grid = Assert.IsType<Grid>(window.Content);
        var dock = Assert.Single(grid.LogicalChildren);
        var layoutDp = DependencyProperty.FromName(dock.GetType(), "Layout");
        Assert.NotNull(layoutDp);
        var root = ((DependencyObject)dock).GetValue(layoutDp!);
        Assert.NotNull(root);
        Assert.Equal("AvalonDock.Layout.LayoutRoot", root!.GetType().FullName);
        var panelProperty = root.GetType().GetProperty("RootPanel");
        Assert.NotNull(panelProperty);
        var panel = panelProperty!.GetValue(root);
        Assert.NotNull(panel);
        Assert.Equal("AvalonDock.Layout.LayoutPanel", panel!.GetType().FullName);
    }

    [Fact]
    public void Model_defattribute_name_registers_in_document_namescope()
    {
        // Failing-first anchor for the real 0x17D6 wall: a converted
        // layout-model element carrying ConnectionId + DefAttribute x:Name
        // (the "WinFormsWindow" shape: sized record, 7-bit name, U16
        // 0xFFFF) must load with the name registered in the document
        // namescope. Seam implementation needs the model-names grant.
        LoadJointPackage();
        var window = Load(DockingManagerWithNamedLayoutModel());
        var grid = Assert.IsType<Grid>(window.Content);
        var dock = Assert.Single(grid.LogicalChildren);
        var layoutDp = DependencyProperty.FromName(dock.GetType(), "Layout");
        Assert.NotNull(layoutDp);
        var root = ((DependencyObject)dock).GetValue(layoutDp!);
        Assert.NotNull(root);
        var scope = NameScope.GetNameScope(window);
        Assert.NotNull(scope);
        Assert.Same(root, scope!.FindName("WinFormsWindow"));
    }

    [Fact]
    public void Object_typed_model_text_value_sets_converted_string()
    {
        // Failing-first anchor for the real 0x189E wall: a converted
        // LayoutAnchorable carrying PWC ToolTip "My WinForms Tool" with
        // StringConverter (-615) onto the object-typed CLR property must
        // load with the converted string set. The anchorable rides
        // RootPanel Children through the proven M27 content route.
        // Seam implementation needs the object-text grant.
        LoadJointPackage();
        var anchorType = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(SafeTypes)
            .First(t => t.FullName == "AvalonDock.Layout.LayoutAnchorable");
        var toolTip = anchorType.GetProperty("ToolTip");
        Assert.NotNull(toolTip);
        Assert.Equal(typeof(object), toolTip!.PropertyType);
        Assert.True(toolTip.CanWrite);
        var window = Load(LayoutAnchorableWithObjectTooltip());
        var grid = Assert.IsType<Grid>(window.Content);
        var dock = Assert.Single(grid.LogicalChildren);
        var layout = ((DependencyObject)dock).GetValue(DependencyProperty.FromName(dock.GetType(), "Layout")!);
        Assert.NotNull(layout);
        var panel = layout!.GetType().GetProperty("RootPanel")!.GetValue(layout);
        Assert.NotNull(panel);
        var panelKids = (System.Collections.IList)panel!.GetType().GetProperty("Children")!.GetValue(panel)!;
        Assert.Equal(1, panelKids.Count);
        var pane = panelKids[0];
        Assert.Equal("AvalonDock.Layout.LayoutAnchorablePane", pane!.GetType().FullName);
        var paneKids = (System.Collections.IList)pane.GetType().GetProperty("Children")!.GetValue(pane)!;
        Assert.Equal(1, paneKids.Count);
        var anchorable = paneKids[0];
        Assert.Equal("AvalonDock.Layout.LayoutAnchorable", anchorable!.GetType().FullName);
        Assert.Equal("My WinForms Tool", anchorable.GetType().GetProperty("ToolTip")!.GetValue(anchorable));
    }

    private static byte[] LayoutAnchorableWithObjectTooltip()
    {
        // Real 0x189E shape: a converted LayoutAnchorable carrying PWC
        // ToolTip with StringConverter (-615). The anchorable rides
        // RootPanel Children into a LayoutAnchorablePane (whose
        // ObservableCollection<LayoutAnchorable> accepts it) through the
        // proven M27 content/list routes.
        var b = new B();
        b.Raw(Header()).Raw(0x01);
        b.Sized(0x1C, p => p.U16(0).Str("TestApp"));
        b.Sized(0x1C, p => p.U16(1).Str("AvalonDock, Version=5.0.2.0, Culture=neutral, PublicKeyToken=null"));
        b.Sized(0x1D, p => p.U16(0).U16(0).Str("TestApp.MainWindow"));
        b.Sized(0x1D, p => p.U16(1).U16(1).Str("AvalonDock.DockingManager"));
        b.Sized(0x1D, p => p.U16(2).U16(1).Str("AvalonDock.Layout.LayoutRoot"));
        b.Sized(0x1D, p => p.U16(3).U16(1).Str("AvalonDock.Layout.LayoutPanel"));
        b.Sized(0x1D, p => p.U16(4).U16(1).Str("AvalonDock.Layout.LayoutAnchorablePane"));
        b.Sized(0x1D, p => p.U16(5).U16(1).Str("AvalonDock.Layout.LayoutAnchorable"));
        b.Sized(0x1F, p => p.U16(8).I16(1).Raw(0x00).Str("Layout"));
        b.Sized(0x1F, p => p.U16(9).I16(2).Raw(0x00).Str("RootPanel"));
        b.Sized(0x1F, p => p.U16(10).I16(3).Raw(0x00).Str("Children"));
        b.Sized(0x1F, p => p.U16(11).I16(4).Raw(0x00).Str("Children"));
        b.Sized(0x1F, p => p.U16(12).I16(5).Raw(0x00).Str("ToolTip"));
        b.Raw(0x03).I16(0).Raw(0x00);
        b.Raw(0x2E).I16(MContent);
        b.Raw(0x03).I16(TGrid).Raw(0x00);
        b.Raw(0x2E).I16(MChildren);
        b.Raw(0x03).I16(1).Raw(0x00);
        b.Raw(0x2E).I16(8);
        b.Raw(0x03).I16(2).Raw(0x00);
        b.Raw(0x2E).I16(9);
        b.Raw(0x03).I16(3).Raw(0x00);
        b.Raw(0x2E).I16(10);
        b.Raw(0x03).I16(4).Raw(0x00);
        b.Raw(0x2E).I16(11);
        b.Raw(0x03).I16(5).Raw(0x00);
        // Real 0x189E shape: PWC ToolTip with StringConverter (-615).
        b.Sized(0x24, p => p.I16(12).Str("My WinForms Tool").I16(CStr));
        b.Raw(0x04);
        b.Raw(0x04);
        b.Raw(0x04).Raw(0x04).Raw(0x04).Raw(0x04).Raw(0x04).Raw(0x02);
        return b.ToArray();
    }

    [Fact]
    public void Host_activation_contract_matches_resolver_identity()
    {
        // Drift guard: the engine-facing contract must name the exact
        // joint-interface pair the existing resolver serves (read off
        // the real provider assembly, no compile-time fork closure).
        var resolver = RealBridgeAssembly().GetType("DesktopWasm.Interop.WindowsFormsHostResolver");
        Assert.NotNull(resolver);
        Assert.Equal(
            WindowsFormsHostActivation.AssemblySimpleName,
            (string?)resolver!.GetField("JointInterfaceAssemblyName")!.GetRawConstantValue());
        Assert.Equal(
            WindowsFormsHostActivation.HostFullName,
            (string?)resolver.GetField("WindowsFormsHostFullName")!.GetRawConstantValue());
    }

    private static Assembly RealBridgeAssembly()
    {
        // Real provider assembly without a compile-time reference (keeps
        // the fork out of the test compile closure, where its global
        // types collide with engine shims). Only the bridge type is ever
        // touched, which never JITs fork code on the activation path
        // (constructor + Focusable/Name/IsDisposed only).
        var directory = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !System.IO.File.Exists(System.IO.Path.Combine(
            directory.FullName, "src", "DesktopWasm.Interop", "DesktopWasm.Interop.csproj")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        foreach (var config in new[] { "Debug", "Release" })
        {
            var dll = System.IO.Path.Combine(directory!.FullName, "src", "DesktopWasm.Interop", "bin", config, "net9.0", "DesktopWasm.Interop.dll");
            if (System.IO.File.Exists(dll)) return Assembly.LoadFrom(dll);
        }
        throw new InvalidOperationException("DesktopWasm.Interop.dll is not built (build the Interop project before running host-activation tests)");
    }

    private static Type RealBridgeType()
    {
        var resolver = RealBridgeAssembly().GetType("DesktopWasm.Interop.WindowsFormsHostResolver");
        Assert.NotNull(resolver);
        var fullName = (string?)resolver!.GetField("WindowsFormsHostFullName")!.GetRawConstantValue();
        var hostType = (Type?)resolver.GetMethod("ResolveHostType")!.Invoke(null, new object?[] { "WindowsFormsIntegration", fullName });
        Assert.NotNull(hostType);
        Assert.Equal(WindowsFormsHostActivation.HostFullName, hostType!.FullName);
        return hostType;
    }

    [Fact]
    public void Host_activation_uses_real_bridge_provider()
    {
        // HOST-CALL-1425 positive: the exact joint-interface pair
        // activates the real fork-backed bridge with genuine
        // construction (the bridge ctor takes focus; the engine stub
        // does not), the Name PWC lands, Child stays null.
        LoadJointPackage();
        var hostType = RealBridgeType();
        WindowsFormsHostActivation.Register(() => (UIElement)Activator.CreateInstance(hostType)!);
        try
        {
            var window = Load(WindowsFormsHostElement());
            var grid = Assert.IsType<Grid>(window.Content);
            var host = Assert.Single(grid.LogicalChildren);
            Assert.Equal(WindowsFormsHostActivation.HostFullName, host.GetType().FullName);
            Assert.True(((FrameworkElement)host).Focusable);
            Assert.Equal("winFormsHost", ((FrameworkElement)host).Name);
            // Child is not read here: touching the fork-typed property
            // would JIT the fork assembly, which the activation path
            // never needs.
            Assert.False((bool)host.GetType().GetProperty("IsDisposed")!.GetValue(host)!);
        }
        finally
        {
            WindowsFormsHostActivation.Unregister();
        }
    }

    [Fact]
    public void Host_activation_missing_provider_is_precise()
    {
        WindowsFormsHostActivation.Unregister();
        var ex = Assert.Throws<XamlLoadException>(() => Load(WindowsFormsHostElement()));
        Assert.Contains("WindowsFormsHost bridge provider is not registered", ex.Message);
    }

    [Fact]
    public void Host_activation_wrong_type_provider_is_precise()
    {
        WindowsFormsHostActivation.Register(() => new Grid());
        try
        {
            var ex = Assert.Throws<XamlLoadException>(() => Load(WindowsFormsHostElement()));
            Assert.Contains("must construct the canonical host type", ex.Message);
            Assert.Contains("DesktopWasm.Wpf.Grid", ex.Message);
        }
        finally
        {
            WindowsFormsHostActivation.Unregister();
        }
    }

    [Fact]
    public void Host_activation_constructor_failure_is_precise()
    {
        WindowsFormsHostActivation.Register(() => throw new InvalidOperationException("boom"));
        try
        {
            var ex = Assert.Throws<XamlLoadException>(() => Load(WindowsFormsHostElement()));
            Assert.Contains("bridge provider constructor failed", ex.Message);
            Assert.Contains("boom", ex.Message);
        }
        finally
        {
            WindowsFormsHostActivation.Unregister();
        }
    }

    [Fact]
    public void Host_activation_contract_rejects_other_joint_types()
    {
        // Same joint assembly, different type: the contract takes the
        // exact pair only (BAML-level, such elements keep the precise
        // v1 boundary instead of activating anything).
        var ok = WindowsFormsHostActivation.TryCreate(
            "WindowsFormsIntegration",
            "System.Windows.Forms.Integration.ElementHost",
            out var element, out var failure);
        Assert.False(ok);
        Assert.Null(element);
        Assert.Contains("exact joint-interface pair", failure);
    }

    [Fact]
    public void Real_mainwindow_boot_advances_past_host_wall_with_provider()
    {
        // ROOT-CONTRACT-1915: with the real bridge provider registered,
        // the TestApp boot parses end-to-end with no error — the 0x1F75
        // converted-root wall is gone (root stand-in plus the UserControl
        // Content arm). The bridge host lands in its converted-model slot
        // (Content of the WinFormsWindow LayoutAnchorable) and the 0x1F75
        // TestUserControl lands in a tool-window anchorable StackPanel;
        // neither is reachable through FrameworkElement.LogicalChildren,
        // so the success witness walks the DockingManager layout model.
        LoadJointPackage();
        var hostType = RealBridgeType();
        WindowsFormsHostActivation.Register(() => (UIElement)Activator.CreateInstance(hostType)!);
        try
        {
            var bytes = LoadRealMainWindowBaml();
            UIElement? root = null;
            XamlLoadException? error = null;
            try
            {
                root = BamlReader.LoadRoot(bytes);
            }
            catch (XamlLoadException ex)
            {
                error = ex;
            }
            if (error is not null)
            {
                Assert.Fail("real TestApp boot regressed past 0x1F75: " + error.Message);
            }
            var anchorable = FindLayoutAnchorable(root!, "WinFormsWindow");
            Assert.NotNull(anchorable);
            var host = anchorable!.GetType().GetProperty("Content")?.GetValue(anchorable);
            Assert.NotNull(host);
            Assert.Equal("System.Windows.Forms.Integration.WindowsFormsHost", host!.GetType().FullName);
            Assert.Equal("winFormsHost", ((FrameworkElement)host).Name);
            var demoControl = FindInLayoutModel(root!, node => string.Equals(node.GetType().FullName, "TestApp.TestUserControl", StringComparison.Ordinal));
            Assert.NotNull(demoControl);
        }
        finally
        {
            WindowsFormsHostActivation.Unregister();
        }
    }

    [Fact]
    public void Panel_background_packed_brush_lands_from_property_custom()
    {
        // Failing-first anchor for the real 0x1BA3 wall: an engine Grid
        // carrying PropertyCustom Background with the packed-ARGB brush
        // serializer (744) and the Transparent value — the byte-identical
        // record from the genuine fw122 Background="Transparent" fixture,
        // matching all three real-file sites — must load with a
        // Transparent SolidColorBrush Background. Seam implementation
        // needs the icon-brush grant.
        var window = Load(GridWithTransparentBackground());
        var grid = Assert.IsType<Grid>(window.Content);
        var bg = Assert.IsType<SolidColorBrush>(grid.Background);
        Assert.Equal(Colors.Transparent, bg.Color);
    }

    [Fact]
    public void Button_element_lands_with_content_from_wellknown_id()
    {
        // Failing-first anchor for the real 0x1BCA wall: an engine Grid
        // carrying a Button child (well-known type -55, verified by the
        // genuine fw123 fixture where Button emits 03 C9 FF) with the
        // byte-identical 0x1BCA shape — ConnectionId 45, Height "28",
        // VerticalAlignment "Top", Content "Click to add 2 documents" —
        // must load as a live Button. Seam implementation needs the
        // Button -55 grant.
        var window = Load(GridWithDocumentPlaceholderButton());
        var grid = Assert.IsType<Grid>(window.Content);
        var button = Assert.IsType<Button>(Assert.Single(grid.LogicalChildren));
        Assert.Equal("Click to add 2 documents", button.Content);
        Assert.Equal(28.0, button.Height);
        Assert.Equal(VerticalAlignment.Top, button.VerticalAlignment);
    }

    [Fact]
    public void TextBox_background_packed_brush_lands_from_property_custom()
    {
        // Failing-first anchor for the real 0x1C12 wall: an engine Grid
        // carrying a TextBox child with Grid.Row 1, PropertyCustom
        // Background member -24 (TextBox.Background, fw124-verified with
        // the byte-identical 06 0A E8 FF E8 02 01 FF FF FF 00) and Text
        // "Document 1 Content" — must land as a live TextBox with a
        // Transparent brush Background synced to paint state. Seam
        // implementation needs the TextBox -24 grant.
        var window = Load(GridWithTransparentTextBox());
        var grid = Assert.IsType<Grid>(window.Content);
        var box = Assert.IsType<TextBox>(Assert.Single(grid.LogicalChildren));
        var bg = Assert.IsType<SolidColorBrush>(box.Background);
        Assert.Equal(Colors.Transparent, bg.Color);
        Assert.Equal(bg.ToRgba(), box.BackgroundRgba);
        Assert.Equal("Document 1 Content", box.Text);
        Assert.Equal(1, Grid.GetRow(box));
    }

    [Fact]
    public void TextBox_borderthickness_zero_lands_from_thickness_converter()
    {
        // Failing-first anchor for the real 0x1C98 wall: an engine Grid
        // carrying a TextBox child with BorderThickness "0" (member -26,
        // fw125-verified with the byte-identical 24 07 E6 FF 01 30 71 FD)
        // — must land as a zero Thickness. Seam implementation needs the
        // TextBox -26 grant.
        var window = Load(GridWithBorderlessTextBox());
        var grid = Assert.IsType<Grid>(window.Content);
        var box = Assert.IsType<TextBox>(Assert.Single(grid.LogicalChildren));
        Assert.Equal(new Thickness(0), box.BorderThickness);
    }

    private static byte[] GridWithBorderlessTextBox()
    {
        // Real 0x1C98 shape (genuine TestApp mainwindow.baml, member
        // identity proven by the fw125 fixture): TextBox element (-639)
        // carrying BorderThickness "0" with ThicknessConverter (-26/-655).
        var b = new B();
        b.Raw(Header()).Raw(0x01);
        b.Sized(0x1C, p => p.U16(0).Str("TestApp"));
        b.Sized(0x1D, p => p.U16(0).U16(0).Str("TestApp.MainWindow"));
        b.Raw(0x03).I16(0).Raw(0x00);
        b.Raw(0x2E).I16(MContent);
        b.Raw(0x03).I16(TGrid).Raw(0x00);
        b.Raw(0x2E).I16(MChildren);
        b.Raw(0x03).I16(-639).Raw(0x00);
        b.Sized(0x24, p => p.I16(-26).Str("0").I16(CThick));
        b.Raw(0x04);
        b.Raw(0x04).Raw(0x04).Raw(0x02);
        return b.ToArray();
    }

    [Fact]
    public void TextBox_text_binding_with_relative_source_lands()
    {
        // Failing-first anchor for the real 0x1CCE wall: an engine Grid
        // carrying a TextBox whose Text is a complex Binding with a path
        // plus a complex RelativeSource child (FindAncestor/Window/1) —
        // the byte-shape of the real TextBox.Text binding (member -516,
        // fw126-verified) — must land as a live binding on a Text DP.
        // Seam implementation needs the RelativeSource -516 grant.
        var window = Load(GridWithRelativeSourceTextBinding());
        var grid = Assert.IsType<Grid>(window.Content);
        var box = Assert.IsType<TextBox>(Assert.Single(grid.LogicalChildren));
        var textDp = DependencyProperty.FromName(typeof(TextBox), "Text");
        Assert.NotNull(textDp);
        var expr = Assert.IsType<BindingExpression>(BindingOperations.GetBindingExpression(box, textDp!));
        Assert.Equal("Title", expr.Binding.Path.Path);
        Assert.NotNull(expr.Binding.RelativeSource);
        var rs = expr.Binding.RelativeSource!;
        Assert.Equal(RelativeSourceMode.FindAncestor, rs.Mode);
        Assert.Equal(typeof(Window), rs.AncestorType);
        Assert.Equal(1, rs.AncestorLevel);
    }

    [Fact]
    public void Binding_mode_oneway_lands_from_enum_converter()
    {
        // Failing-first anchor for the real 0x1CFF wall: an engine Grid
        // carrying a TextBox whose Text is a complex Binding with a path
        // plus a Mode "OneWay" simple (EnumConverter, per-file attr —
        // fw127-verified byte-shape 24 .. 06 "OneWay" 3D FF) — must land
        // as a OneWay binding. Seam implementation needs the Binding.Mode
        // grant.
        var window = Load(GridWithOneWayTextBinding());
        var grid = Assert.IsType<Grid>(window.Content);
        var box = Assert.IsType<TextBox>(Assert.Single(grid.LogicalChildren));
        var textDp = DependencyProperty.FromName(typeof(TextBox), "Text");
        Assert.NotNull(textDp);
        var expr = Assert.IsType<BindingExpression>(BindingOperations.GetBindingExpression(box, textDp!));
        Assert.Equal("Title", expr.Binding.Path.Path);
        Assert.Equal(BindingMode.OneWay, expr.Binding.Mode);
    }

    [Fact]
    public void Binding_stringformat_lands_from_string_converter()
    {
        // Failing-first anchor for the real 0x1D20 wall: an engine Grid
        // carrying a TextBox whose Text is a complex Binding with a path
        // plus a StringFormat "Document: {0}" simple (StringConverter,
        // per-file attr — fw128-verified byte-shape 24 .. "Document: {0}"
        // 99 FD) — must land on the Binding. Seam implementation needs the
        // Binding.StringFormat grant.
        var window = Load(GridWithFormattedTextBinding());
        var grid = Assert.IsType<Grid>(window.Content);
        var box = Assert.IsType<TextBox>(Assert.Single(grid.LogicalChildren));
        var textDp = DependencyProperty.FromName(typeof(TextBox), "Text");
        Assert.NotNull(textDp);
        var expr = Assert.IsType<BindingExpression>(BindingOperations.GetBindingExpression(box, textDp!));
        Assert.Equal("Title", expr.Binding.Path.Path);
        Assert.Equal("Document: {0}", expr.Binding.StringFormat);
    }

    private static byte[] GridWithFormattedTextBinding()
    {
        // Real 0x1D20 shape (genuine TestApp mainwindow.baml, member
        // identity proven by the fw128 fixture): TextBox element (-639)
        // with complex Text (-114) holding a Binding (-20) with a ctor
        // path plus a StringFormat simple with StringConverter (per-file
        // attr; real file uses id 46, fw128 uses id 0 — name/owner match).
        var b = new B();
        b.Raw(Header()).Raw(0x01);
        b.Sized(0x1C, p => p.U16(0).Str("TestApp"));
        b.Sized(0x1D, p => p.U16(0).U16(0).Str("TestApp.MainWindow"));
        b.Sized(0x1F, p => p.U16(40).I16(-20).Raw(0x00).Str("StringFormat"));
        b.Raw(0x03).I16(0).Raw(0x00);
        b.Raw(0x2E).I16(MContent);
        b.Raw(0x03).I16(TGrid).Raw(0x00);
        b.Raw(0x2E).I16(MChildren);
        b.Raw(0x03).I16(-639).Raw(0x00);
        b.Raw(0x07).I16(-114);
        b.Raw(0x03).I16(-20).Raw(0x00);
        b.Raw(0x2A);
        b.Sized(0x10, p => p.Str("Title"));
        b.Raw(0x2B);
        b.Sized(0x24, p => p.I16(40).Str("Document: {0}").I16(CStr));
        b.Raw(0x04);
        b.Raw(0x08);
        b.Raw(0x04).Raw(0x04).Raw(0x04).Raw(0x02);
        return b.ToArray();
    }

    [Fact]
    public void StackPanel_minheight_lands_from_length_converter()
    {
        // Failing-first anchor for the real 0x1F3F wall: an engine Grid
        // carrying a StackPanel child with MinHeight "121" (member -52
        // with LengthConverter, fw129-verified byte-shape 24 09 CC FF 03
        // "121" A4 FE, byte-identical to the real 0x1F3F record except the
        // value "450") — must land as 121.0. Seam implementation needs
        // the member-52 grant.
        var window = Load(GridWithMinHeightStackPanel());
        var grid = Assert.IsType<Grid>(window.Content);
        var panel = Assert.IsType<StackPanel>(Assert.Single(grid.LogicalChildren));
        Assert.Equal(121.0, panel.MinHeight);
    }

    private static byte[] GridWithMinHeightStackPanel()
    {
        // Real 0x1F3F shape (genuine TestApp mainwindow.baml, member
        // identity proven by the fw129 fixture): StackPanel element
        // (-601) carrying MinHeight (member -52) with LengthConverter.
        var b = new B();
        b.Raw(Header()).Raw(0x01);
        b.Sized(0x1C, p => p.U16(0).Str("TestApp"));
        b.Sized(0x1D, p => p.U16(0).U16(0).Str("TestApp.MainWindow"));
        b.Raw(0x03).I16(0).Raw(0x00);
        b.Raw(0x2E).I16(MContent);
        b.Raw(0x03).I16(TGrid).Raw(0x00);
        b.Raw(0x2E).I16(MChildren);
        b.Raw(0x03).I16(TStackPanel).Raw(0x00);
        b.Sized(0x24, p => p.I16(-52).Str("121").I16(CLen));
        b.Raw(0x04);
        b.Raw(0x04).Raw(0x04).Raw(0x02);
        return b.ToArray();
    }

    [Fact]
    public void TestUserControl_element_lands_from_converted_type()
    {
        // Failing-first anchor for the real 0x1F75 wall: an engine Grid
        // carrying an empty TestApp.TestUserControl child (per-file
        // TypeInfo, fw130-verified shape 1D .. "TestControl" + 03 <id>
        // 00 04, byte-identical to the real 0x1F75 element except the
        // type id/name) — must land as the live converted engine
        // UserControl from the loaded joint closure. Seam implementation
        // needs the user-control grant.
        LoadJointPackage();
        var window = Load(GridWithTestUserControl());
        var grid = Assert.IsType<Grid>(window.Content);
        var control = Assert.IsAssignableFrom<UserControl>(Assert.Single(grid.LogicalChildren));
        Assert.Equal("TestApp.TestUserControl", control.GetType().FullName);
    }

    [Fact]
    public void TestApp_mainwindow_boots_and_wires_winformshost_child()
    {
        // Demonstrates and verifies original mixed TestApp MainWindow boots,
        // wires winFormsHost via NameScope from BAML, and sets its Child to
        // TestApp.UserControl1 (the genuine WinForms UserControl) via the bridge.
        LoadJointPackage();
        var hostType = RealBridgeType();
        WindowsFormsHostActivation.Register(() => (UIElement)Activator.CreateInstance(hostType)!);
        try
        {
            var testApp = AppDomain.CurrentDomain.GetAssemblies()
                .First(a => string.Equals(a.GetName().Name, "TestApp", StringComparison.OrdinalIgnoreCase));
            var mainWinType = testApp.GetType("TestApp.MainWindow");
            Assert.NotNull(mainWinType);

            var window = (Window)Activator.CreateInstance(mainWinType)!;
            Assert.NotNull(window);
            var hostField = mainWinType.GetField("winFormsHost", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            var host = hostField?.GetValue(window);
            Assert.NotNull(host);
            var childProp = host!.GetType().GetProperty("Child");
            var child = childProp?.GetValue(host);
            Assert.NotNull(child);
            Assert.Equal("TestApp.UserControl1", child!.GetType().FullName);

            // Exercise bridge layout passes on the live hosted child
            var hostElement = Assert.IsAssignableFrom<UIElement>(host);
            hostElement.Measure(new WpfSize(400, 300));
            hostElement.Arrange(new WpfRect(0, 0, 400, 300));
        }
        finally
        {
            WindowsFormsHostActivation.Unregister();
        }
    }

    [Fact]
    public void TestApp_merged_ResourceDictionary_and_NameScope_lookup_integration()
    {
        // Demonstrates and verifies merged ResourceDictionary and NameScope lookup
        // integration in genuine converted mixed TestApp:
        // 1. MainWindow boots and resolves NameScope registered elements (winFormsHost).
        // 2. MergedDictionaries integration allows theme/resource dictionaries to be merged.
        // 3. TryFindResource and FindResource resolve resources across MergedDictionaries
        //    hierarchy with proper precedence (last merged dictionary wins).
        // 4. NameScope.FindName and FrameworkElement.FindName consistently resolve the
        //    bridge-wired WindowsFormsHost and its live hosted child.
        LoadJointPackage();
        var hostType = RealBridgeType();
        WindowsFormsHostActivation.Register(() => (UIElement)Activator.CreateInstance(hostType)!);
        try
        {
            var testApp = AppDomain.CurrentDomain.GetAssemblies()
                .First(a => string.Equals(a.GetName().Name, "TestApp", StringComparison.OrdinalIgnoreCase));
            var mainWinType = testApp.GetType("TestApp.MainWindow");
            Assert.NotNull(mainWinType);

            var window = (Window)Activator.CreateInstance(mainWinType)!;
            Assert.NotNull(window);

            // NameScope lookup integration
            var scope = NameScope.GetNameScope(window);
            Assert.NotNull(scope);

            var namedFromScope = scope.FindName("winFormsHost");
            Assert.NotNull(namedFromScope);

            var namedFromElement = window.FindName("winFormsHost");
            Assert.Same(namedFromScope, namedFromElement);

            var hostField = mainWinType.GetField("winFormsHost", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            var hostFromField = hostField?.GetValue(window);
            Assert.Same(hostFromField, namedFromScope);

            var childProp = namedFromScope.GetType().GetProperty("Child");
            var child = childProp?.GetValue(namedFromScope);
            Assert.NotNull(child);
            Assert.Equal("TestApp.UserControl1", child!.GetType().FullName);

            // Merged ResourceDictionary integration
            var themeDict1 = new ResourceDictionary();
            themeDict1.Add("AccentBrush", new SolidColorBrush(Colors.Blue));
            themeDict1.Add("HeaderFontWeight", FontWeights.Bold);
            window.Resources.MergedDictionaries.Add(themeDict1);

            var themeDict2 = new ResourceDictionary();
            themeDict2.Add("AccentBrush", new SolidColorBrush(Colors.Red)); // Overrides themeDict1
            themeDict2.Add("SidebarWidth", 250.0);
            window.Resources.MergedDictionaries.Add(themeDict2);

            // Verify TryFindResource / FindResource resolution and override precedence
            Assert.True(window.TryFindResource("AccentBrush", out var resolvedAccent));
            var brush = Assert.IsType<SolidColorBrush>(resolvedAccent);
            Assert.Equal(Colors.Red, brush.Color); // Later merged dictionary wins

            Assert.True(window.TryFindResource("HeaderFontWeight", out var resolvedWeight));
            Assert.Equal(FontWeights.Bold, resolvedWeight);

            Assert.True(window.TryFindResource("SidebarWidth", out var resolvedWidth));
            Assert.Equal(250.0, (double)resolvedWidth!);

            // Measure and Arrange passes verify layout integration with merged resources active
            window.Measure(new WpfSize(800, 600));
            window.Arrange(new WpfRect(0, 0, 800, 600));
            Assert.True(window.DesiredSize.Width > 0);
            Assert.True(window.DesiredSize.Height > 0);
        }
        finally
        {
            WindowsFormsHostActivation.Unregister();
        }
    }

    [Fact]
    public void Nested_UserControl_document_elements_map_namescope_and_content_in_BamlReader()
    {
        // Validates BamlReader nested document element mapping for UserControls:
        // Window -> Grid -> nested UserControl ("nestedUc") -> TextBlock ("innerTb")
        // Both nested element names must register in root NameScope and content
        // must attach properly to the UserControl.
        var window = Load(WindowWithNestedUserControlAndNamedElements());
        var grid = Assert.IsType<Grid>(window.Content);
        var uc = Assert.IsType<UserControl>(Assert.Single(grid.LogicalChildren));
        Assert.Equal("nestedUc", uc.Name);

        var tb = Assert.IsType<TextBlock>(uc.Content);
        Assert.Equal("innerTb", tb.Name);
        Assert.Equal("Nested Value", tb.Text);

        var scope = NameScope.GetNameScope(window);
        Assert.NotNull(scope);
        Assert.Same(uc, scope.FindName("nestedUc"));
        Assert.Same(tb, scope.FindName("innerTb"));

        window.Measure(new WpfSize(500, 400));
        window.Arrange(new WpfRect(0, 0, 500, 400));
        Assert.True(window.DesiredSize.Width > 0);
        Assert.True(window.DesiredSize.Height > 0);
    }

    private static byte[] WindowWithNestedUserControlAndNamedElements()
    {
        var b = new B();
        b.Raw(Header()).Raw(0x01);
        b.Sized(0x1C, p => p.U16(0).Str("TestApp"));
        b.Sized(0x1C, p => p.U16(1).Str("PresentationFramework"));
        b.Sized(0x1D, p => p.U16(0).U16(0).Str("TestApp.MainWindow"));
        b.Sized(0x1D, p => p.U16(1).U16(1).Str("System.Windows.Controls.UserControl"));
        // Window root (type 0):
        b.Raw(0x03).I16(0).Raw(0x00);
        b.Raw(0x2E).I16(MContent);
        b.Raw(0x03).I16(TGrid).Raw(0x00);
        b.Raw(0x2E).I16(MChildren);
        // Nested UserControl (type 1):
        b.Raw(0x03).I16(1).Raw(0x00);
        b.Sized(0x24, p => p.I16(MName).Str("nestedUc").I16(CStr));
        // Content of nested UserControl:
        b.Raw(0x2E).I16(MContent);
        b.Raw(0x03).I16(TTextBlock).Raw(0x00);
        b.Sized(0x24, p => p.I16(MName).Str("innerTb").I16(CStr));
        b.Sized(0x24, p => p.I16(MText).Str("Nested Value").I16(CStr));
        b.Raw(0x04); // End TextBlock
        b.Raw(0x04); // End UserControl
        b.Raw(0x04); // End Grid
        b.Raw(0x04); // End Window
        b.Raw(0x02); // DocumentEnd
        return b.ToArray();
    }

    [Fact]
    public void Converted_testusercontrol_carries_live_parsed_content()
    {
        // Live-content hydration: the converted TestUserControl ctor runs
        // LoadComponent on the real testusercontrol.baml, so the LIVE
        // instance (not just the detached stand-in) must carry the parsed
        // Content subtree — the Grid holding the demo TextBlock with the
        // genuine "Auto-Size FloatingWindow" text from the real
        // testusercontrol.baml. Grounds the Hydrate copy-onto-component
        // contract on genuine bytes.
        LoadJointPackage();
        var type = DockingIntegration.TryResolveUserControl("TestApp.TestUserControl");
        Assert.NotNull(type);
        var control = Assert.IsAssignableFrom<UserControl>(Activator.CreateInstance(type!, nonPublic: true)!);
        var content = Assert.IsType<Grid>(control.Content);
        var demo = Assert.Single(Descendants(content).OfType<TextBlock>());
        Assert.Contains("Auto-Size FloatingWindow", demo.Text, StringComparison.Ordinal);
    }

    private static byte[] GridWithTestUserControl()
    {
        // Real 0x1F75 shape (genuine TestApp mainwindow.baml, type
        // identity proven by the fw130 fixture): per-file TestUserControl
        // TypeInfo plus an empty ElementStart/ElementEnd pair.
        var b = new B();
        b.Raw(Header()).Raw(0x01);
        b.Sized(0x1C, p => p.U16(0).Str("TestApp"));
        b.Sized(0x1D, p => p.U16(0).U16(0).Str("TestApp.MainWindow"));
        b.Sized(0x1D, p => p.U16(1).U16(0).Str("TestApp.TestUserControl"));
        b.Raw(0x03).I16(0).Raw(0x00);
        b.Raw(0x2E).I16(MContent);
        b.Raw(0x03).I16(TGrid).Raw(0x00);
        b.Raw(0x2E).I16(MChildren);
        b.Raw(0x03).I16(1).Raw(0x00);
        b.Raw(0x04);
        b.Raw(0x04).Raw(0x04).Raw(0x02);
        return b.ToArray();
    }

    [Fact]
    public void Testusercontrol_root_lands_as_usercontrol_standin()
    {
        // Converted-root contract (0x1F75 wall): a BAML file whose root
        // per-file type names the converted TestApp.TestUserControl
        // element — the shape the converted TestUserControl ctor's own
        // testusercontrol.baml presents — must parse as a detached
        // plain engine UserControl (MinWidth "120", member -53 with
        // LengthConverter, fw129-verified byte shape), never a Window.
        // The converted constructor is itself running LoadComponent
        // and must not be re-entered; WpfAppBoot hydrates this
        // stand-in onto the live component instance.
        using var stream = new MemoryStream(TestUserControlRootWithMinWidth());
        var control = XamlLoader.LoadComponent<UserControl>(stream);
        Assert.Equal(120.0, control.MinWidth);
    }

    [Fact]
    public void Testusercontrol_root_is_not_a_window()
    {
        // Negative incompatible-root pin: the same TestUserControl-rooted
        // bytes must not load as a Window (the pre-contract behavior).
        using var stream = new MemoryStream(TestUserControlRootWithMinWidth());
        Assert.Throws<XamlLoadException>(() => XamlLoader.LoadComponent<Window>(stream));
    }

    [Fact]
    public void Mainwindow_root_stays_window_not_usercontrol()
    {
        // Main-window root route preservation: MainWindow-rooted bytes
        // still land as a Window (MinWidth 120.0) and must not load as
        // a UserControl.
        var window = Load(WindowWithMinWidth());
        Assert.Equal(120.0, window.MinWidth);
        using var stream = new MemoryStream(WindowWithMinWidth());
        Assert.Throws<XamlLoadException>(() => XamlLoader.LoadComponent<UserControl>(stream));
    }

    private static byte[] TestUserControlRootWithMinWidth()
    {
        // Root-contract fixture: single TestApp assembly record plus the
        // TestApp.TestUserControl per-file type, root element of that
        // type carrying MinWidth (member -53, LengthConverter).
        var b = new B();
        b.Raw(Header()).Raw(0x01);
        b.Sized(0x1C, p => p.U16(0).Str("TestApp"));
        b.Sized(0x1D, p => p.U16(0).U16(0).Str("TestApp.TestUserControl"));
        b.Raw(0x03).I16(0).Raw(0x00);
        b.Sized(0x24, p => p.I16(-53).Str("120").I16(CLen));
        b.Raw(0x04);
        b.Raw(0x02);
        return b.ToArray();
    }

    [Fact]
    public void Window_minwidth_lands_from_length_converter()
    {
        // Failing-first anchor for the nested 0x67D wall (inside the
        // converted TestUserControl ctor's own BAML, blocking the 0x1F75
        // pin): a Window root carrying MinWidth "120" (member -53 with
        // LengthConverter, fw129-verified byte-shape 24 09 CB FF 03
        // "120" A4 FE) — must land as 120.0. Seam implementation needs
        // the member-53 grant.
        var window = Load(WindowWithMinWidth());
        Assert.Equal(120.0, window.MinWidth);
    }

    private static byte[] WindowWithMinWidth()
    {
        // Nested 0x67D shape (testusercontrol.baml inside the converted
        // TestUserControl ctor; member identity proven by the fw129
        // fixture: MinWidth emits member -53 with LengthConverter):
        // Window root carrying the MinWidth simple.
        var b = new B();
        b.Raw(Header()).Raw(0x01);
        b.Sized(0x1C, p => p.U16(0).Str("TestApp"));
        b.Sized(0x1D, p => p.U16(0).U16(0).Str("TestApp.MainWindow"));
        b.Raw(0x03).I16(0).Raw(0x00);
        b.Sized(0x24, p => p.I16(-53).Str("120").I16(CLen));
        b.Raw(0x04);
        b.Raw(0x02);
        return b.ToArray();
    }

    [Fact]
    public void TextBlock_textwrapping_lands_from_enum_converter()
    {
        // Failing-first anchor for the nested 0x6B9 wall (inside the
        // converted TestUserControl ctor's own BAML, blocking the 0x1F75
        // pin): an engine Grid carrying a TextBlock child with
        // TextWrapping "WrapWithOverflow" (member -113 with
        // EnumConverter, fw131-verified byte-shape 24 16 8F FF 10
        // "WrapWithOverflow" 3D FF, byte-identical to the nested 0x6B9
        // record) — must land as WrapWithOverflow. Seam implementation
        // needs the member-113 grant.
        var window = Load(GridWithWrappingTextBlock());
        var grid = Assert.IsType<Grid>(window.Content);
        var text = Assert.IsType<TextBlock>(Assert.Single(grid.LogicalChildren));
        Assert.Equal(TextWrapping.WrapWithOverflow, text.TextWrapping);
    }

    private static byte[] GridWithWrappingTextBlock()
    {
        // Nested 0x6B9 shape (testusercontrol.baml inside the converted
        // TestUserControl ctor; member identity proven by the fw131
        // fixture): TextBlock element (-638) carrying TextWrapping
        // (member -113) with EnumConverter.
        var b = new B();
        b.Raw(Header()).Raw(0x01);
        b.Sized(0x1C, p => p.U16(0).Str("TestApp"));
        b.Sized(0x1D, p => p.U16(0).U16(0).Str("TestApp.MainWindow"));
        b.Raw(0x03).I16(0).Raw(0x00);
        b.Raw(0x2E).I16(MContent);
        b.Raw(0x03).I16(TGrid).Raw(0x00);
        b.Raw(0x2E).I16(MChildren);
        b.Raw(0x03).I16(-638).Raw(0x00);
        b.Sized(0x24, p => p.I16(-113).Str("WrapWithOverflow").I16(CEnum));
        b.Raw(0x04);
        b.Raw(0x04).Raw(0x04).Raw(0x02);
        return b.ToArray();
    }

    [Fact]
    public void TextBlock_nested_text_appends_to_parent_text()
    {
        // Failing-first anchor for the nested 0x6D0 wall (inside the
        // converted TestUserControl ctor's own BAML, blocking the 0x1F75
        // pin): an engine TextBlock carrying a nested TextBlock child
        // with "0." plus trailing "This Control demos" (0x10 text runs;
        // nesting shape fw132-verified: outer 03 82 FD 00, inner 03 82
        // FD 00 with FontWeight PWC, then 0x10 runs) — must land as the
        // concatenated "0.This Control demos" on the parent Text. Seam
        // implementation needs the nested-text grant (inline styling is
        // v1-dropped, like ReadText accumulation).
        var window = Load(GridWithNestedTextBlock());
        var grid = Assert.IsType<Grid>(window.Content);
        var text = Assert.IsType<TextBlock>(Assert.Single(grid.LogicalChildren));
        Assert.Equal("0.This Control demos", text.Text);
    }

    private static byte[] GridWithNestedTextBlock()
    {
        // Nested 0x6D0 shape (testusercontrol.baml inside the converted
        // TestUserControl ctor; nesting shape proven by the fw132
        // fixture): TextBlock element (-638) carrying a nested TextBlock
        // child plus a trailing 0x10 text run.
        var b = new B();
        b.Raw(Header()).Raw(0x01);
        b.Sized(0x1C, p => p.U16(0).Str("TestApp"));
        b.Sized(0x1D, p => p.U16(0).U16(0).Str("TestApp.MainWindow"));
        b.Raw(0x03).I16(0).Raw(0x00);
        b.Raw(0x2E).I16(MContent);
        b.Raw(0x03).I16(TGrid).Raw(0x00);
        b.Raw(0x2E).I16(MChildren);
        b.Raw(0x03).I16(-638).Raw(0x00);
        b.Sized(0x24, p => p.I16(-113).Str("WrapWithOverflow").I16(CEnum));
        b.Raw(0x03).I16(-638).Raw(0x00);
        b.Sized(0x10, p => p.Str("0."));
        b.Raw(0x04);
        b.Sized(0x10, p => p.Str("This Control demos"));
        b.Raw(0x04);
        b.Raw(0x04).Raw(0x04).Raw(0x02);
        return b.ToArray();
    }

    [Fact]
    public void TextBlock_nested_baml_child_preserves_bold_styling_in_segments()
    {
        // Reader attachment contract consumption for BamlReader:
        // Nested TextBlock with FontWeight="Bold" preserves its styling as an
        // inline Span attached via AddInline, while trailing text attaches via
        // AppendLiteral. BuildSegments() produces styled segments in encounter order.
        var window = Load(GridWithNestedBoldTextBlock());
        var grid = Assert.IsType<Grid>(window.Content);
        var text = Assert.IsType<TextBlock>(Assert.Single(grid.LogicalChildren));
        Assert.Equal("0.This Control demos", text.Text);
        var inline = Assert.Single(text.Inlines);
        var span = Assert.IsType<Span>(inline);
        Assert.Equal("0.", span.Text);
        Assert.Equal(FontWeights.Bold, span.FontWeight);

        var frame = WasmMilCore.RenderHeadless(text, new WpfSize(400, 100));
        var texts = frame.OfType<DrawCommand.DrawText>().ToList();
        Assert.Contains(texts, d => d.Text.Contains("0.", StringComparison.Ordinal) &&
            d.FontCss.Contains("bold", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(texts, d => d.Text.Contains("This Control demos", StringComparison.Ordinal) &&
            !d.FontCss.Contains("bold", StringComparison.OrdinalIgnoreCase));
    }

    private static byte[] GridWithNestedBoldTextBlock()
    {
        // Outer TextBlock (-638) with TextWrapping WrapWithOverflow (-113)
        // Nested inner TextBlock (-638) with FontWeight Bold (-108, CFw) and text "0."
        // Trailing 0x10 text run "This Control demos"
        var b = new B();
        b.Raw(Header()).Raw(0x01);
        b.Sized(0x1C, p => p.U16(0).Str("TestApp"));
        b.Sized(0x1D, p => p.U16(0).U16(0).Str("TestApp.MainWindow"));
        b.Raw(0x03).I16(0).Raw(0x00);
        b.Raw(0x2E).I16(MContent);
        b.Raw(0x03).I16(TGrid).Raw(0x00);
        b.Raw(0x2E).I16(MChildren);
        b.Raw(0x03).I16(-638).Raw(0x00);
        b.Sized(0x24, p => p.I16(-113).Str("WrapWithOverflow").I16(CEnum));
        b.Raw(0x03).I16(-638).Raw(0x00);
        b.Sized(0x24, p => p.I16(-108).Str("Bold").I16(CFw));
        b.Sized(0x10, p => p.Str("0."));
        b.Raw(0x04);
        b.Sized(0x10, p => p.Str("This Control demos"));
        b.Raw(0x04);
        b.Raw(0x04).Raw(0x04).Raw(0x02);
        return b.ToArray();
    }

    [Fact]
    public void TextBlock_fontweight_bold_lands_from_converter()
    {
        // Failing-first anchor for the nested 0x6D4 wall (inside the
        // converted TestUserControl ctor's own BAML, blocking the 0x1F75
        // pin): an engine TextBlock child with FontWeight "Bold"
        // (member -108 with converter -222, fw132-verified byte-shape
        // 24 0A 94 FF 04 "Bold" 22 FF, byte-identical to the nested 0x6D4
        // record) — must land as Bold. Seam implementation needs the
        // member-108 grant.
        var window = Load(GridWithBoldTextBlock());
        var grid = Assert.IsType<Grid>(window.Content);
        var text = Assert.IsType<TextBlock>(Assert.Single(grid.LogicalChildren));
        Assert.Equal(FontWeights.Bold, text.FontWeight);
    }

    private static byte[] GridWithBoldTextBlock()
    {
        // Nested 0x6D4 shape (testusercontrol.baml inside the converted
        // TestUserControl ctor; member identity proven by the fw132
        // fixture): TextBlock element (-638) carrying FontWeight
        // (member -108) with converter -222.
        var b = new B();
        b.Raw(Header()).Raw(0x01);
        b.Sized(0x1C, p => p.U16(0).Str("TestApp"));
        b.Sized(0x1D, p => p.U16(0).U16(0).Str("TestApp.MainWindow"));
        b.Raw(0x03).I16(0).Raw(0x00);
        b.Raw(0x2E).I16(MContent);
        b.Raw(0x03).I16(TGrid).Raw(0x00);
        b.Raw(0x2E).I16(MChildren);
        b.Raw(0x03).I16(-638).Raw(0x00);
        b.Sized(0x24, p => p.I16(-108).Str("Bold").I16(CFw));
        b.Raw(0x04);
        b.Raw(0x04).Raw(0x04).Raw(0x02);
        return b.ToArray();
    }

    private static byte[] GridWithOneWayTextBinding()
    {
        // Real 0x1CFF shape (genuine TestApp mainwindow.baml, member
        // identity proven by the fw127 fixture): TextBox element (-639)
        // with complex Text (-114) holding a Binding (-20) with a ctor
        // path plus a Mode "OneWay" simple with EnumConverter (per-file
        // attr; real file uses id 45, fw127 uses id 0 — name/owner match).
        var b = new B();
        b.Raw(Header()).Raw(0x01);
        b.Sized(0x1C, p => p.U16(0).Str("TestApp"));
        b.Sized(0x1D, p => p.U16(0).U16(0).Str("TestApp.MainWindow"));
        b.Sized(0x1F, p => p.U16(40).I16(-20).Raw(0x00).Str("Mode"));
        b.Raw(0x03).I16(0).Raw(0x00);
        b.Raw(0x2E).I16(MContent);
        b.Raw(0x03).I16(TGrid).Raw(0x00);
        b.Raw(0x2E).I16(MChildren);
        b.Raw(0x03).I16(-639).Raw(0x00);
        b.Raw(0x07).I16(-114);
        b.Raw(0x03).I16(-20).Raw(0x00);
        b.Raw(0x2A);
        b.Sized(0x10, p => p.Str("Title"));
        b.Raw(0x2B);
        b.Sized(0x24, p => p.I16(40).Str("OneWay").I16(CEnum));
        b.Raw(0x04);
        b.Raw(0x08);
        b.Raw(0x04).Raw(0x04).Raw(0x04).Raw(0x02);
        return b.ToArray();
    }

    private static byte[] GridWithRelativeSourceTextBinding()
    {
        // Real 0x1CBE-0x1CCE shape (member identity proven by the genuine
        // fw126 fixture): complex TextBox.Text (-114) holding a Binding
        // (-20) with a ctor path plus a complex RelativeSource child
        // (-516, FindAncestor + AncestorType Window + AncestorLevel 1).
        // Per-file member ids stand in for the real file's table
        // (name/owner pairs match; well-known ids are fixed).
        var b = new B();
        b.Raw(Header()).Raw(0x01);
        b.Sized(0x1C, p => p.U16(0).Str("TestApp"));
        b.Sized(0x1D, p => p.U16(0).U16(0).Str("TestApp.MainWindow"));
        b.Sized(0x1F, p => p.U16(40).I16(-20).Raw(0x00).Str("RelativeSource"));
        b.Sized(0x1F, p => p.U16(41).I16(-516).Raw(0x00).Str("AncestorType"));
        b.Sized(0x1F, p => p.U16(42).I16(-516).Raw(0x00).Str("AncestorLevel"));
        b.Raw(0x03).I16(0).Raw(0x00);
        b.Raw(0x2E).I16(MContent);
        b.Raw(0x03).I16(TGrid).Raw(0x00);
        b.Raw(0x2E).I16(MChildren);
        b.Raw(0x03).I16(-639).Raw(0x00);
        b.Raw(0x07).I16(-114);
        b.Raw(0x03).I16(-20).Raw(0x00);
        b.Raw(0x2A);
        b.Sized(0x10, p => p.Str("Title"));
        b.Raw(0x2B);
        b.Raw(0x07).I16(40);
        b.Raw(0x03).I16(-516).Raw(0x00);
        b.Raw(0x2A);
        b.Sized(0x10, p => p.Str("FindAncestor"));
        b.Raw(0x2B);
        b.Sized(0x05, p => p.U16(41).Str("Window"));
        b.Sized(0x24, p => p.I16(42).Str("1").I16(CInt));
        b.Raw(0x04);
        b.Raw(0x08);
        b.Raw(0x04);
        b.Raw(0x08);
        b.Raw(0x04).Raw(0x04).Raw(0x04).Raw(0x02);
        return b.ToArray();
    }

    private static byte[] GridWithTransparentTextBox()
    {
        // Real 0x1C12 shape (genuine TestApp mainwindow.baml, member
        // identity proven by the fw124 fixture): TextBox element (-639),
        // Grid.Row "1" with Int32Converter (-63/-315), PropertyCustom
        // Background -24 with packed-ARGB serializer 744 carrying
        // Transparent, Text with StringConverter (-114/-615).
        var b = new B();
        b.Raw(Header()).Raw(0x01);
        b.Sized(0x1C, p => p.U16(0).Str("TestApp"));
        b.Sized(0x1D, p => p.U16(0).U16(0).Str("TestApp.MainWindow"));
        b.Raw(0x03).I16(0).Raw(0x00);
        b.Raw(0x2E).I16(MContent);
        b.Raw(0x03).I16(TGrid).Raw(0x00);
        b.Raw(0x2E).I16(MChildren);
        b.Raw(0x03).I16(-639).Raw(0x00);
        b.Sized(0x24, p => p.I16(-63).Str("1").I16(CInt));
        b.Raw(0x06, 0x0A, 0xE8, 0xFF, 0xE8, 0x02, 0x01, 0xFF, 0xFF, 0xFF, 0x00);
        b.Sized(0x24, p => p.I16(-114).Str("Document 1 Content").I16(CStr));
        b.Raw(0x04);
        b.Raw(0x04).Raw(0x04).Raw(0x02);
        return b.ToArray();
    }

    private static byte[] GridWithDocumentPlaceholderButton()
    {
        // Real 0x1BCA shape (genuine TestApp mainwindow.baml): ElementStart
        // type -55 (Button, fw123-verified), ConnectionId 45, Height "28"
        // with LengthConverter, VerticalAlignment "Top" with
        // EnumConverter, Content text with StringConverter.
        var b = new B();
        b.Raw(Header()).Raw(0x01);
        b.Sized(0x1C, p => p.U16(0).Str("TestApp"));
        b.Sized(0x1D, p => p.U16(0).U16(0).Str("TestApp.MainWindow"));
        b.Raw(0x03).I16(0).Raw(0x00);
        b.Raw(0x2E).I16(MContent);
        b.Raw(0x03).I16(TGrid).Raw(0x00);
        b.Raw(0x2E).I16(MChildren);
        b.Raw(0x03).I16(-55).Raw(0x00);
        b.Raw(0x2D, 0x2D, 0x00, 0x00, 0x00);
        b.Sized(0x24, p => p.I16(-47).Str("28").I16(CLen));
        b.Sized(0x24, p => p.I16(-56).Str("Top").I16(CEnum));
        b.Sized(0x24, p => p.I16(-14).Str("Click to add 2 documents").I16(CStr));
        b.Raw(0x04);
        b.Raw(0x04).Raw(0x04).Raw(0x02);
        return b.ToArray();
    }

    private static byte[] GridWithTransparentBackground()
    {
        // Real 0x1BA3 shape (genuine fw122 Background="Transparent",
        // byte-identical at all three real-file sites): PropertyCustom
        // attr -86, serializer 744, 0x01 plus LE ARGB Transparent.
        var b = new B();
        b.Raw(Header()).Raw(0x01);
        b.Sized(0x1C, p => p.U16(0).Str("TestApp"));
        b.Sized(0x1D, p => p.U16(0).U16(0).Str("TestApp.MainWindow"));
        b.Raw(0x03).I16(0).Raw(0x00);
        b.Raw(0x2E).I16(MContent);
        b.Raw(0x03).I16(TGrid).Raw(0x00);
        b.Raw(0x06, 0x0A, 0xAA, 0xFF, 0xE8, 0x02, 0x01, 0xFF, 0xFF, 0xFF, 0x00);
        b.Raw(0x04);
        b.Raw(0x04).Raw(0x02);
        return b.ToArray();
    }

    private static byte[] WindowsFormsHostElement()
    {
        // Real 0x18FF-0x1913 shape: per-file WindowsFormsHost element
        // with ConnectionId 43 and per-file Name PWC "winFormsHost".
        var b = new B();
        b.Raw(Header()).Raw(0x01);
        b.Sized(0x1C, p => p.U16(0).Str("TestApp"));
        b.Sized(0x1C, p => p.U16(1).Str("WindowsFormsIntegration, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35"));
        b.Sized(0x1D, p => p.U16(0).U16(0).Str("TestApp.MainWindow"));
        b.Sized(0x1D, p => p.U16(1).U16(1).Str("System.Windows.Forms.Integration.WindowsFormsHost"));
        b.Sized(0x1F, p => p.U16(8).I16(1).Raw(0x00).Str("Name"));
        b.Raw(0x03).I16(0).Raw(0x00);
        b.Raw(0x2E).I16(MContent);
        b.Raw(0x03).I16(TGrid).Raw(0x00);
        b.Raw(0x2E).I16(MChildren);
        b.Raw(0x03).I16(1).Raw(0x00);
        b.Raw(0x2D, 0x2B, 0x00, 0x00, 0x00);
        b.Sized(0x24, p => p.I16(8).Str("winFormsHost").I16(CStr));
        b.Raw(0x04);
        b.Raw(0x04).Raw(0x04).Raw(0x02);
        return b.ToArray();
    }

    private static byte[] DockingManagerWithNamedLayoutModel()
    {
        var b = new B();
        b.Raw(Header()).Raw(0x01);
        b.Sized(0x1C, p => p.U16(0).Str("TestApp"));
        b.Sized(0x1C, p => p.U16(1).Str("AvalonDock, Version=5.0.2.0, Culture=neutral, PublicKeyToken=null"));
        b.Sized(0x1D, p => p.U16(0).U16(0).Str("TestApp.MainWindow"));
        b.Sized(0x1D, p => p.U16(1).U16(1).Str("AvalonDock.DockingManager"));
        b.Sized(0x1D, p => p.U16(2).U16(1).Str("AvalonDock.Layout.LayoutRoot"));
        b.Sized(0x1D, p => p.U16(3).U16(1).Str("AvalonDock.Layout.LayoutPanel"));
        b.Sized(0x1F, p => p.U16(8).I16(1).Raw(0x00).Str("Layout"));
        b.Sized(0x1F, p => p.U16(9).I16(2).Raw(0x00).Str("RootPanel"));
        b.Raw(0x03).I16(0).Raw(0x00);
        b.Raw(0x2E).I16(MContent);
        b.Raw(0x03).I16(TGrid).Raw(0x00);
        b.Raw(0x2E).I16(MChildren);
        b.Raw(0x03).I16(1).Raw(0x00);
        b.Raw(0x2E).I16(8);
        b.Raw(0x03).I16(2).Raw(0x00);
        // Real 0x17D6 shape: ConnectionId 42, then DefAttribute x:Name
        // "WinFormsWindow" (sized, 7-bit name, U16 0xFFFF).
        b.Raw(0x2D, 0x2A, 0x00, 0x00, 0x00);
        b.Sized(0x19, p => p.Str("WinFormsWindow").U16(0xFFFF));
        b.Raw(0x2E).I16(9);
        b.Raw(0x03).I16(3).Raw(0x00);
        b.Raw(0x04);
        b.Raw(0x04);
        b.Raw(0x04).Raw(0x04).Raw(0x04).Raw(0x02);
        return b.ToArray();
    }

    private static byte[] DockingManagerWithLayoutModel()
    {
        var b = new B();
        b.Raw(Header()).Raw(0x01);
        b.Sized(0x1C, p => p.U16(0).Str("TestApp"));
        b.Sized(0x1C, p => p.U16(1).Str("AvalonDock, Version=5.0.2.0, Culture=neutral, PublicKeyToken=null"));
        b.Sized(0x1D, p => p.U16(0).U16(0).Str("TestApp.MainWindow"));
        b.Sized(0x1D, p => p.U16(1).U16(1).Str("AvalonDock.DockingManager"));
        b.Sized(0x1D, p => p.U16(2).U16(1).Str("AvalonDock.Layout.LayoutRoot"));
        b.Sized(0x1D, p => p.U16(3).U16(1).Str("AvalonDock.Layout.LayoutPanel"));
        b.Sized(0x1F, p => p.U16(8).I16(1).Raw(0x00).Str("Layout"));
        b.Sized(0x1F, p => p.U16(9).I16(2).Raw(0x00).Str("RootPanel"));
        b.Raw(0x03).I16(0).Raw(0x00);
        b.Raw(0x2E).I16(MContent);
        b.Raw(0x03).I16(TGrid).Raw(0x00);
        b.Raw(0x2E).I16(MChildren);
        b.Raw(0x03).I16(1).Raw(0x00);
        b.Raw(0x2E).I16(8);
        b.Raw(0x03).I16(2).Raw(0x00);
        b.Raw(0x2E).I16(9);
        b.Raw(0x03).I16(3).Raw(0x00);
        b.Raw(0x04);
        b.Raw(0x04);
        b.Raw(0x04).Raw(0x04).Raw(0x04).Raw(0x02);
        return b.ToArray();
    }

    private static byte[] DockingManagerWithContentValue()
    {
        var b = new B();
        b.Raw(Header()).Raw(0x01);
        b.Sized(0x1C, p => p.U16(0).Str("TestApp"));
        b.Sized(0x1C, p => p.U16(1).Str("AvalonDock"));
        b.Sized(0x1C, p => p.U16(2).Str("AvalonDock.Themes.Arc, Version=5.0.2.0, Culture=neutral, PublicKeyToken=null"));
        b.Sized(0x1D, p => p.U16(0).U16(0).Str("TestApp.MainWindow"));
        b.Sized(0x1D, p => p.U16(1).U16(1).Str("AvalonDock.DockingManager"));
        b.Sized(0x1D, p => p.U16(2).U16(2).Str("AvalonDock.Themes.ArcDarkTheme"));
        b.Sized(0x1F, p => p.U16(8).I16(1).Raw(0x00).Str("Theme"));
        b.Raw(0x03).I16(0).Raw(0x00);
        b.Raw(0x2E).I16(MContent);
        b.Raw(0x03).I16(TGrid).Raw(0x00);
        b.Raw(0x2E).I16(MChildren);
        b.Raw(0x03).I16(1).Raw(0x00);
        b.Raw(0x2E).I16(MThemeAttr);
        b.Raw(0x03).I16(2).Raw(0x00);
        b.Raw(0x04);
        b.Raw(0x04).Raw(0x04).Raw(0x04).Raw(0x02);
        return b.ToArray();
    }

    private static byte[] DockingManagerWithWellknownTemplate()
    {
        var b = new B();
        b.Raw(Header()).Raw(0x01);
        b.Sized(0x1C, p => p.U16(0).Str("TestApp"));
        b.Sized(0x1C, p => p.U16(1).Str("AvalonDock"));
        b.Sized(0x1D, p => p.U16(0).U16(0).Str("TestApp.MainWindow"));
        b.Sized(0x1D, p => p.U16(1).U16(1).Str("AvalonDock.DockingManager"));
        b.Sized(0x1F, p => p.U16(0).I16(1).Raw(0x00).Str("Theme"));
        b.Raw(0x03).I16(0).Raw(0x00);
        b.Raw(0x2E).I16(MContent);
        b.Raw(0x03).I16(TGrid).Raw(0x00);
        b.Raw(0x2E).I16(MChildren);
        b.Raw(0x03).I16(1).Raw(0x00);
        b.Raw(0x07).I16(0);
        b.Raw(0x03).I16(TDataTemplate).Raw(0x00);
        b.Raw(0x04);
        b.Raw(0x08);
        b.Raw(0x04).Raw(0x04).Raw(0x04).Raw(0x02);
        return b.ToArray();
    }
    private static byte[] DockingManagerWithTheme()
    {
        var b = new B();
        b.Raw(Header()).Raw(0x01);
        b.Sized(0x1C, p => p.U16(0).Str("TestApp"));
        b.Sized(0x1C, p => p.U16(1).Str("AvalonDock"));
        b.Sized(0x1C, p => p.U16(2).Str("AvalonDock.Themes.Arc"));
        b.Sized(0x1D, p => p.U16(0).U16(0).Str("TestApp.MainWindow"));
        b.Sized(0x1D, p => p.U16(1).U16(1).Str("AvalonDock.DockingManager"));
        b.Sized(0x1D, p => p.U16(2).U16(2).Str("AvalonDock.Themes.ArcDarkTheme"));
        b.Sized(0x1F, p => p.U16(0).I16(1).Raw(0x00).Str("Theme"));
        b.Raw(0x03).I16(0).Raw(0x00);
        b.Raw(0x2E).I16(MContent);
        b.Raw(0x03).I16(TGrid).Raw(0x00);
        b.Raw(0x2E).I16(MChildren);
        b.Raw(0x03).I16(1).Raw(0x00);
        b.Raw(0x07).I16(0);
        b.Raw(0x03).I16(2).Raw(0x00);
        b.Raw(0x04);
        b.Raw(0x08);
        b.Raw(0x04).Raw(0x04).Raw(0x04).Raw(0x02);
        return b.ToArray();
    }

    private static byte[] DockingManagerWithAttribute(string name, string value, short conv)
    {
        var b = new B();
        b.Raw(Header()).Raw(0x01);
        b.Sized(0x1C, p => p.U16(0).Str("TestApp"));
        b.Sized(0x1C, p => p.U16(1).Str("AvalonDock"));
        b.Sized(0x1D, p => p.U16(0).U16(0).Str("TestApp.MainWindow"));
        b.Sized(0x1D, p => p.U16(1).U16(1).Str("AvalonDock.DockingManager"));
        b.Sized(0x1F, p => p.U16(0).I16(1).Raw(0x00).Str(name));
        b.Raw(0x03).I16(0).Raw(0x00);
        b.Raw(0x2E).I16(MContent);
        b.Raw(0x03).I16(TGrid).Raw(0x00);
        b.Raw(0x2E).I16(MChildren);
        b.Raw(0x03).I16(1).Raw(0x00);
        b.Sized(0x24, p => p.I16(0).Str(value).I16(conv));
        b.Raw(0x04).Raw(0x04).Raw(0x04).Raw(0x02);
        return b.ToArray();
    }

    private static IEnumerable<UIElement> Descendants(UIElement element)
    {
        yield return element;
        if (element is FrameworkElement framework)
            foreach (var child in framework.LogicalChildren)
                foreach (var item in Descendants(child)) yield return item;
    }

    private static byte[] LoadRealMainWindowBaml()
    {
        var testApp = AppDomain.CurrentDomain.GetAssemblies()
            .First(a => string.Equals(a.GetName().Name, "TestApp", StringComparison.OrdinalIgnoreCase));
        using var stream = testApp.GetManifestResourceStream("TestApp.g.resources");
        Assert.NotNull(stream);
        using var reader = new System.Resources.ResourceReader(stream!);
        byte[]? found = null;
        foreach (System.Collections.DictionaryEntry entry in reader)
        {
            if (((string)entry.Key).EndsWith("mainwindow.baml", StringComparison.OrdinalIgnoreCase))
            {
                using var copy = new MemoryStream();
                ((Stream)entry.Value!).CopyTo(copy);
                found = copy.ToArray();
            }
        }
        Assert.NotNull(found);
        return found!;
    }

    private static IEnumerable<Type> SafeTypes(Assembly asm)
    {
        try { return asm.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t is not null)!; }
    }

        private static object? FindLayoutAnchorable(UIElement root, string contentId)
    {
        // Boot witness through the converted-model slots: the WinForms host
        // lives in AvalonDock layout content, not under
        // FrameworkElement.LogicalChildren. Public surface plus reflection
        // only, cycle-guarded, depth-capped, early exit on hit.
        return FindInLayoutModel(root, node =>
            node.GetType().FullName == "AvalonDock.Layout.LayoutAnchorable"
            && string.Equals(node.GetType().GetProperty("ContentId")?.GetValue(node) as string, contentId, StringComparison.Ordinal));
    }

    private static object? FindInLayoutModel(UIElement root, Func<object, bool> match)
    {
        foreach (var manager in Descendants(root).Where(node => node.GetType().FullName == "AvalonDock.DockingManager"))
        {
            var hit = WalkLayoutModel(manager.GetType().GetProperty("Layout")?.GetValue(manager), match, new HashSet<object>(ReferenceEqualityComparer.Instance), 0);
            if (hit is not null) return hit;
        }
        return null;
    }

    private static object? WalkLayoutModel(object? node, Func<object, bool> match, HashSet<object> seen, int depth)
    {
        // Depth cap plus a narrow FrameworkElement rule (LogicalChildren and
        // ContentControl.Content only): the full FrameworkElement property
        // surface is enormous and fork-backed members must never be touched
        // by a witness walk, so generic property reflection applies to
        // converted-model nodes only.
        if (node is null || depth > 40 || !seen.Add(node)) return null;
        if (match(node)) return node;
        if (node is FrameworkElement framework)
        {
            foreach (var child in framework.LogicalChildren)
            {
                var hit = WalkLayoutModel(child, match, seen, depth + 1);
                if (hit is not null) return hit;
            }
            if (framework is ContentControl contentControl && contentControl.Content is object content)
            {
                var hit = WalkLayoutModel(content, match, seen, depth + 1);
                if (hit is not null) return hit;
            }
            return null;
        }
        if (node is System.Collections.IEnumerable sequence && node is not string)
        {
            foreach (var item in sequence)
            {
                var hit = WalkLayoutModel(item, match, seen, depth + 1);
                if (hit is not null) return hit;
            }
            return null;
        }
        var type = node.GetType();
        if (type.Assembly.GetName().Name?.StartsWith("System", StringComparison.Ordinal) == true) return null;
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetIndexParameters().Length > 0 || !property.CanRead) continue;
            if (property.PropertyType.IsPrimitive || property.PropertyType == typeof(string) || property.PropertyType == typeof(decimal)) continue;
            object? value;
            try { value = property.GetValue(node); }
            catch { continue; }
            var hit = WalkLayoutModel(value, match, seen, depth + 1);
            if (hit is not null) return hit;
        }
        return null;
    }
    private static readonly object JointGate = new();
    private static bool _jointLoaded;

    private static void LoadJointPackage()
    {
        lock (JointGate)
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(System.IO.Path.Combine(
                directory.FullName, "inputs", "mixed", "testapp-package", "app-manifest.json")))
                directory = directory.Parent;
            Assert.NotNull(directory);
            var apps = System.IO.Path.Combine(directory!.FullName, "inputs", "mixed", "testapp-package");
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
            if (!_jointLoaded)
            {
                AppDomain.CurrentDomain.AssemblyResolve += (_, args) =>
                {
                    var requested = new AssemblyName(args.Name).Name;
                    var loaded = AppDomain.CurrentDomain.GetAssemblies()
                        .FirstOrDefault(a => string.Equals(a.GetName().Name, requested, StringComparison.OrdinalIgnoreCase));
                    if (loaded is not null) return loaded;
                    if (requested is not null && images.TryGetValue(requested, out var image))
                        return Assembly.Load(image);
                    if (string.Equals(requested, "WindowsFormsIntegration", StringComparison.OrdinalIgnoreCase))
                        return RealBridgeAssembly();
                    var bridgeDir = System.IO.Path.GetDirectoryName(RealBridgeAssembly().Location);
                    if (bridgeDir != null && requested != null)
                    {
                        var candidate = System.IO.Path.Combine(bridgeDir, requested + ".dll");
                        if (System.IO.File.Exists(candidate))
                            return Assembly.LoadFrom(candidate);
                    }
                    return null;
                };
                foreach (var image in images.Values)
                {
                    try { Assembly.Load(image); } catch { }
                }
                _jointLoaded = true;
            }
        }
    }
}




