using System.Reflection;
using System.Text.Json;
using DesktopWasm.Wpf;
using Microsoft.JSInterop;

namespace DesktopWasm.BrowserHost;

// Browser attach for the real converted OxyPlot ExampleBrowser (external app,
// slug oxyplot). Called once per session from Home.razor: installs the
// wpfOxyplot status channel so the proof asserts post-input STATE (row/group
// counts, expanded group, selection, CanvasModel, same-instance host Model,
// presenter canvas children) instead of repaint hashes alone. No input
// bridging: Playwright drives the real pointer path through the shared input
// surface.
public static class OxyPlotBrowser
{
    public static async Task WireAsync(string slug, Window window, WasmWpfHost host, IJSRuntime js)
    {
        if (!string.Equals(slug, "oxyplot", StringComparison.OrdinalIgnoreCase)) return;
        try
        {
            await js.InvokeVoidAsync("wpfOxyplot.attach").ConfigureAwait(false);
        }
        catch
        {
        }
        var state = new OxyPlotState(window, host, js);
        _active = state;
        state.PublishAll();
    }

    private static OxyPlotState? _active;

    /// <summary>Re-publishes OxyPlot state on demand (the proof pulls this after layout settles).</summary>
    [JSInvokable]
    public static void RefreshOxyPlotNotes()
    {
        try { _active?.PublishAll(); }
        catch
        {
        }
    }

    private sealed class OxyPlotState
    {
        private readonly Window _window;
        private readonly WasmWpfHost _host;
        private readonly IJSRuntime _js;
        private bool _attached;
        private string _lastSignature = "";
        private bool _layoutPending = true;

        public OxyPlotState(Window window, WasmWpfHost host, IJSRuntime js)
        {
            _window = window;
            _host = host;
            _js = js;
        }

        public void PublishAll()
        {
            var list = FindVisual<ListBox>(_window.Content as FrameworkElement);
            if (!_attached)
            {
                _attached = true;
                if (list is not null) list.SelectionChanged += () => PublishAll();
                _window.Invalidated += _ => PublishOnInvalidated();
            }
            Publish(list);
        }

        private void PublishOnInvalidated()
        {
            // Layout, selection and binding updates surface here; re-publish
            // only when the state signature moved or rects were still empty
            // (first layout), so idle frames cost nothing.
            try
            {
                var list = FindVisual<ListBox>(_window.Content as FrameworkElement);
                var signature = Signature(list);
                if (signature == _lastSignature && !_layoutPending) return;
                Publish(list);
            }
            catch
            {
            }
        }

