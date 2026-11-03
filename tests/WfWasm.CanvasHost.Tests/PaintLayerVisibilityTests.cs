using System.Reflection;
using System.Text;
using WfWasm.CanvasHost;
using WfWasm.Shim;

namespace WfWasm.CanvasHost.Tests;

// FACTORY-shell-6: verifier repro (u-paint-vis4/5) shows the Paint Layers
// visibility checkbox silently ignoring clicks. The box is owner-drawn inside
// the custom LayersStrip (no CheckBox control exists) and toggles only via
// PerformItemClick(index, CheckBox); the host's synthetic OnMouseDown/Click
// reflection never reaches it. WinFormsAppHost routes strip checkbox hits
// through PerformItemClick (TryPerformStripCheckClick, reflection-only so the
// host takes no PaintDotNet compile dependency). Row geometry comes from
// ClientRectToViewRect + ViewPointToItemIndex (PDN has no per-part measure
// API in this build); the eye is gated to the row leading edge.
// Functional proof is browser-side (staging pair + screenshots, report);
// these tests pin the integration contract and the fail-open behavior.
[Collection("Paint")]
public sealed class PaintLayerVisibilityTests
{
    private readonly WinFormsAppHost _host;

    public PaintLayerVisibilityTests(PaintHostFixture fx)
    {
        _host = fx.Host;
    }

    [Fact]
    public void StripCheckbox_ClickContract_Exists()
    {
        // The host fix depends on this exact PDN surface. If Paint.NET
        // renames it, this test fails loudly instead of silently no-op clicks.
        var strip = FindLayersStrip(out string tree);
        Assert.True(strip is not null, "No LayersStrip found in Layers dialog.\n" + tree);
        var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        System.Reflection.MethodInfo? Find(string n, int argc, Func<System.Reflection.ParameterInfo[], bool>? extra = null)
        {
            for (var tt = strip!.GetType(); tt is not null && tt != typeof(object); tt = tt.BaseType)
            {
                var m = tt.GetMethods(flags | BindingFlags.DeclaredOnly)
                    .FirstOrDefault(x => x.Name == n && x.GetParameters().Length == argc && (extra == null || extra(x.GetParameters())));
                if (m is not null) return m;
            }
            return null;
        }
        var clickM = Find("PerformItemClick", 3, ps => ps[0].ParameterType == typeof(int));
        var c2vM = Find("ClientRectToViewRect", 1);
        var v2iM = Find("ViewPointToItemIndex", 1);
        var itemM = Find("ItemIndexToItem", 1);
        var countP = strip!.GetType().GetProperty("ItemCount", flags);
        Assert.True(clickM is not null, "Missing PerformItemClick(int, part, buttons).\n" + tree);
        Assert.True(c2vM is not null, "Missing ClientRectToViewRect(rect).\n" + tree);
        Assert.True(v2iM is not null, "Missing ViewPointToItemIndex(point).\n" + tree);
        Assert.True(itemM is not null, "Missing ItemIndexToItem(index).\n" + tree);
        Assert.True(countP is not null, "Missing ItemCount.\n" + tree);
        var ps = clickM!.GetParameters();
        Assert.True(Enum.IsDefined(ps[1].ParameterType, "CheckBox"), "ItemPart.CheckBox missing.\n" + tree);
        Assert.True(Enum.IsDefined(ps[2].ParameterType, "Left"), "PointerButtons.Left missing.\n" + tree);
        System.Type? nested = null;
        for (var tt = strip!.GetType(); tt is not null && tt != typeof(object) && nested is null; tt = tt.BaseType)
            nested = tt.GetNestedType("Item", flags);
        var checkedP = nested?.GetProperty("IsChecked", flags);
        Assert.True(checkedP is not null && checkedP.PropertyType == typeof(bool), "Missing Item.IsChecked bool.\n" + tree);
    }

    [Fact]
    public void StripClick_WithoutItems_IsGracefulNoop()
    {
        // Headless the strip materializes no items (no render loop), so the
        // checkbox path cannot fire: the paired click must degrade to the
        // legacy path without throwing, mutating, or closing the dialog.
        var strip = FindLayersStrip(out string tree);
        Assert.True(strip is not null, "No LayersStrip found in Layers dialog.\n" + tree);
        var layer = GetBackgroundLayer(out string model, out _);
        Assert.True(layer is not null, "No background layer in active document.\n" + model);
        bool initial = GetVisible(layer!);
        var (originX, originY) = StripOrigin(strip!);
        int px = originX + strip!.Width / 2, py = originY + strip.Height / 2;
        _host.OnMouseDown(px, py);
        _host.OnMouseUp(px, py);
        Assert.Equal(initial, GetVisible(layer!));
        Assert.NotNull(FindLayersStrip(out _));
    }

