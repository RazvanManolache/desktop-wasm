using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Windows.Forms;

namespace WfWasm.CanvasHost;

// Explicit read-only inspector for headless acceptance; never runs in paint.
internal static class EditorStateProbe
{
    private static object? Get(object? o, string p)
    {
        if (o is null) return null;
        for (Type? t = o.GetType(); t is not null; t = t.BaseType)
            try
            {
                var property = t.GetProperty(p, BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
                if (property is not null) return property.GetValue(o);
            }
            catch { return null; }
        return null;
    }

    internal static string Dump(WinFormsAppHost host)
    {
        var root = typeof(WinFormsAppHost).GetField("_activeForm", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(host) as Control;
        var controls = new List<object>();
        var elements = new List<object>();
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        object? caret = null;
        void Visual(object? e, int depth)
        {
            if (e is null || depth > 40 || seen.Count > 3000 || !seen.Add(e)) return;
            caret ??= Get(e, "Caret");
            string type = e.GetType().Name;
            if (type.Contains("Font", StringComparison.OrdinalIgnoreCase) || type.Contains("RichText", StringComparison.Ordinal) || type.Contains("ListView", StringComparison.Ordinal) || type.Contains("List", StringComparison.Ordinal))
            {
                var names = Get(e, "FontNames") as IEnumerable;
                elements.Add(new { type, name = Get(e, "Name")?.ToString(), text = Get(e, "Text")?.ToString(), bounds = Get(e, "ControlBoundingRectangle")?.ToString(), size = Get(e, "Size")?.ToString(), visibility = Get(e, "Visibility")?.ToString(), children = Get(Get(e, "Children"), "Count"), items = Get(Get(e, "Items"), "Count"), fonts = names?.Cast<object>().Take(100).Select(x => x.ToString()).ToArray() });
            }
            if (Get(e, "Children") is IEnumerable children)
                foreach (var child in children) Visual(child, depth + 1);
        }
        void Walk(Control c)
        {
            if (controls.Count > 1000) return;
            controls.Add(new { type = c.GetType().FullName, c.Name, c.Text, bounds = c.Bounds.ToString(), c.Visible, c.Focused, c.ContainsFocus, parent = c.Parent?.Name, dock = c.Dock.ToString(), value = Get(c, "Value"), selfPaint = WinFormsAppHost.TypeSelfPaints(c.GetType()), bgImageNull = c.BackgroundImage is null, items = Get(Get(c, "Items"), "Count"), columns = (Get(c, "Columns") as IEnumerable)?.Cast<object>().Select(x => new { name = Get(x, "Name")?.ToString(), width = Get(x, "Width") }).ToArray() });
            Visual(Get(c, "RootElement"), 0);
            foreach (Control child in c.Controls) Walk(child);
        }
        if (root is not null) Walk(root);
        foreach (var window in WasmDialogHost.Snapshot()) if (!ReferenceEquals(window.Form, root)) { seen.Clear(); Walk(window.Form); }
        foreach (var popup in WasmPopupHost.Snapshot()) { seen.Clear(); Walk(popup); }
        object? position = Get(caret, "Position"), bounds = Get(caret, "ControlBoundingRectangle");
        return JsonSerializer.Serialize(new
        {
            controls, elements,
            caret = caret is null ? null : new { type = caret.GetType().FullName, visibility = Get(caret, "Visibility")?.ToString(), color = Get(caret, "Color")?.ToString(), x = Get(position, "X"), y = Get(position, "Y"), width = Get(caret, "Width"), height = Get(caret, "Height"), bounds = bounds?.ToString(), focused = Get(caret, "IsFocused"), shown = Get(caret, "IsVisible") },
            forms = Application.OpenForms.Cast<Form>().Select(f => new { type = f.GetType().FullName, f.Text, bounds = f.Bounds.ToString(), f.Visible, controls = f.Controls.Cast<Control>().Select(c => new { type = c.GetType().Name, c.Name, c.Text, bounds = c.Bounds.ToString(), value = Get(c, "Value") }).ToArray() }).ToArray()
        });
    }
}