        private void Publish(ListBox? list)
        {
            var vm = _window.DataContext;
            var groups = list is null
                ? []
                : list.LogicalChildren.OfType<GroupItem>().ToList();
            var rows = list?.Items.Count ?? 0;
            var first = (list is not null && rows > 0) ? list.Items[0].Content ?? "" : "";
            var expanders = groups.Select(g => FindVisual<Expander>(g)).ToList();
            var expandedFirst = expanders.Count > 0 && expanders[0]?.IsExpanded == true;

            var selected = vm is null ? null
                : vm.GetType().GetProperty("SelectedExample")?.GetValue(vm);
            var selectedTitle = selected?.GetType().GetProperty("Title")?.GetValue(selected)?.ToString() ?? "<none>";
            var canvas = vm?.GetType().GetProperty("CanvasModel")?.GetValue(vm);
            var skia = vm?.GetType().GetProperty("SkiaModel")?.GetValue(vm);
            var xamlModel = vm?.GetType().GetProperty("CanvasXamlModel")?.GetValue(vm);
            var code = vm?.GetType().GetProperty("Code")?.GetValue(vm) as string;
            var renderer = vm?.GetType().GetProperty("Renderer")?.GetValue(vm)?.ToString() ?? "";

            var oxyPlot = FindByFullName(_window.Content as FrameworkElement, "OxyPlot.Wpf.PlotView");
            var xamlPlot = FindByFullName(_window.Content as FrameworkElement, "ExampleBrowser.XamlPlotView");
            var oxyModel = oxyPlot?.GetType().GetProperty("Model")?.GetValue(oxyPlot);
            var oxyCanvas = CanvasOf(oxyPlot);
            var (paths, labels) = CanvasInk(oxyCanvas);
            var xamlCanvas = CanvasOf(xamlPlot);
            var xamlModelProp = xamlPlot?.GetType().GetProperty("Model")?.GetValue(xamlPlot);
            var (xamlPaths, xamlLabels) = CanvasInk(xamlCanvas);
            // Slice-52: the converted oxyWpf/Xaml hosts do not derive the
            // engine shim, so the first engine PlotView in this converted
            // tree is the honest-empty oxySkia host (no engine-native
            // XamlPlotView appears here; the loader resolves it converted).
            var skiaHost = FindVisual<PlotView>(_window.Content as FrameworkElement);
            var skiaModelProp = skiaHost?.Model;
            var combo = FindVisual<ComboBox>(_window.Content as FrameworkElement);

            var rects = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var i = 0; i < expanders.Count && i < 8; i++)
                if (expanders[i] is { } expander)
                    rects["group:" + i] = Rect(expander.RenderRect);
            if (list is not null)
                for (var i = 0; i < Math.Min(8, list.Items.Count); i++)
                    rects["examplerow:" + i] = Rect(list.Items[i].RenderRect);
            if (oxyPlot is not null) rects["plot"] = Rect(oxyPlot.RenderRect);
            // Slice-54: the window is confined to the 800x600 scene and the
            // renderer panel docks at its bottom-right corner, so the
            // ComboBox rect below is in-scene geometry evidence; the proof
            // still drives it through the real keyboard path (Tab focus +
            // ArrowDown/Up) while the engine suite covers the pointer path
            // directly.
            if (combo is not null) rects["rendererBox"] = Rect(combo.RenderRect);

            _lastSignature = Signature(list);
            _layoutPending = rects.Any(kv => IsClickable(kv.Key) && !HasArea(kv.Value));
            Note("rows", rows.ToString());
            Note("groups", groups.Count.ToString());
            Note("first", first);
            Note("groupExpanded", expandedFirst ? "True" : "False");
            Note("selectedIndex", (list?.SelectedIndex ?? -1).ToString());
            Note("selected", selectedTitle);
            Note("canvasModel", canvas is null ? "False" : "True");
            Note("skiaModel", skia is null ? "False" : "True");
            Note("xamlModel", xamlModel is null ? "False" : "True");
            Note("modelSameInstance",
                canvas is not null && ReferenceEquals(oxyModel, canvas) ? "True" : "False");
            Note("hostVisible",
                oxyPlot is not null && oxyPlot.Visibility == Visibility.Visible ? "True" : "False");
            Note("renderer", renderer);
            Note("codePresent", string.IsNullOrEmpty(code) ? "False" : "True");
            Note("canvasChildren", ChildCount(oxyCanvas).ToString());
            Note("canvasPaths", paths.ToString());
            Note("canvasLabels", labels.Count.ToString());
            Note("labels", JsonSerializer.Serialize(labels.Take(64).ToList()));
            Note("frameCommands", _host.CurrentCommands.Count.ToString());
            Note("xamlChildren", ChildCount(xamlCanvas).ToString());
            Note("xamlVisible",
                xamlPlot is not null && xamlPlot.Visibility == Visibility.Visible ? "True" : "False");
            Note("xamlModelSameInstance",
                xamlModel is not null && ReferenceEquals(xamlModelProp, xamlModel) ? "True" : "False");
            Note("xamlPaths", xamlPaths.ToString());
            Note("xamlLabelCount", xamlLabels.Count.ToString());
            Note("xamlLabels", JsonSerializer.Serialize(xamlLabels.Take(64).ToList()));
            Note("skiaVisible",
                skiaHost is not null && skiaHost.Visibility == Visibility.Visible ? "True" : "False");
            Note("skiaHostModel", skiaModelProp is null ? "False" : "True");
            Note("rendererText", combo?.SelectedItem?.ToString() ?? "");
            Note("focused", Keyboard.FocusedElement?.GetType().FullName ?? "<none>");
            Note("rects", "{" + string.Join(",",
                rects.Select(kv => JsonSerializer.Serialize(kv.Key) + ":" +
                    JsonSerializer.Serialize(kv.Value))) + "}");
        }

