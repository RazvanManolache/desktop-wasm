using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using DesktopWasm.Core;
using DesktopWasm.Graphics;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Slice-59: PC Space Map boot chain. The App.xaml ComboBox template sets
/// Focusable="False" on its ToggleButton (raw line 109, style-reader line
/// 149) and Popup, ClickMode="Press", PopupAnimation="Slide"; MainWindow
/// needs IsTextSearchEnabled/Closing/Show/ShowActivated plus code-driven
/// OnStartup (Run raises Startup; first shown window becomes MainWindow);
/// ScanWorkspaceControl needs converted TreemapControl event wires,
/// DataGrid column Binding/SortMemberPath, an exact-signature binder for
/// unconstrained EventHandler&lt;T&gt; payloads, and a Color-coercing
/// template-trigger setter path. The staged entry now boots to a live
/// MainWindow; the Kestrel/ASP.NET rows stay a declared conversion
/// boundary (assemblies absent, server never serves).
/// </summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class PcSpaceMapBootTests : IDisposable
{
    private const string PresentationNs = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private const string XamlNs = "http://schemas.microsoft.com/winfx/2006/xaml";

    private readonly Application? _previous = Application.Current;

    public PcSpaceMapBootTests()
    {
        WpfAnimationClock.ResetForTests();
    }

    public void Dispose()
    {
        XamlLoader.CustomElementSource = null;
        Application.Current = _previous;
        WpfAnimationClock.ResetForTests();
    }

    private static Application SeedApp()
    {
        var app = new Application();
        Application.Current = app;
        return app;
    }

    private static (WasmInputRouter Router, WpfInputManager Manager) Input()
    {
        WasmDispatcher.Current = new WasmDispatcher(new WasmDispatcherPump());
        var router = new WasmInputRouter();
        return (router, new WpfInputManager(router));
    }

    private static void Pointer(WasmInputRouter router, double x, double y,
        WasmPointerButton button, bool isDown, WasmPointerKind kind) =>
        router.DispatchPointer(new WasmPointerArgs(new WasmWindowHandle(1), x, y, button, isDown)
            { Kind = kind, PointerId = 1 });

    [Fact]
    public void ToggleButton_focusable_defaults_true_and_parses_false()
    {
        Assert.True(new ToggleButton().Focusable);
        var toggle = new ToggleButton { Focusable = false };
        Assert.False(toggle.Focusable);
        toggle.Focusable = true;
        Assert.True(toggle.Focusable);
    }

    [Fact]
    public void ToggleButton_template_focusable_false_inflates()
    {
        SeedApp();
        var dict = XamlLoader.LoadResourceDictionary(
            "<ResourceDictionary xmlns=\"" + PresentationNs + "\" xmlns:x=\"" + XamlNs + "\">" +
            "<Style x:Key=\"NoFocusToggle\" TargetType=\"{x:Type ToggleButton}\">" +
            "<Setter Property=\"Template\"><Setter.Value>" +
            "<ControlTemplate TargetType=\"{x:Type ToggleButton}\">" +
            "<ToggleButton Focusable=\"False\" ClickMode=\"Press\" Content=\"Go\"/>" +
            "</ControlTemplate></Setter.Value></Setter>" +
            "</Style></ResourceDictionary>");
        var style = Assert.IsType<Style>(dict["NoFocusToggle"]);
        var host = new ToggleButton();
        style.ApplyTo(host);
        host.Measure(new WpfSize(200, 40));
        host.Arrange(new WpfRect(0, 0, 200, 40));
        var part = Assert.IsType<ToggleButton>(host.TemplateRoot);
        Assert.False(part.Focusable);
        Assert.Equal(ClickMode.Press, part.ClickMode);
    }

    [Fact]
    public void ClickMode_press_clicks_on_press_for_button_and_toggle()
    {
        var (router, manager) = Input();
        SeedApp();

        var button = new Button { ClickMode = ClickMode.Press };
        button.Measure(new WpfSize(120, 40));
        button.Arrange(new WpfRect(10, 10, 120, 40));
        manager.AddRoot(button);
        var clicks = 0;
        button.Click += (_, _) => clicks++;
        Pointer(router, 30, 30, WasmPointerButton.Left, true, WasmPointerKind.Down);
        Assert.Equal(1, clicks);
        Pointer(router, 30, 30, WasmPointerButton.Left, false, WasmPointerKind.Up);
        Assert.Equal(1, clicks);

        var toggle = new ToggleButton { ClickMode = ClickMode.Press };
        toggle.Measure(new WpfSize(120, 40));
        toggle.Arrange(new WpfRect(10, 60, 120, 40));
        manager.AddRoot(toggle);
        var toggles = 0;
        toggle.Click += (_, _) => toggles++;
        Pointer(router, 30, 80, WasmPointerButton.Left, true, WasmPointerKind.Down);
        Assert.True(toggle.IsChecked == true);
        Assert.Equal(1, toggles);
        Pointer(router, 30, 80, WasmPointerButton.Left, false, WasmPointerKind.Up);
        Assert.True(toggle.IsChecked == true);
        Assert.Equal(1, toggles);
    }

    [Fact]
    public void ClickMode_release_and_hover_keep_parity()
    {
        var (router, manager) = Input();
        SeedApp();

        var release = new Button();
        release.Measure(new WpfSize(120, 40));
        release.Arrange(new WpfRect(10, 10, 120, 40));
        manager.AddRoot(release);
        var releaseClicks = 0;
        release.Click += (_, _) => releaseClicks++;
        Pointer(router, 30, 30, WasmPointerButton.Left, true, WasmPointerKind.Down);
        Assert.Equal(0, releaseClicks);
        Pointer(router, 30, 30, WasmPointerButton.Left, false, WasmPointerKind.Up);
        Assert.Equal(1, releaseClicks);
        Assert.Equal(ClickMode.Release, new Button().ClickMode);

        var hover = new Button { ClickMode = ClickMode.Hover };
        hover.Measure(new WpfSize(120, 40));
        hover.Arrange(new WpfRect(10, 60, 120, 40));
        manager.AddRoot(hover);
        var hoverClicks = 0;
        hover.Click += (_, _) => hoverClicks++;
        Pointer(router, 300, 300, WasmPointerButton.None, false, WasmPointerKind.Move);
        Assert.Equal(0, hoverClicks);
        Pointer(router, 30, 80, WasmPointerButton.None, false, WasmPointerKind.Move);
        Assert.Equal(1, hoverClicks);
        Pointer(router, 31, 80, WasmPointerButton.None, false, WasmPointerKind.Move);
        Assert.Equal(1, hoverClicks);
    }

    [Fact]
    public void Popup_animation_parses_and_template_child_hosts()
    {
        Assert.Equal(PopupAnimation.Slide, StyleValueConverter.Convert("Slide", typeof(PopupAnimation)));
        Assert.Equal(0, (int)PopupAnimation.None);
        Assert.Equal(1, (int)PopupAnimation.Fade);
        Assert.Equal(2, (int)PopupAnimation.Slide);
        Assert.Equal(3, (int)PopupAnimation.Scroll);

        SeedApp();
        var popup = Assert.IsType<Popup>(XamlLoader.Load(
            "<Popup xmlns=\"" + PresentationNs + "\" Focusable=\"False\" " +
            "AllowsTransparency=\"True\" PopupAnimation=\"Slide\" StaysOpen=\"False\">" +
            "<Border/></Popup>").Root);
        Assert.False(popup.Focusable);
        Assert.True(popup.AllowsTransparency);
        Assert.Equal(PopupAnimation.Slide, popup.PopupAnimation);
        Assert.False(popup.StaysOpen);
        Assert.IsType<Border>(popup.Child);
    }

    [Fact]
    public void ComboBoxItem_highlight_tracks_hover()
    {
        var (router, manager) = Input();
        SeedApp();

        var combo = new ComboBox();
        combo.Items.Add(new ComboBoxItem { Content = "Ada" });
        combo.Items.Add(new ComboBoxItem { Content = "Grace" });
        combo.Measure(new WpfSize(200, 600));
        combo.Arrange(new WpfRect(0, 0, 200, 600));
        manager.AddRoot(combo);
        combo.IsDropDownOpen = true;

        var dropTop = combo.RenderRect.Y + 30;
        Pointer(router, 10, dropTop + 12, WasmPointerButton.None, false, WasmPointerKind.Move);
        var rows = combo.LogicalChildren.OfType<ComboBoxItem>().ToList();
        Assert.Equal(2, rows.Count);
        Assert.True(rows[0].IsHighlighted);
        Assert.False(rows[1].IsHighlighted);

        Pointer(router, 10, dropTop + 36, WasmPointerButton.None, false, WasmPointerKind.Move);
        Assert.False(rows[0].IsHighlighted);
        Assert.True(rows[1].IsHighlighted);

        combo.IsDropDownOpen = false;
        Assert.False(rows[1].IsHighlighted);
    }

    [Fact]
    public void TextBox_selection_brushes_default_and_paint()
    {
        var first = new TextBox();
        var second = new TextBox();
        Assert.Equal(SystemColors.HighlightColor, ((SolidColorBrush)first.SelectionBrush).Color);
        Assert.NotSame(first.SelectionBrush, second.SelectionBrush);
        Assert.Null(first.CaretBrush);
        Assert.Equal(0.4, first.SelectionOpacity);
        Assert.Equal(0x000000FFu, first.CaretFillRgba);

        first.SelectionBrush = new SolidColorBrush(Color.FromRgb(0x1E, 0x5A, 0x7A));
        first.SelectionOpacity = 0.85;
        first.CaretBrush = new SolidColorBrush(Colors.White);
        var expected = BrushRender.ResolveFill(first.SelectionBrush, 0);
        var alpha = (byte)((expected & 0xFF) * 0.85);
        Assert.Equal((expected & 0xFFFFFF00u) | alpha, first.SelectionFillRgba);
        Assert.Equal(new SolidColorBrush(Colors.White).ToRgba(), first.CaretFillRgba);
    }

    [Fact]
    public void Cursor_parses_stock_names_and_rejects_garbage()
    {
        Assert.Same(Cursors.Hand, StyleValueConverter.Convert("Hand", typeof(Cursor)));
        Assert.Same(Cursors.Arrow, StyleValueConverter.Convert("Arrow", typeof(Cursor)));
        Assert.Same(Cursors.Wait, StyleValueConverter.Convert("Wait", typeof(Cursor)));
        Assert.Same(Cursors.IBeam, StyleValueConverter.Convert("ibeam", typeof(Cursor)));
        Assert.Throws<FormatException>(() => StyleValueConverter.Convert("NotACursor", typeof(Cursor)));

        SeedApp();
        var button = Assert.IsType<Button>(XamlLoader.Load(
            "<Button xmlns=\"" + PresentationNs + "\" Cursor=\"Hand\"/>").Root);
        Assert.Same(Cursors.Hand, button.Cursor);
    }

    [Fact]
    public void ContentPresenter_contentsource_mirrors_header()
    {
        SeedApp();
        var dict = XamlLoader.LoadResourceDictionary(
            "<ResourceDictionary xmlns=\"" + PresentationNs + "\" xmlns:x=\"" + XamlNs + "\">" +
            "<Style x:Key=\"HeaderTab\" TargetType=\"{x:Type TabItem}\">" +
            "<Setter Property=\"Template\"><Setter.Value>" +
            "<ControlTemplate TargetType=\"{x:Type TabItem}\">" +
            "<Border><ContentPresenter ContentSource=\"Header\"/></Border>" +
            "</ControlTemplate></Setter.Value></Setter>" +
            "</Style></ResourceDictionary>");
        var tab = new TabItem { Header = "Space map" };
        Assert.IsType<Style>(dict["HeaderTab"]).ApplyTo(tab);
        tab.Measure(new WpfSize(200, 40));
        tab.Arrange(new WpfRect(0, 0, 200, 40));
        var presenter = tab.TemplateRoot is Border border
            ? Assert.IsType<ContentPresenter>(Assert.Single(border.LogicalChildren))
            : Assert.IsType<ContentPresenter>(tab.TemplateRoot);
        Assert.Equal("Space map", presenter.Content);
    }

    [Fact]
    public void DataGrid_chrome_knobs_pin_values_and_layout()
    {
        Assert.Equal(0, (int)DataGridGridLinesVisibility.All);
        Assert.Equal(1, (int)DataGridGridLinesVisibility.Horizontal);
        Assert.Equal(2, (int)DataGridGridLinesVisibility.None);
        Assert.Equal(3, (int)DataGridGridLinesVisibility.Vertical);
        Assert.Equal(0, (int)DataGridHeadersVisibility.None);
        Assert.Equal(1, (int)DataGridHeadersVisibility.Column);
        Assert.Equal(3, (int)DataGridHeadersVisibility.All);
        Assert.Equal(0, (int)DataGridSelectionMode.Single);
        Assert.Equal(1, (int)DataGridSelectionMode.Extended);
        Assert.Equal(1, (int)DataGridSelectionUnit.FullRow);

        var grid = new DataGrid();
        Assert.Equal(DataGridGridLinesVisibility.All, grid.GridLinesVisibility);
        Assert.Equal(DataGridHeadersVisibility.All, grid.HeadersVisibility);
        Assert.True(grid.AutoGenerateColumns);
        Assert.True(grid.CanUserAddRows);
        Assert.False(grid.IsReadOnly);

        grid.AddColumn(new DataGridTextColumn { Header = "Name", BindingPath = "Name", Width = new GridLength(100, GridUnitType.Pixel) });
        grid.Items.Add(new Row("Ada"));
        grid.Items.Add(new Row("Grace"));
        grid.Measure(new WpfSize(400, 400));
        var withHeader = grid.DesiredSize.Height;
        grid.HeadersVisibility = DataGridHeadersVisibility.None;
        grid.Measure(new WpfSize(400, 400));
        Assert.True(grid.DesiredSize.Height < withHeader);

        grid.RowHeight = 32;
        grid.Measure(new WpfSize(400, 400));
        grid.Arrange(new WpfRect(0, 0, 400, 400));
        Assert.Equal(2, grid.RowRects.Count);
        Assert.Equal(32, grid.RowRects[0].Height);

        grid.RowBackground = new SolidColorBrush(Color.FromRgb(0x0D, 0x17, 0x27));
        grid.AlternatingRowBackground = new SolidColorBrush(Color.FromRgb(0x11, 0x1D, 0x2F));
        var commands = WasmMilCore.RenderHeadless(grid, new WpfSize(400, 400));
        var fills = commands.OfType<DrawCommand.FillRect>().Select(f => f.Rgba).ToList();
        Assert.Contains(new SolidColorBrush(Color.FromRgb(0x0D, 0x17, 0x27)).ToRgba(), fills);
        Assert.Contains(new SolidColorBrush(Color.FromRgb(0x11, 0x1D, 0x2F)).ToRgba(), fills);

        grid.GridLinesVisibility = DataGridGridLinesVisibility.None;
        var plain = WasmMilCore.RenderHeadless(grid, new WpfSize(400, 400));
        Assert.DoesNotContain(plain.OfType<DrawCommand.FillRect>(), f => f.Rgba == 0xD0D0D0FF);
    }

    private sealed record Row(string Name);

    [Fact]
    public void DataGrid_double_press_raises_mouse_double_click()
    {
        var (router, manager) = Input();
        SeedApp();

        DataGrid.DoubleClickWindow = TimeSpan.FromHours(1);
        try
        {
            var grid = new DataGrid();
            grid.AddColumn(new DataGridTextColumn { Header = "Name", BindingPath = "Name", Width = new GridLength(200, GridUnitType.Pixel) });
            grid.Items.Add(new Row("Ada"));
            grid.Measure(new WpfSize(300, 200));
            grid.Arrange(new WpfRect(0, 0, 300, 200));
            manager.AddRoot(grid);
            var doubleClicks = 0;
            grid.MouseDoubleClick += (_, _) => doubleClicks++;

            var y = grid.RowRects[0].Y + grid.RowRects[0].Height / 2;
            Pointer(router, 10, y, WasmPointerButton.Left, true, WasmPointerKind.Down);
            Pointer(router, 10, y, WasmPointerButton.Left, false, WasmPointerKind.Up);
            Assert.Equal(0, doubleClicks);
            Pointer(router, 10, y, WasmPointerButton.Left, true, WasmPointerKind.Down);
            Assert.Equal(1, doubleClicks);
        }
        finally
        {
            DataGrid.DoubleClickWindow = TimeSpan.FromMilliseconds(500);
        }
    }

    [Fact]
    public void Window_show_hide_closing_showactivated_parity()
    {
        SeedApp();
        var app = Application.Current!;
        var first = new Window();
        var second = new Window();
        Assert.Null(app.MainWindow);
        first.Show();
        Assert.Same(first, app.MainWindow);
        Assert.True(first.IsVisible);
        Assert.True(first.IsActive);
        second.Show();
        Assert.Same(first, app.MainWindow);

        var quiet = new Window { ShowActivated = false };
        quiet.Show();
        Assert.True(quiet.IsVisible);
        Assert.False(quiet.IsActive);

        var closing = 0;
        var closed = 0;
        first.Closing += (_, e) => { closing++; e.Cancel = true; };
        first.Closed += (_, _) => closed++;
        first.Close();
        Assert.Equal(1, closing);
        Assert.Equal(0, closed);
        Assert.True(first.IsVisible);
    }

    [Fact]
    public void Run_raises_startup_then_throws_sentinel()
    {
        var app = SeedApp();
        var startups = 0;
        app.Startup += (_, e) => { startups++; Assert.NotNull(e); };
        var error = Assert.Throws<InvalidOperationException>(() => app.Run());
        Assert.Equal(1, startups);
        Assert.StartsWith(WpfAppBoot.RunSentinelPrefix, error.Message);
    }

    [Fact]
    public void Staged_app_initializecomponent_succeeds()
    {
        Application.Current = null;
        var entry = LoadStagedPcSpaceMapEntry();
        var previous = XamlLoader.CustomElementSource;
        XamlLoader.CustomElementSource = entry;
        try
        {
            var appType = entry.GetType("PcSpaceMap.App");
            Assert.NotNull(appType);
            var app = Activator.CreateInstance(appType!);
            Assert.NotNull(app);
            appType!.GetMethod("InitializeComponent",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(app, null);
            var application = Assert.IsAssignableFrom<Application>(app);
            Assert.True(application.Resources.Contains("AppBackground"));
        }
        finally
        {
            XamlLoader.CustomElementSource = previous;
            Application.Current = null;
        }
    }

    [Fact]
    public void Staged_mainwindow_constructs_hydrated()
    {
        Application.Current = null;
        var entry = LoadStagedPcSpaceMapEntry();
        var previous = XamlLoader.CustomElementSource;
        XamlLoader.CustomElementSource = entry;
        try
        {
            var appType = entry.GetType("PcSpaceMap.App");
            Assert.NotNull(appType);
            var app = Activator.CreateInstance(appType!);
            Assert.NotNull(app);
            appType!.GetMethod("InitializeComponent",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(app, null);

            var mainType = entry.GetType("PcSpaceMap.MainWindow");
            Assert.NotNull(mainType);
            var window = Activator.CreateInstance(mainType!) as Window;
            Assert.NotNull(window);
            Assert.Equal("PC Space Map", window!.Title);
            Assert.IsType<Grid>(window.Content);

            var quickPath = mainType!.GetField("QuickPathBox",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(window);
            var combo = Assert.IsType<ComboBox>(quickPath);
            Assert.False(combo.IsTextSearchEnabled);
            Assert.False(combo.IsEditable);

            var tabs = mainType.GetField("DiskTabs",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(window);
            Assert.IsType<TabControl>(tabs);

            var workspaceType = entry.GetType("PcSpaceMap.Controls.ScanWorkspaceControl");
            Assert.NotNull(workspaceType);
            var workspace = Activator.CreateInstance(workspaceType!);
            Assert.NotNull(workspace);
            var treemapField = workspaceType!.GetFields(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .FirstOrDefault(f => f.Name == "Treemap");
            Assert.NotNull(treemapField);
            var treemap = treemapField!.GetValue(workspace);
            Assert.NotNull(treemap);
            var nodeSelected = treemap!.GetType().GetEvent("NodeSelected");
            Assert.NotNull(nodeSelected);
            var wired = (Delegate?)treemap.GetType().GetField("NodeSelected",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)?.GetValue(treemap);
            Assert.NotNull(wired);
            Assert.NotEmpty(wired!.GetInvocationList());

            var gridField = workspaceType.GetFields(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .FirstOrDefault(f => f.Name == "LargestFilesGrid");
            Assert.NotNull(gridField);
            var grid = Assert.IsType<DataGrid>(gridField!.GetValue(workspace));
            Assert.False(grid.AutoGenerateColumns);
            var paths = grid.Columns.OfType<DataGridTextColumn>()
                .Select(c => c.Binding?.Path?.Path ?? string.Empty).ToList();
            Assert.Equal(["Name", "SizeText", "TypeText", "ModifiedText", "Path"], paths);
        }
        finally
        {
            XamlLoader.CustomElementSource = previous;
            Application.Current = null;
        }
    }

    [Fact]
    public void Staged_boot_returns_live_mainwindow()
    {
        Application.Current = null;
        var entry = LoadStagedPcSpaceMapEntry();
        try
        {
            var window = WpfAppBoot.Boot(entry);
            Assert.Equal("PcSpaceMap.MainWindow", window.GetType().FullName);
            Assert.Equal("PC Space Map", window.Title);
            Assert.IsType<Grid>(window.Content);
            Assert.True(window.IsVisible);
            Assert.Same(window, Application.Current!.MainWindow);
        }
        finally
        {
            Application.Current = null;
        }
    }

    [Fact]
    public void Staged_manifest_keeps_conversion_failed_14_gap_boundary()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(System.IO.Path.Combine(
            directory.FullName, "src", "DesktopWasm.BrowserHost", "wwwroot",
            "apps", "pc-space-map", "app-manifest.json")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var manifestPath = System.IO.Path.Combine(directory!.FullName, "src",
            "DesktopWasm.BrowserHost", "wwwroot", "apps", "pc-space-map", "app-manifest.json");
        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var root = document.RootElement;
        Assert.Equal("conversion-failed", root.GetProperty("status").GetString());
        var assemblies = root.GetProperty("assemblies").EnumerateArray().ToList();
        Assert.Single(assemblies);
        Assert.Equal("PC Space Map", assemblies[0].GetProperty("name").GetString());
        var gaps = root.GetProperty("gaps").EnumerateArray().ToList();
        Assert.Equal(14, gaps.Count);
        foreach (var gap in gaps)
        {
            Assert.Equal("missing-dependency", gap.GetProperty("kind").GetString());
            Assert.Contains("absent", gap.GetProperty("detail").GetString(), StringComparison.Ordinal);
        }
        Assert.DoesNotContain(gaps, g => g.GetProperty("kind").GetString() == "unmapped-type");
    }

    private static readonly object StagedGate = new();
    private static Assembly? _stagedEntry;

    private static Assembly LoadStagedPcSpaceMapEntry()
    {
        lock (StagedGate)
        {
            var hit = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => string.Equals(a.GetName().Name, "PC Space Map", StringComparison.OrdinalIgnoreCase));
            if (hit is not null)
            {
                VerifyStagedHashes();
                return hit;
            }
            if (_stagedEntry is not null) return _stagedEntry;
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(System.IO.Path.Combine(
                directory.FullName, "src", "DesktopWasm.BrowserHost", "wwwroot",
                "apps", "pc-space-map", "app-manifest.json")))
                directory = directory.Parent;
            Assert.NotNull(directory);
            var apps = System.IO.Path.Combine(directory!.FullName, "src",
                "DesktopWasm.BrowserHost", "wwwroot", "apps", "pc-space-map");
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
                .First(a => string.Equals(a.GetName().Name, "PC Space Map", StringComparison.OrdinalIgnoreCase));
            return _stagedEntry;
        }
    }

    private static void VerifyStagedHashes()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(System.IO.Path.Combine(
            directory.FullName, "src", "DesktopWasm.BrowserHost", "wwwroot",
            "apps", "pc-space-map", "app-manifest.json")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var apps = System.IO.Path.Combine(directory!.FullName, "src",
            "DesktopWasm.BrowserHost", "wwwroot", "apps", "pc-space-map");
        using var document = JsonDocument.Parse(
            File.ReadAllText(System.IO.Path.Combine(apps, "app-manifest.json")));
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
}
