using System.Text.Json;
using DesktopWasm.Wpf;
using Microsoft.JSInterop;

namespace DesktopWasm.BrowserHost;

// Browser attach for the real converted WPFGallery (external app,
// slug wpf-gallery). Called once per session from Home.razor: installs the
// wpfGallery status channel so the proof asserts post-input STATE (current
// page, tree selection, checkbox/button states, clickable rects) instead of
// repaint hashes alone. No input bridging: Playwright drives the real
// pointer path through the shared input surface.
public static class GalleryBrowser
{
    public static async Task WireAsync(string slug, Window window, IJSRuntime js)
    {
        if (!string.Equals(slug, "wpf-gallery", StringComparison.OrdinalIgnoreCase)) return;
        try
        {
            await js.InvokeVoidAsync("wpfGallery.attach").ConfigureAwait(false);
        }
        catch
        {
        }
        var state = new GalleryState(window, js);
        _active = state;
        state.PublishAll();
    }

    private static GalleryState? _active;

    /// <summary>Re-publishes gallery state on demand (the proof pulls this after layout settles).</summary>
    [JSInvokable]
    public static void RefreshNotes()
    {
        try { _active?.PublishAll(); }
        catch
        {
        }
    }

    private sealed class GalleryState
    {
        private readonly Window _window;
        private readonly IJSRuntime _js;
        private bool _attached;
        private string _lastSignature = "";
        private bool _layoutPending = true;
        private readonly HashSet<ListBox> _selectionWired = new();

        public GalleryState(Window window, IJSRuntime js)
        {
            _window = window;
            _js = js;
        }

        public void PublishAll()
        {
            var frame = FindVisual<Frame>(_window.Content as FrameworkElement);
            var tree = FindVisual<TreeView>(_window.Content as FrameworkElement);
            if (!_attached)
            {
                _attached = true;
                if (frame is not null) frame.Navigated += (_, _) => PublishAll();
                if (tree is not null) tree.SelectionChanged += () => PublishAll();
                _window.Invalidated += _ => PublishOnInvalidated();
            }
            if (frame?.Content is FrameworkElement freshPage)
                foreach (var list in FindVisuals<ListBox>(freshPage))
                    if (_selectionWired.Add(list))
                        list.SelectionChanged += () => PublishAll();
            Publish(frame, tree);
        }

        private void PublishOnInvalidated()
        {
            // Layout, navigation and control updates surface here; re-publish
            // only when the state signature moved or rects were still empty
            // (first layout), so idle frames cost nothing.
            try
            {
                var frame = FindVisual<Frame>(_window.Content as FrameworkElement);
                var tree = FindVisual<TreeView>(_window.Content as FrameworkElement);
                var signature = Signature(frame, tree);
                if (signature == _lastSignature && !_layoutPending) return;
                Publish(frame, tree);
            }
            catch
            {
            }
        }