        private string Signature(ListBox? list)
        {
            var vm = _window.DataContext;
            var selected = vm?.GetType().GetProperty("SelectedExample")?.GetValue(vm);
            var title = selected?.GetType().GetProperty("Title")?.GetValue(selected)?.ToString() ?? "<none>";
            var canvas = vm?.GetType().GetProperty("CanvasModel")?.GetValue(vm);
            var oxyPlot = FindByFullName(_window.Content as FrameworkElement, "OxyPlot.Wpf.PlotView");
            var oxyModel = oxyPlot?.GetType().GetProperty("Model")?.GetValue(oxyPlot);
            var labels = CanvasInk(CanvasOf(oxyPlot)).Labels;
            var groups = list is null ? "" : string.Join(";",
                list.LogicalChildren.OfType<GroupItem>()
                    .Select(g => FindVisual<Expander>(g)?.IsExpanded == true ? "1" : "0"));
            var xamlPlotView = FindByFullName(_window.Content as FrameworkElement, "ExampleBrowser.XamlPlotView");
            var xamlLabelList = CanvasInk(CanvasOf(xamlPlotView)).Labels;
            var rendererName = vm?.GetType().GetProperty("Renderer")?.GetValue(vm)?.ToString() ?? "";
            var skiaHostView = FindVisual<PlotView>(_window.Content as FrameworkElement);
            return (list?.Items.Count ?? 0) + "|" + groups + "|" +
                (list?.SelectedIndex ?? -1) + "|" + title + "|" +
                (canvas is null ? "0" : "1") + "|" +
                (canvas is not null && ReferenceEquals(oxyModel, canvas) ? "1" : "0") + "|" +
                ChildCount(CanvasOf(oxyPlot)) + "|" + labels.Count + "|" +
                string.Join(";", labels.Take(6)) + "|" + _host.CurrentCommands.Count + "|" +
                rendererName + "|" +
                ChildCount(CanvasOf(xamlPlotView)) + "|" + xamlLabelList.Count + "|" +
                (skiaHostView is not null && skiaHostView.Visibility == Visibility.Visible ? "1" : "0") + "|" +
                (Keyboard.FocusedElement?.GetType().FullName ?? "<none>");
        }

        private void Note(string key, string value)
        {
            try { _ = _js.InvokeVoidAsync("wpfOxyplot.note", key, value); }
            catch { }
        }

        private static bool IsClickable(string key) =>
            key.StartsWith("group:", StringComparison.Ordinal) ||
            key.StartsWith("examplerow:", StringComparison.Ordinal);

        private static bool HasArea(string rect)
        {
            var parts = rect.Split(',');
            return parts.Length == 4 &&
                double.TryParse(parts[2], out var w) &&
                double.TryParse(parts[3], out var h) && w > 0 && h > 0;
        }

        private static string Rect(WpfRect r) =>
            $"{r.X:0},{r.Y:0},{r.Width:0},{r.Height:0}";

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

        private static FrameworkElement? FindByFullName(FrameworkElement? root, string fullName)
        {
            if (root is null) return null;
            if (root.GetType().FullName == fullName) return root;
            foreach (var child in root.LogicalChildren)
            {
                var found = FindByFullName(child as FrameworkElement, fullName);
                if (found is not null) return found;
            }
            return null;
        }

        private static object? CanvasOf(object? plotView) =>
            plotView?.GetType().GetProperty("Canvas",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.FlattenHierarchy)
                ?.GetValue(plotView);

        private static (int Paths, List<string> Labels) CanvasInk(object? canvas)
        {
            if (canvas?.GetType().GetProperty("Children")?.GetValue(canvas)
                is not System.Collections.IEnumerable children)
                return (0, []);
            var list = children.Cast<object>().ToList();
            var paths = list.OfType<DesktopWasm.Wpf.Path>().Count();
            var labels = list.OfType<TextBlock>()
                .Select(t => t.Text ?? "")
                .Where(t => t.Length > 0)
                .ToList();
            return (paths, labels);
        }

        private static int ChildCount(object? canvas) =>
            canvas?.GetType().GetProperty("Children")?.GetValue(canvas)
                is System.Collections.IEnumerable children
                ? children.Cast<object>().Count() : -1;
    }
}
