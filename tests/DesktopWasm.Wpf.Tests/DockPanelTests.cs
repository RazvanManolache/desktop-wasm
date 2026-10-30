using System.Text;
using DesktopWasm.Core;

namespace DesktopWasm.Wpf.Tests;

/// <summary>DockPanel v1 tests: edge docking order, fill semantics, margins,
/// measure shape, loose XAML (DockPanel.Dock/LastChildFill), synthetic BAML
/// (type -163, Dock -39/Enum, LastChildFill -40/Boolean, Children -213;
/// framing mirrors the real DockPanel fixture dump).</summary>
[ResetAmbientState]
public sealed class DockPanelTests
{
    private const short TDock = -163;
    private const short MDock = -39;
    private const short MFill = -40;
    private const short MContent = -14;
    private const short MChildren = -213;
    private const short TTextBlock = -638;
    private const short TTextBox = -639;
    private const short CString = -615;
    private const short CBool = -46;
    private const short CEnum = -195;

    private static Window Shell(DockPanel dock, double width = 400, double height = 300)
    {
        var window = new Window { Content = dock };
        WasmMilCore.RenderHeadless(window, new WpfSize(width, height));
        return window;
    }

    private static Border Slab(double width, double height)
    {
        var b = new Border { Width = width, Height = height };
        return b;
    }

    [Fact]
    public void Layout_docks_edges_in_order_with_fill()
    {
        var dock = new DockPanel();
        var left = Slab(100, 40);
        var top = Slab(500, 50);
        var right = Slab(60, 500);
        var fill = Slab(10, 10);
        DockPanel.SetDock(left, Dock.Left);
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(right, Dock.Right);
        dock.AddChild(left);
        dock.AddChild(top);
        dock.AddChild(right);
        dock.AddChild(fill);
        Shell(dock);

        Assert.Equal(new WpfRect(0, 0, 100, 300), left.ArrangedRect);
        Assert.Equal(new WpfRect(100, 0, 300, 50), top.ArrangedRect);
        Assert.Equal(new WpfRect(340, 50, 60, 250), right.ArrangedRect);
        Assert.Equal(new WpfRect(100, 50, 240, 250), fill.ArrangedRect);
    }

    [Fact]
    public void Layout_clamps_docked_slabs_to_remaining_space()
    {
        var dock = new DockPanel();
        var left = Slab(500, 10);
        var fill = Slab(10, 10);
        DockPanel.SetDock(left, Dock.Left);
        dock.AddChild(left);
        dock.AddChild(fill);
        Shell(dock, 400, 300);

        Assert.Equal(new WpfRect(0, 0, 400, 300), left.ArrangedRect);
        Assert.Equal(new WpfRect(400, 0, 0, 300), fill.ArrangedRect);
    }

    [Fact]
    public void LastChildFill_false_docks_last_child()
    {
        var dock = new DockPanel { LastChildFill = false };
        var left = Slab(100, 40);
        var last = Slab(40, 40);
        DockPanel.SetDock(left, Dock.Left);
        DockPanel.SetDock(last, Dock.Right);
        dock.AddChild(left);
        dock.AddChild(last);
        Shell(dock);

        Assert.Equal(new WpfRect(0, 0, 100, 300), left.ArrangedRect);
        Assert.Equal(new WpfRect(360, 0, 40, 300), last.ArrangedRect);
        // Natural size: slabs add (100+40 wide), heights max (40); the
        // panel stretches both to the 400x300 slot at arrange time.
        Assert.Equal(new Size(140, 40), dock.DesiredSize);
    }

    [Fact]
    public void Default_dock_is_left()
    {
        var dock = new DockPanel();
        var first = Slab(70, 20);
        var fill = Slab(10, 10);
        dock.AddChild(first); // no attached value
        dock.AddChild(fill);
        Shell(dock);

        Assert.Equal(Dock.Left, DockPanel.GetDock(first));
        Assert.Equal(new WpfRect(0, 0, 70, 300), first.ArrangedRect);
        Assert.Equal(new WpfRect(70, 0, 330, 300), fill.ArrangedRect);
    }