        private void Publish(Frame? frame, TreeView? tree)
        {
            var page = frame?.Content as FrameworkElement;
            var pageName = frame?.Content?.GetType().FullName ?? "<none>";
            var selected = SelectedHeader(tree);
            var checks = page is null ? [] : FindVisuals<CheckBox>(page)
                .Select((c, i) => new CheckState(i, c.Content?.ToString() ?? "", c.IsChecked == true)).ToList();
            var buttons = page is null ? [] : FindVisuals<Button>(page)
                .Take(12).Select(b => new ButtonState(
                    b.Content?.ToString() ?? b.GetType().Name, b.IsEnabled)).ToList();
            var textboxes = page is null ? [] : FindVisuals<TextBox>(page)
                .Take(12).Select(t => new TextState(
                    t.Name, t.Text ?? "", Validation.GetHasError(t))).ToList();
            var groups = page is null ? [] : FindVisuals<GroupBox>(page)
                .Take(8).Select(g => new GroupState(
                    g.Header?.ToString() ?? "", g.HasHeader)).ToList();
            var grids = page is null ? [] : FindVisuals<Grid>(page)
                .Take(8).Select(g => new GridState(
                    g.ShowGridLines, g.RowDefinitions.Count, g.ColumnDefinitions.Count)).ToList();
            var lists = page is null ? [] : FindVisuals<ListBox>(page)
                .Take(8).Select(l => new ListState(
                    AutomationProperties.GetName(l),
                    l.Items.Count, l.SelectedIndex,
                    l.SelectedItem?.Content ?? "")).ToList();
            var menus = page is null ? [] : FindVisuals<Menu>(page)
                .Take(4).Select(m => new MenuState(
                    m.Items.Select(MenuItemState.For).ToList())).ToList();
            var calendars = page is null ? [] : FindVisuals<Calendar>(page)
                .Take(4).Select(c => new CalendarState(
                    AutomationProperties.GetName(c),
                    c.DisplayDate.ToString("yyyy-MM-dd"),
                    c.SelectedDate?.ToString("yyyy-MM-dd") ?? "",
                    c.DisplayMode.ToString())).ToList();
            var richtexts = page is null ? [] : FindVisuals<RichTextBox>(page)
                .Take(4).Select(r => new RichTextState(
                    AutomationProperties.GetName(r),
                    r.PlainText ?? "",
                    r.IsFocused)).ToList();
            var pickers = page is null ? [] : FindVisuals<DatePicker>(page)
                .Take(4).Select(p => new DatePickerState(
                    AutomationProperties.GetName(p),
                    p.Text ?? "",
                    p.SelectedDate?.ToString("yyyy-MM-dd") ?? "",
                    p.IsDropDownOpen)).ToList();
            var texts = page is null ? [] : FindVisuals<TextBlock>(page)
                .Where(t => t.Name.Length > 0)
                .Take(12).Select(t => new NamedTextState(
                    t.Name, t.Text ?? "", t.Visibility == Visibility.Visible)).ToList();
            var rects = new Dictionary<string, string>(StringComparer.Ordinal);
            if (tree is not null)
                foreach (var item in tree.VisibleRows)
                {
                    var header = HeaderText(item);
                    if (string.IsNullOrEmpty(header) || rects.ContainsKey("row:" + header)) continue;
                    rects["row:" + header] = Rect(item.RenderRect);
                }
            if (page is not null)
            {
                foreach (var card in FindVisuals<Button>(page))
                {
                    var param = card.CommandParameter?.ToString() ?? "";
                    if (param.Length == 0 || rects.ContainsKey("card:" + param)) continue;
                    rects["card:" + param] = Rect(card.RenderRect);
                }
                var index = 0;
                foreach (var check in FindVisuals<CheckBox>(page))
                    rects["check:" + (index++)] = Rect(check.RenderRect);
                var textIndex = 0;
                foreach (var field in FindVisuals<TextBox>(page).Take(12))
                    rects["text:" + (textIndex++)] = Rect(field.RenderRect);
                var listIndex = 0;
                var menuKey = 0;
                foreach (var list in FindVisuals<ListBox>(page).Take(8))
                    foreach (var row in list.Items)
                        rects["listitem:" + (listIndex++)] = Rect(row.RenderRect);
                foreach (var entry in FindVisuals<MenuItem>(page).Take(64))
                {
                    var key = AutomationProperties.GetName(entry);
                    if (string.IsNullOrEmpty(key)) key = MenuItemState.Text(entry);
                    if (string.IsNullOrEmpty(key)) key = "item" + menuKey++;
                    key = "menuitem:" + key;
                    if (!rects.ContainsKey(key)) rects[key] = Rect(entry.RenderRect);
                }
                foreach (var cal in FindVisuals<Calendar>(page).Take(4))
                {
                    var first = new DateTime(cal.DisplayDate.Year, cal.DisplayDate.Month, 1);
                    for (var i = -7; i < 42; i++)
                    {
                        var day = first.AddDays(i);
                        if (cal.TryGetDateRect(day, out var dayRect))
                            rects["calday:" + day.ToString("yyyy-MM-dd")] = Rect(dayRect);
                    }
                }
                var richIndex = 0;
                foreach (var rich in FindVisuals<RichTextBox>(page).Take(4))
                    rects[$"richtext:{richIndex++}"] = Rect(rich.RenderRect);
                var pickerIndex = 0;
                foreach (var picker in FindVisuals<DatePicker>(page).Take(4))
                {
                    rects[$"datepicker:{pickerIndex}"] = Rect(picker.RenderRect);
                    if (picker.TryGetToggleRect(out var toggleRect))
                        rects[$"datepicker-toggle:{pickerIndex}"] = Rect(toggleRect);
                    pickerIndex++;
                }
            }
            _lastSignature = Signature(frame, tree);
            _layoutPending = rects.Any(kv => IsClickable(kv.Key) && !HasArea(kv.Value));
            Note("rows", JsonSerializer.Serialize(rects.Keys
                .Where(k => k.StartsWith("row:", StringComparison.Ordinal)).ToList()));
            Note("cards", JsonSerializer.Serialize(rects.Keys
                .Where(k => k.StartsWith("card:", StringComparison.Ordinal)).ToList()));
            Note("page", pageName);
            Note("selected", selected ?? "<none>");
            Note("checks", JsonSerializer.Serialize(checks));
            Note("buttons", JsonSerializer.Serialize(buttons));
            Note("textboxes", JsonSerializer.Serialize(textboxes));
            Note("groupboxes", JsonSerializer.Serialize(groups));
            Note("grids", JsonSerializer.Serialize(grids));
            Note("listboxes", JsonSerializer.Serialize(lists));
            Note("menus", JsonSerializer.Serialize(menus));
            Note("calendars", JsonSerializer.Serialize(calendars));
            Note("richtexts", JsonSerializer.Serialize(richtexts));
            Note("datepickers", JsonSerializer.Serialize(pickers));
            Note("texts", JsonSerializer.Serialize(texts));
            Note("rects", "{" + string.Join(",",
                rects.Select(kv => JsonSerializer.Serialize(kv.Key) + ":" +
                    JsonSerializer.Serialize(kv.Value))) + "}");
        }