    private static void SetVisible(object layer, bool value)
    {
        layer.GetType().GetProperty("Visible")!.SetValue(layer, value);
    }

    private static System.Windows.Forms.Control? FindLayersStrip(out string tree)
    {
        var dump = new StringBuilder();
        System.Windows.Forms.Control? strip = null;
        foreach (var win in System.Windows.Forms.WasmDialogHost.Snapshot())
        {
            var form = win.Form;
            try
            {
                dump.AppendLine($"dialog={form.GetType().FullName} name={form.Name} loc={form.Location}");
                if (form.Name.Contains("Layer", StringComparison.OrdinalIgnoreCase))
                    strip = FindByName(form.Controls, "layersStrip");
            }
            catch (Exception ex) { dump.AppendLine("  WALK-ERR " + ex.GetType().Name); }
        }
        tree = dump.ToString();
        return strip;
    }

    private static System.Windows.Forms.Control? FindByTypeName(
        System.Windows.Forms.Control.ControlCollection controls, string part)
    {
        foreach (System.Windows.Forms.Control c in controls)
        {
            if (c.GetType().Name.Contains(part, StringComparison.OrdinalIgnoreCase))
                return c;
            var inner = FindByTypeName(c.Controls, part);
            if (inner is not null)
                return inner;
        }
        return null;
    }

    private static System.Windows.Forms.Control? FindByName(
        System.Windows.Forms.Control.ControlCollection controls, string part)
    {
        foreach (System.Windows.Forms.Control c in controls)
        {
            if (c.Name.Contains(part, StringComparison.OrdinalIgnoreCase))
                return c;
            var inner = FindByName(c.Controls, part);
            if (inner is not null)
                return inner;
        }
        return null;
    }

    private static (int X, int Y) StripOrigin(System.Windows.Forms.Control strip)
    {
        int x = strip.Left, y = strip.Top;
        var p = strip.Parent;
        while (p is not null && p is not System.Windows.Forms.Form)
        {
            x += p.Left;
            y += p.Top;
            p = p.Parent;
        }
        var form = strip.FindForm();
        Assert.NotNull(form);
        return (x + form!.Location.X, y + form.Location.Y + WinFormsAppHost.ChromeHeight(form));
    }

    private object? GetBackgroundLayer(out string model, out object? docOut)
    {
        var dump = new StringBuilder();
        object? layer = null;
        docOut = null;
        try
        {
            var form = _host.GetOrCreateForm(AppScene.Paint);
            dump.AppendLine("form=" + (form?.GetType().FullName ?? "null"));
            var ws = form is null ? null : FindByTypeName(form.Controls, "AppWorkspace");
            dump.AppendLine("workspace=" + (ws?.GetType().FullName ?? "null"));
            var docWs = ws?.GetType().GetProperty("ActiveDocumentWorkspace")?.GetValue(ws);
            dump.AppendLine("docWs=" + (docWs?.GetType().FullName ?? "null"));
            var doc = docWs?.GetType().GetProperty("Document")?.GetValue(docWs);
            docOut = doc;
            var layers = doc?.GetType().GetProperty("Layers")?.GetValue(doc) as System.Collections.IList;
            dump.AppendLine("layers=" + (layers is null ? "null" : "count=" + layers.Count));
            if (layers is not null && layers.Count > 0)
            {
                layer = layers[0];
                dump.AppendLine("layer0=" + layer?.GetType().FullName + " visible=" + (layer is null ? "?" : GetVisible(layer).ToString()));
            }
        }
        catch (Exception ex) { dump.AppendLine("MODEL-ERR " + ex.GetType().Name + ": " + ex.Message.Split('\n')[0]); }
        model = dump.ToString();
        return layer;
    }

    private static bool GetVisible(object layer)
    {
        return (bool)layer.GetType().GetProperty("Visible")!.GetValue(layer)!;
    }
}