    [Fact]
    public void Margins_inset_children_and_expand_slabs()
    {
        var dock = new DockPanel();
        var left = Slab(100, 40);
        left.Margin = new Thickness(4);
        var fill = Slab(10, 10);
        DockPanel.SetDock(left, Dock.Left);
        dock.AddChild(left);
        dock.AddChild(fill);
        Shell(dock);

        Assert.Equal(new WpfRect(4, 4, 100, 292), left.ArrangedRect);
        Assert.Equal(new WpfRect(108, 0, 292, 300), fill.ArrangedRect);
    }

    [Fact]
    public void Measure_reports_docked_extents()
    {
        var dock = new DockPanel();
        var left = Slab(100, 40);
        var top = Slab(500, 50);
        var fill = Slab(10, 10);
        DockPanel.SetDock(left, Dock.Left);
        DockPanel.SetDock(top, Dock.Top);
        dock.AddChild(left);
        dock.AddChild(top);
        dock.AddChild(fill);
        Shell(dock);

        // Natural size may exceed available (arrange clamps into the slot):
        // Left 100 + Top 500 wide (explicit Width survives measure) = 600;
        // heights max to the fill row (50 + 10) = 60.
        Assert.Equal(new Size(600, 60), dock.DesiredSize);
    }

    [Fact]
    public void Empty_panel_measures_zero_and_renders_nothing()
    {
        var dock = new DockPanel();
        var window = Shell(dock);
        Assert.Equal(new Size(0, 0), dock.DesiredSize);
        var dc = new DrawingContext();
        dock.Render(dc);
        Assert.Empty(dc.Commands);
        _ = window;
    }

    [Fact]
    public void Attached_accessors_roundtrip_and_parse()
    {
        var b = Slab(10, 10);
        Assert.Equal(Dock.Left, DockPanel.GetDock(b));
        DockPanel.SetDock(b, Dock.Bottom);
        Assert.Equal(Dock.Bottom, DockPanel.GetDock(b));
        Assert.True(DockPanel.TryParseDock("Top", out var d));
        Assert.Equal(Dock.Top, d);
        Assert.True(DockPanel.TryParseDock("bottom", out d));
        Assert.Equal(Dock.Bottom, d);
        Assert.False(DockPanel.TryParseDock("Sideways", out _));
        Assert.False(DockPanel.TryParseDock(null, out _));
        // String-backed attached values (BAML form) read through the same path.
        b.SetAttached("DockPanel.Dock", "Right");
        Assert.Equal(Dock.Right, DockPanel.GetDock(b));
    }

    [Fact]
    public void Xaml_maps_dockpanel_with_dock_and_fill()
    {
        const string xaml =
            "<Window xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\">" +
            "<DockPanel LastChildFill=\"False\">" +
            "<Button DockPanel.Dock=\"Top\" Content=\"Bar\"/>" +
            "<Button DockPanel.Dock=\"Left\" Content=\"Side\"/>" +
            "</DockPanel></Window>";
        var window = (Window)XamlLoader.Load(xaml).Root;
        var dock = Assert.IsType<DockPanel>(window.Content);
        Assert.False(dock.LastChildFill);
        Assert.Equal(2, dock.LogicalChildren.Count);
        Assert.Equal(Dock.Top, DockPanel.GetDock(dock.LogicalChildren[0]));
        Assert.Equal(Dock.Left, DockPanel.GetDock(dock.LogicalChildren[1]));
    }

    [Fact]
    public void Xaml_rejects_unknown_dock_value()
    {
        const string xaml =
            "<Window xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\">" +
            "<DockPanel><Button DockPanel.Dock=\"Sideways\" Content=\"x\"/></DockPanel></Window>";
        var ex = Assert.Throws<XamlLoadException>(() => XamlLoader.Load(xaml));
        Assert.Contains("is not a Dock", ex.Message, StringComparison.Ordinal);
    }

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