        private static string Signature(Frame? frame, TreeView? tree)
        {
            var page = frame?.Content as FrameworkElement;
            var checks = page is null ? "" : string.Join(";",
                FindVisuals<CheckBox>(page).Select(c => c.IsChecked == true ? "1" : "0"));
            var buttons = page is null ? "" : string.Join(";",
                FindVisuals<Button>(page).Take(12).Select(b => b.IsEnabled ? "1" : "0"));
            var fields = page is null ? "" : string.Join(";",
                FindVisuals<TextBox>(page).Take(12).Select(t => (t.Text ?? "") + (Validation.GetHasError(t) ? "!err" : "")));
            var gridFlags = page is null ? "" : string.Join(";",
                FindVisuals<Grid>(page).Take(8).Select(g => g.ShowGridLines ? "1" : "0"));
            var listFlags = page is null ? "" : string.Join(";",
                FindVisuals<ListBox>(page).Take(8).Select(l =>
                    l.Items.Count + ":" + l.SelectedIndex + ":" + (l.SelectedItem?.Content ?? "")));
            var menuFlags = page is null ? "" : string.Join(";",
                FindVisuals<MenuItem>(page).Take(64).Select(i =>
                    (i.IsSubmenuOpen ? "1" : "0") + MenuItemState.Text(i)));
            var calFlags = page is null ? "" : string.Join(";",
                FindVisuals<Calendar>(page).Take(4).Select(c =>
                    c.DisplayDate.ToString("yyyy-MM-dd") + ":" +
                    (c.SelectedDate?.ToString("yyyy-MM-dd") ?? "") + ":" + c.DisplayMode));
            var pickerFlags = page is null ? "" : string.Join(";",
                FindVisuals<DatePicker>(page).Take(4).Select(p =>
                    (p.Text ?? "") + ":" +
                    (p.SelectedDate?.ToString("yyyy-MM-dd") ?? "") + ":" + (p.IsDropDownOpen ? "1" : "0")));
            var richFlags = page is null ? "" : string.Join(";",
                FindVisuals<RichTextBox>(page).Take(4).Select(r =>
                    AutomationProperties.GetName(r) + "=" + (r.PlainText ?? "") + (r.IsFocused ? "" : "!blur")));
            var textFlags = page is null ? "" : string.Join(";",
                FindVisuals<TextBlock>(page).Where(t => t.Name.Length > 0).Take(12).Select(t =>
                    t.Name + "=" + (t.Text ?? "") + (t.Visibility == Visibility.Visible ? "" : "!hidden")));
            return (frame?.Content?.GetType().FullName ?? "<none>") + "|" +
                SelectedHeader(tree) + "|" + checks + "|" + buttons + "|" + fields + "|" + gridFlags + "|" + listFlags + "|" + menuFlags + "|" + textFlags + "|" + calFlags + "|" + pickerFlags + "|" + richFlags;
        }

