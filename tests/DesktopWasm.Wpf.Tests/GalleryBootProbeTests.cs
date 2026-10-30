using System.Text.Json;
using DesktopWasm.Core;

namespace DesktopWasm.Wpf.Tests;

// TEMPORARY probe: boots the real converted gallery package in-process to
// record the first boot boundary. Deleted before the slice lands.
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class GalleryBootProbeTests : IDisposable
{
    private readonly Application? _previous = Application.Current;

    public void Dispose() => Application.Current = _previous;

    [Fact]
    public async Task ProbeGalleryBootBoundary()
    {
        var dir = @"E:\AI\Code\WPF-wasm\src\DesktopWasm.BrowserHost\wwwroot\apps\wpf-gallery";
        var json = File.ReadAllText(System.IO.Path.Combine(dir, "app-manifest.json"));
        var package = JsonSerializer.Deserialize<ApplicationPackage>(json,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        Assert.NotNull(package);
        using var loader = await ApplicationPackageLoader.CreateAsync(package!, "wpf-gallery",
            file => Task.FromResult(File.ReadAllBytes(System.IO.Path.Combine(dir, file))));
        var window = loader.Boot();
        Assert.NotNull(window);
        var lines = new List<string>
        {
            $"TITLE={window.Title} SIZE={window.Width}x{window.Height} CONTENT={window.Content?.GetType().FullName ?? "<none>"}",
        };
        try
        {
            var app = Application.Current!;
            var asm2 = window.GetType().Assembly;
            var ceType = asm2.GetType("WPFGallery.Controls.ControlExample");
            lines.Add("CETYPE=" + (ceType?.FullName ?? "<none>"));
            var merged = 0;
            var res = app.Resources;
            while (res is not null) { merged += res.MergedDictionaries.Count; break; }
            lines.Add("APPMERGED=" + merged);
            if (ceType is not null)
            {
                lines.Add("IMPLICIT=" + (res.TryGetValue(ceType, out var st) ? st?.GetType().Name ?? "<null>" : "<missing>"));
            }
            var examples = FindVisualsByName(window.Content as FrameworkElement, "WPFGallery.Controls.ControlExample");
            lines.Add("EXAMPLES=" + examples.Count);
            foreach (var ex in examples.Take(2))
            {
                var templateProp = ex.GetType().GetProperty("Template")?.GetValue(ex);
                lines.Add("EXSTYLE=" + (ex.Style is null ? "<null>" : ex.Style.GetType().Name) +
                    " TEMPLATE=" + (templateProp is null ? "<null>" : "set") +
                    " EXCONTENT=" + (ex.GetType().GetProperty("ExampleContent")?.GetValue(ex)?.GetType().Name ?? "<null>"));
            }
        }
        catch (Exception exStyle) { lines.Add("STYLEPROBE-FAIL " + exStyle.GetBaseException().Message); }
        Dump(window.Content as FrameworkElement, 0, lines);
        File.WriteAllLines(System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(typeof(GalleryBootProbeTests).Assembly.Location)!,
            "gallery-boot-tree.txt"), lines);

        // Sweep every embedded loose-XAML page through the loader, the same
        // source WpfAppBoot.Hydrate reads. Catches each parse boundary at
        // once; the browser then proves paint + input on the real path.
        var asm = window.GetType().Assembly;
        var names = asm.GetManifestResourceNames();
        File.WriteAllLines(System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(typeof(GalleryBootProbeTests).Assembly.Location)!,
            "gallery-resources.txt"), names);
        var previous = XamlLoader.CustomElementSource;
        XamlLoader.CustomElementSource = asm;
        try
        {
            var report = new List<string>();
            foreach (var name in names.Where(n => n.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase)).OrderBy(n => n))
            {
                using var stream = asm.GetManifestResourceStream(name)!;
                using var reader = new StreamReader(stream);
                try
                {
                    var root = XamlLoader.Load(reader.ReadToEnd());
                    report.Add("XAML-OK " + name);
                    if (name.EndsWith("Views/BasicInput/ButtonPage.xaml"))
                    {
                        var counts = new System.Collections.Generic.Dictionary<string, int>();
                        CountTree(root.Root as FrameworkElement, counts);
                        foreach (var kv in counts.OrderBy(kv => kv.Key))
                            report.Add("BTNTREE " + kv.Key + "=" + kv.Value);
                    }
                }
                catch (Exception ex)
                {
                    report.Add("XAML-FAIL " + name + " :: " + ex.GetBaseException().GetType().Name + ": " + FirstLine(ex.GetBaseException().Message));
                }
                if (name.EndsWith("WhatsNewPage.xaml") && !System.IO.File.Exists(System.IO.Path.Combine(
                    System.IO.Path.GetDirectoryName(typeof(GalleryBootProbeTests).Assembly.Location)!,
                    "gallery-nav.txt")))
                {
                    var nav = new List<string>();
                    var tree = FindVisual<TreeView>(window.Content as FrameworkElement);
                    nav.Add("TREE=" + (tree is null ? "<none>" : tree.GetType().FullName));
                    if (tree is not null)
                    {
                        var nodes = FindVisuals<TreeViewItem>(window.Content as FrameworkElement);
                        nav.Add("NODES=" + nodes.Count);
                        var basicNode = nodes.FirstOrDefault(t => HeaderText(t).Contains("Basic Input"));
                        nav.Add("BASICNODE=" + (basicNode is null ? "<none>" : HeaderText(basicNode)));
                        if (basicNode is not null)
                        {
                            var items = ((ItemsControl)tree).Items;
                            var basicData = items.Cast<object?>().FirstOrDefault(d => d?.ToString()?.Contains("Basic Input") == true);
                            nav.Add("BASICDATA=" + (basicData?.ToString() ?? "<none>"));
                            if (basicData is not null)
                            {
                                tree.SelectedItem = basicData;
                                nav.Add("DATASEL-OK selected=" + (tree.SelectedItem?.ToString() ?? "<null>") +
                                    " frame=" + (FindVisual<Frame>(window.Content as FrameworkElement)?.Content?.GetType().FullName ?? "<noframe>"));
                            }
                            basicNode.IsSelected = true;
                            nav.Add("SEL-OK selected=" + (tree.SelectedItem?.ToString() ?? "<null>"));
                            var catFrame = FindVisual<Frame>(window.Content as FrameworkElement);
                            nav.Add("CATFRAME=" + (catFrame is null ? "<none>" : catFrame.Content?.GetType().FullName ?? "<empty>"));
                            var catCard = FindVisuals<Button>(catFrame?.Content as FrameworkElement)
                                .FirstOrDefault(b => (b.CommandParameter?.ToString() ?? "").Contains("BasicInputPage"));
                            nav.Add("CATCARD=" + (catCard is null ? "<none>" : catCard.CommandParameter?.ToString() + " cmd=" + (catCard.Command?.GetType().FullName ?? "<none>")));
                            if (catCard?.Command is not null && catFrame is not null)
                            {
                                try
                                {
                                    catCard.Command.Execute(catCard.CommandParameter);
                                    nav.Add("CATEXEC-OK frame=" + (catFrame.Content?.GetType().FullName ?? "<empty>"));
                                    var sub = FindVisuals<Button>(catFrame.Content as FrameworkElement).ToList();
                                    nav.Add("SUBBUTTONS=" + sub.Count);
                                    foreach (var b in sub.Take(12))
                                        nav.Add("SUB cmd=" + (b.Command?.GetType().Name ?? "<none>") +
                                            " param=" + (b.CommandParameter?.ToString() ?? "<none>"));
                                    var detail = sub.FirstOrDefault(b => (b.CommandParameter?.ToString() ?? "").Contains("ButtonPage"));
                                    if (detail?.Command is not null)
                                    {
                                        try
                                        {
                                            window.Measure(new WpfSize(800, 600));
                                            window.Arrange(new WpfRect(0, 0, 800, 600));
                                            var gen = tree.ItemContainerGenerator;
                                            var btnData = items.Cast<object?>().SelectMany(d => ChildData(tree, d)).FirstOrDefault(d => d?.ToString()?.Contains("Button") == true);
                                            nav.Add("BTNDATA=" + (btnData?.ToString() ?? "<none>"));
                                            nav.Add("BTNCONTAINER=" + (btnData is null ? "<nodata>" : (gen.ContainerFromItem(btnData)?.GetType().Name ?? "<null>")));
                                            var btnNode = nodes.FirstOrDefault(t => HeaderText(t) == "Button");
                                            nav.Add("BTNVISUAL=" + (btnNode is null ? "<none>" : "found"));
                                            nav.Add("REFEQ=" + (btnData is null || btnNode is null ? "<na>" : ReferenceEquals(gen.ContainerFromItem(btnData), btnNode).ToString()));
                                            nav.Add("NESTHIT=" + (btnData is null ? "<na>" : basicNode.ItemContainerGenerator.TryGetContainer(btnData, out _).ToString()));
                                            nav.Add("TOPHIT=" + ((DesktopWasm.Wpf.TreeViewContainerGenerator)gen).TryResolveNode(basicData, out _).ToString());
                                            nav.Add("NESTRESOLVE=" + ((DesktopWasm.Wpf.TreeViewContainerGenerator)gen).TryResolveNode(btnData, out var nestNode).ToString());
                                            nav.Add("NESTREFEQ=" + (nestNode is null || btnNode is null ? "<na>" : ReferenceEquals(nestNode, btnNode).ToString()));
                                            detail.Command.Execute(detail.CommandParameter);
                                            nav.Add("SUBEXEC-OK frame=" + (catFrame.Content?.GetType().FullName ?? "<empty>"));
                                            nav.Add("SELNOW=" + (tree.SelectedItem?.ToString() ?? "<null>"));
                                        }
                                        catch (Exception ex6) { nav.Add("SUBEXEC-FAIL " + ex6.GetBaseException().ToString()); }
                                    }
                                    else nav.Add("SUBCARD=" + (detail is null ? "none" : "nullcmd"));
                                }
                                catch (Exception ex5) { nav.Add("CATEXEC-FAIL " + ex5.GetBaseException().ToString()); }
                            }
                            else nav.Add("CATCOMMAND=" + (catCard is null ? "nocard" : "null"));
                        }
                        var frame = FindVisual<Frame>(window.Content as FrameworkElement);
                        nav.Add("FRAME=" + (frame is null ? "<none>" : frame.Content?.GetType().FullName ?? "<empty>"));
                        if (frame?.Content is FrameworkElement pageContent)
                        {
                            var ces = FindVisualsByName(pageContent, "WPFGallery.Controls.ControlExample");
                            nav.Add("CES=" + ces.Count);
                            foreach (var ce in ces.Take(2))
                            {
                                var ceTemplate = ce.GetType().GetProperty("Template")?.GetValue(ce);
                                var ceEx = ce.GetType().GetProperty("ExampleContent")?.GetValue(ce);
                                nav.Add("CE style=" + (ce.Style is null ? "<null>" : "set") +
                                    " template=" + (ceTemplate is null ? "<null>" : "set") +
                                    " ex=" + (ceEx?.GetType().Name ?? "<null>") +
                                    " logical=" + ce.LogicalChildren.Count +
                                    " visual=" + VisualTreeHelper.GetChildrenCount(ce));
                            }
                            var phs = FindVisualsByName(pageContent, "WPFGallery.Controls.PageHeader");
                            nav.Add("PHS=" + phs.Count);
                            foreach (var ph in phs.Take(1))
                                nav.Add("PH style=" + (ph.Style is null ? "<null>" : "set") +
                                    " logical=" + ph.LogicalChildren.Count +
                                    " visual=" + VisualTreeHelper.GetChildrenCount(ph));
                            foreach (var ce in ces.Take(1))
                            {
                                var ceEx = ce.GetType().GetProperty("ExampleContent")?.GetValue(ce);
                                if (ceEx is FrameworkElement ceExFe) ceExFe.Measure(new WpfSize(600, 600));
                                nav.Add("CEEXROOT=" + (ceEx is FrameworkElement ? string.Join(",", CollectLayout(ceEx as FrameworkElement).Take(6)) : "<" + (ceEx?.GetType().Name ?? "null") + ">"));
                                var cp = FindPresenter(ce);
                                nav.Add("CP=" + (cp is null ? "<none>" : cp.Content?.GetType().Name ?? "<null-content>") +
                                    " entries=" + (ce.GetType().GetProperty("ActiveEntries")?.GetValue(ce) is System.Collections.IList l ? l.Count.ToString() : "?"));
                                var vroot = VisualTreeHelper.GetChildrenCount(ce) > 0 ? VisualTreeHelper.GetChild(ce, 0) : null;
                                nav.Add("CEVROOT=" + (vroot?.GetType().Name ?? "<none>") +
                                    " vkids=" + (vroot is null ? -1 : VisualTreeHelper.GetChildrenCount(vroot)));
                                nav.Add("CEVTEXT=" + string.Join("|", CollectVisualText(ce).Take(8)));
                                pageContent.Measure(new WpfSize(800, 600));
                                pageContent.Arrange(new WpfRect(0, 0, 800, 600));
                                nav.Add("PAGEDESIRED=" + pageContent.DesiredSize.Width + "x" + pageContent.DesiredSize.Height);
                                nav.Add("LAYOUT=" + string.Join("|", CollectLayout(pageContent).Take(26)));
                            }
                        }
                        if (frame is not null)
                        {
                            var buttons = FindVisuals<Button>(frame.Content as FrameworkElement).ToList();
                            nav.Add("BUTTONS=" + buttons.Count);
                            foreach (var b in buttons.Take(35))
                                nav.Add("BTN cmd=" + (b.Command?.GetType().Name ?? "<none>") +
                                    " param=" + (b.CommandParameter?.ToString() ?? "<none>") +
                                    " content=" + (b.Content?.ToString() ?? "<none>"));
                            var card = buttons.FirstOrDefault(b => (b.CommandParameter?.ToString() ?? "").Contains("ButtonPage"));
                            nav.Add("CARD=" + (card is null ? "<none>" : card.CommandParameter?.ToString()));
                            if (card?.Command is not null)
                            {
                                try
                                {
                                    card.Command.Execute(card.CommandParameter);
                                    nav.Add("EXEC-OK frame=" + (frame.Content?.GetType().FullName ?? "<empty>"));
                                }
                                catch (Exception ex4) { nav.Add("EXEC-FAIL " + ex4.GetBaseException().ToString()); }
                            }
                            else nav.Add("COMMAND=" + (card is null ? "nocard" : "null"));
                        }
                    }
                    File.WriteAllLines(System.IO.Path.Combine(
                        System.IO.Path.GetDirectoryName(typeof(GalleryBootProbeTests).Assembly.Location)!,
                        "gallery-nav.txt"), nav);
                }
                if (name.EndsWith("SettingsPage.xaml") && !System.IO.File.Exists(System.IO.Path.Combine(
                    System.IO.Path.GetDirectoryName(typeof(GalleryBootProbeTests).Assembly.Location)!,
                    "gallery-isolated.txt")))
                {
                    var iso = new List<string>();
                    foreach (var snippet in new[]
                    {
                        "<Image Width=\"20\" Height=\"20\" Source=\"pack://application:,,,/Assets/AppIcons/WPFGallery_48px.png\" />",
                        "<ComboBox SelectedIndex=\"2\"><ComboBoxItem Content=\"Light\" /></ComboBox>",
                        "<Expander><Expander.Header><Grid>" + System.IO.File.ReadAllText("C:/Users/razva/AppData/Local/Temp/hdr_img.xml") + "</Grid></Expander.Header><Expander.Content><TextBlock Text=\"x\" /></Expander.Content></Expander>",
                        "<Expander><Expander.Header><Grid>" + System.IO.File.ReadAllText("C:/Users/razva/AppData/Local/Temp/hdr_stack.xml") + "</Grid></Expander.Header><Expander.Content><TextBlock Text=\"x\" /></Expander.Content></Expander>",
                        "<Expander><Expander.Header><Grid><Grid.ColumnDefinitions><ColumnDefinition Width=\"Auto\" /><ColumnDefinition Width=\"*\" /><ColumnDefinition Width=\"Auto\" /></Grid.ColumnDefinitions>" + System.IO.File.ReadAllText("C:/Users/razva/AppData/Local/Temp/hdr_img.xml") + System.IO.File.ReadAllText("C:/Users/razva/AppData/Local/Temp/hdr_stack.xml") + "</Grid></Expander.Header><Expander.Content><TextBlock Text=\"x\" /></Expander.Content></Expander>",
                    })
                    {
                        try { XamlLoader.Load(snippet); iso.Add("ISO-OK " + snippet[..40]); }
                        catch (Exception ex2) { iso.Add("ISO-FAIL " + snippet[..40] + " :: " + ex2.GetBaseException().GetType().Name + ": " + FirstLine(ex2.GetBaseException().Message)); }
                    }
                    File.WriteAllLines(System.IO.Path.Combine(
                        System.IO.Path.GetDirectoryName(typeof(GalleryBootProbeTests).Assembly.Location)!,
                        "gallery-isolated.txt"), iso);
                }
            }
            File.WriteAllLines(System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(typeof(GalleryBootProbeTests).Assembly.Location)!,
                "gallery-pages.txt"), report);
        }
        finally { XamlLoader.CustomElementSource = previous; }
    }

    private static void CountTree(FrameworkElement? fe, System.Collections.Generic.Dictionary<string, int> counts)
    {
        if (fe is null) return;
        var key = fe.GetType().Name;
        counts[key] = counts.TryGetValue(key, out var n) ? n + 1 : 1;
        foreach (var child in fe.LogicalChildren) CountTree(child as FrameworkElement, counts);
    }

    private static string FirstLine(string message)
    {
        var idx = message.IndexOf('\n');
        return idx < 0 ? message : message[..idx];
    }

    private static T? FindVisual<T>(FrameworkElement? root) where T : FrameworkElement
    {
        if (root is null) return null;
        if (root is T match) return match;
        foreach (var child in root.LogicalChildren)
        {
            var found = FindVisual<T>(child as FrameworkElement);
            if (found is not null) return found;
        }
        return null;
    }

    private static List<FrameworkElement> FindVisualsByName(FrameworkElement? root, string fullName)
    {
        var found = new List<FrameworkElement>();
        CollectByName(root, fullName, found);
        return found;
    }

    private static void CollectByName(FrameworkElement? root, string fullName, List<FrameworkElement> found)
    {
        if (root is null) return;
        if (root.GetType().FullName == fullName) found.Add(root);
        foreach (var child in root.LogicalChildren) CollectByName(child as FrameworkElement, fullName, found);
    }

    private static List<string> CollectVisualText(FrameworkElement? root)
    {
        var found = new List<string>();
        CollectVisualTextInto(root, found);
        return found;
    }

    private static void CollectVisualTextInto(DependencyObject? node, List<string> found)
    {
        if (node is null) return;
        if (node is TextBlock tb && !string.IsNullOrEmpty(tb.Text)) found.Add(tb.Text);
        var count = VisualTreeHelper.GetChildrenCount(node);
        for (var i = 0; i < count; i++) CollectVisualTextInto(VisualTreeHelper.GetChild(node, i), found);
    }

    private static List<string> CollectLayout(FrameworkElement? root)
    {
        var found = new List<string>();
        CollectLayoutInto(root, found);
        return found;
    }

    private static void CollectLayoutInto(DependencyObject? node, List<string> found)
    {
        if (node is null || found.Count >= 40) return;
        if (node is FrameworkElement fe && (node is TextBlock || node is Grid || node is ScrollViewer || node is Button || node is StackPanel || node is Border || node is ContentPresenter || node is CheckBox))
        {
            var extra = node is TextBlock tb ? " '" + (tb.Text.Length > 18 ? tb.Text[..18] : tb.Text) + "'" : "";
            found.Add(node.GetType().Name + extra + "=" + fe.DesiredSize.Width.ToString("0") + "x" + fe.DesiredSize.Height.ToString("0"));
        }
        var count = VisualTreeHelper.GetChildrenCount(node);
        for (var i = 0; i < count; i++) CollectLayoutInto(VisualTreeHelper.GetChild(node, i), found);
    }

    private static ContentPresenter? FindPresenter(FrameworkElement root)
    {
        if (root is ContentPresenter cp) return cp;
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
            if (FindPresenter(VisualTreeHelper.GetChild(root, i) as FrameworkElement) is { } found) return found;
        return null;
    }

    private static List<object?> ChildData(TreeView tree, object? data)
    {
        var found = new List<object?>();
        if (data is null) return found;
        if (tree.ItemContainerGenerator.ContainerFromItem(data) is ItemsControl container)
            found.AddRange(container.Items.Cast<object?>());
        return found;
    }

    private static List<T> FindVisuals<T>(FrameworkElement? root) where T : FrameworkElement
    {
        var found = new List<T>();
        CollectVisuals(root, found);
        return found;
    }

    private static void CollectVisuals<T>(FrameworkElement? root, List<T> found) where T : FrameworkElement
    {
        if (root is null) return;
        if (root is T match) found.Add(match);
        foreach (var child in root.LogicalChildren) CollectVisuals(child as FrameworkElement, found);
    }

    private static string HeaderText(TreeViewItem item)
    {
        if (item.Header is string s) return s;
        if (item.Header is TextBlock tb) return tb.Text;
        if (item.Header is FrameworkElement fe)
        {
            var found = new List<string>();
            CollectText(fe, found);
            if (found.Count > 0) return string.Join(" ", found);
        }
        return item.Header?.ToString() ?? "<null>";
    }

    private static void CollectText(FrameworkElement? fe, List<string> found)
    {
        if (fe is null) return;
        if (fe is TextBlock tb && !string.IsNullOrEmpty(tb.Text)) found.Add(tb.Text);
        foreach (var child in fe.LogicalChildren) CollectText(child as FrameworkElement, found);
    }

    private static void Collect(FrameworkElement? fe, List<TreeViewItem> items)
    {
        if (fe is null) return;
        if (fe is TreeViewItem tvi) items.Add(tvi);
        foreach (var child in fe.LogicalChildren) Collect(child as FrameworkElement, items);
    }

    private static void Dump(FrameworkElement? fe, int depth, List<string> lines)
    {
        if (fe is null || depth > 6) return;
        var extra = fe is ContentControl cc ? $" content-type={cc.Content?.GetType().Name ?? "<none>"}"
            : fe is TextBlock tb ? $" text='{tb.Text}'"
            : "";
        lines.Add($"{new string(' ', depth * 2)}{fe.GetType().FullName}{extra}");
        if (depth == 6) return;
        foreach (var child in fe.LogicalChildren) Dump(child as FrameworkElement, depth + 1, lines);
    }
}