    private static byte[] DockBytes()
    {
        var b = new B();
        b.Raw(0x0C, 0x00, 0x00, 0x00)
         .Raw(0x4D, 0x00, 0x53, 0x00, 0x42, 0x00, 0x41, 0x00, 0x4D, 0x00, 0x4C, 0x00)
         .U16(0).U16(0x60).Raw(new byte[] { 0x00, 0x00, 0x60, 0x00, 0x00, 0x00, 0x60, 0x00, 0x01, 0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0x00 });
        b.Raw(0x01);
        b.Sized(0x1C, p => p.U16(0).Str("DockProbe"));
        b.Sized(0x1D, p => p.U16(0).U16(0).Str("DockProbe.MainWindow"));
        b.Raw(0x03).I16(0).Raw(0x00);
        b.Raw(0x2E).I16(MContent);
        b.Raw(0x03).I16(TDock).Raw(0x00);
        b.Sized(0x24, p => p.I16(MFill).Str("False").I16(CBool));
        b.Raw(0x2E).I16(MChildren);
        b.Raw(0x03).I16(TTextBlock).Raw(0x00);
        b.Sized(0x24, p => p.I16(MDock).Str("Top").I16(CEnum));
        b.Sized(0x10, p => p.Str("Bar"));
        b.Raw(0x04);
        b.Raw(0x03).I16(TTextBox).Raw(0x00);
        b.Sized(0x24, p => p.I16(MDock).Str("Bottom").I16(CEnum));
        b.Raw(0x04);
        b.Raw(0x04).Raw(0x04).Raw(0x02);
        return b.ToArray();
    }

    [Fact]
    public void Baml_dockpanel_dock_and_fill_load()
    {
        using var stream = new MemoryStream(DockBytes());
        var window = XamlLoader.LoadComponent<Window>(stream);
        var dock = Assert.IsType<DockPanel>(window.Content);
        Assert.False(dock.LastChildFill);
        Assert.Equal(2, dock.LogicalChildren.Count);
        Assert.Equal(Dock.Top, DockPanel.GetDock(dock.LogicalChildren[0]));
        Assert.Equal(Dock.Bottom, DockPanel.GetDock(dock.LogicalChildren[1]));
        Assert.Equal("Bar", Assert.IsType<TextBlock>(dock.LogicalChildren[0]).Text);
    }

    [Fact]
    public void Baml_dockpanel_rejects_bad_dock_value()
    {
        // "Sideways" does not fit the "Top" payload, so rebuild the doc.
        var b = new B();
        b.Raw(0x0C, 0x00, 0x00, 0x00)
         .Raw(0x4D, 0x00, 0x53, 0x00, 0x42, 0x00, 0x41, 0x00, 0x4D, 0x00, 0x4C, 0x00)
         .U16(0).U16(0x60).Raw(new byte[] { 0x00, 0x00, 0x60, 0x00, 0x00, 0x00, 0x60, 0x00, 0x01, 0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0x00 });
        b.Raw(0x01);
        b.Sized(0x1C, p => p.U16(0).Str("DockProbe"));
        b.Sized(0x1D, p => p.U16(0).U16(0).Str("DockProbe.MainWindow"));
        b.Raw(0x03).I16(0).Raw(0x00);
        b.Raw(0x2E).I16(MContent);
        b.Raw(0x03).I16(TDock).Raw(0x00);
        b.Raw(0x2E).I16(MChildren);
        b.Raw(0x03).I16(TTextBlock).Raw(0x00);
        b.Sized(0x24, p => p.I16(MDock).Str("Sideways").I16(CEnum));
        b.Raw(0x04).Raw(0x04).Raw(0x04).Raw(0x02);
        using var stream = new MemoryStream(b.ToArray());
        var ex = Assert.Throws<XamlLoadException>(() => XamlLoader.LoadComponent<Window>(stream));
        Assert.Contains("is not a Dock", ex.Message, StringComparison.Ordinal);
    }
}