        private void Note(string key, string value)
        {
            try { _ = _js.InvokeVoidAsync("wpfGallery.note", key, value); }
            catch { }
        }

        private static string? SelectedHeader(TreeView? tree)
        {
            var selected = tree?.SelectedItem;
            if (selected is null) return "<none>";
            if (tree?.ItemContainerGenerator.ContainerFromItem(selected) is TreeViewItem item)
                return HeaderText(item);
            return selected.ToString();
        }

        private static bool IsClickable(string key) =>
            key.StartsWith("row:", StringComparison.Ordinal) ||
            key.StartsWith("card:", StringComparison.Ordinal) ||
            key.StartsWith("check:", StringComparison.Ordinal) ||
            key.StartsWith("listitem:", StringComparison.Ordinal) ||
            key.StartsWith("menuitem:", StringComparison.Ordinal) ||
            key.StartsWith("calday:", StringComparison.Ordinal) ||
            key.StartsWith("datepicker:", StringComparison.Ordinal) ||
            key.StartsWith("datepicker-toggle:", StringComparison.Ordinal) ||
            key.StartsWith("richtext:", StringComparison.Ordinal);

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
            if (item.Header is TextBlock tb) return tb.Text ?? "";
            if (item.Header is FrameworkElement fe)
            {
                var texts = new List<string>();
                CollectText(fe, texts);
                if (texts.Count > 0) return string.Join(" ", texts);
            }
            return item.Header?.ToString() ?? "";
        }

        private static void CollectText(FrameworkElement? fe, List<string> found)
        {
            if (fe is null) return;
            if (fe is TextBlock tb && !string.IsNullOrEmpty(tb.Text)) found.Add(tb.Text);
            foreach (var child in fe.LogicalChildren) CollectText(child as FrameworkElement, found);
        }

        private sealed record CheckState(int Index, string Text, bool Checked);

        private sealed record ButtonState(string Text, bool Enabled);

        private sealed record TextState(string Name, string Text, bool HasError);

        private sealed record GroupState(string Header, bool HasHeader);

        private sealed record GridState(bool ShowGridLines, int Rows, int Cols);

        private sealed record ListState(string Name, int Count, int SelectedIndex, string Selected);

        private sealed record MenuState(List<MenuItemState> Items);

        private sealed record CalendarState(string Name, string Display, string Selected, string Mode);

        private sealed record DatePickerState(string Name, string Text, string Selected, bool IsOpen);

        private sealed record RichTextState(string Name, string Text, bool IsFocused);

        private sealed record MenuItemState(string Header, string Name, bool Enabled, bool Open, bool HasItems, List<MenuItemState> Children)
        {
            public static MenuItemState For(MenuItem item) => new(
                Text(item), AutomationProperties.GetName(item),
                item.IsEffectivelyEnabled, item.IsSubmenuOpen, item.HasItems,
                item.Items.Select(For).ToList());

            public static string Text(MenuItem item) => item.Header switch
            {
                string s => s,
                TextBlock block => block.Text ?? "",
                null => "",
                _ => item.Header.ToString() ?? "",
            };
        }

        private sealed record NamedTextState(string Name, string Text, bool Visible);
    }
}
