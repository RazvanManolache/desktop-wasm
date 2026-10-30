using System.Text;
using DesktopWasm.Core;

namespace DesktopWasm.Wpf.Tests;

/// <summary>DataGrid v1 tests: column sizing, cell text, row selection
/// (programmatic, press, keyboard, INCC-tracked), disabled gating, XAML
/// columns and synthetic BAML.</summary>
[ResetAmbientState]
public sealed class DataGridTests
{
    private sealed class Person
    {
        public string Name { get; set; } = string.Empty;
        public int Age { get; set; }
    }

    private static (WasmInputRouter Router, WpfInputManager Manager, DataGrid Grid) Harness()
    {
        var router = new WasmInputRouter();
        var manager = new WpfInputManager(router);
        var root = new Window();
        var grid = new DataGrid { Width = 400 };
        grid.AddColumn(new DataGridTextColumn { Header = "Name", BindingPath = "Name", Width = GridLength.Pixel(120) });
        grid.AddColumn(new DataGridTextColumn { Header = "Age", BindingPath = "Age", Width = GridLength.Star() });
        grid.AddColumn(new DataGridTextColumn { Header = "Bio", BindingPath = "Name" }); // Auto
        grid.Items.Add(new Person { Name = "Ada", Age = 36 });
        grid.Items.Add(new Person { Name = "Grace", Age = 85 });
        root.Content = grid;
        manager.AddRoot(root);
        var mil = new WasmMilCore(new DesktopWasm.Graphics.SkiaSurface(400, 600));
        mil.Render(root, new WpfSize(400, 600), new WpfRect(0, 0, 400, 600));
        return (router, manager, grid);
    }

    private static readonly WasmWindowHandle Handle = new(1);

    private static void Down(WasmInputRouter router, double x, double y) =>
        router.DispatchPointer(new WasmPointerArgs(Handle, x, y, WasmPointerButton.Left, true));

    private static void Key(WasmInputRouter router, string key) =>
        router.DispatchKeyboard(new WasmKeyArgs(Handle, key, true, false, false, false));

    [Fact]
    public void Layout_resolves_pixel_star_auto_and_row_geometry()
    {
        var (_, _, grid) = Harness();
        var widths = grid.ColumnWidths;
        Assert.Equal(3, widths.Count);
        Assert.Equal(120, widths[0]); // pixel
        Assert.True(widths[2] > 0); // auto: widest header/cell text + padding
        Assert.Equal(400 - 2 - 120 - widths[2], widths[1], 6); // star takes the rest
        Assert.Equal(widths.Sum() + 2, grid.DesiredSize.Width); // columns + 1px borders

        Assert.Equal(2, grid.RowRects.Count);
        var r0 = grid.RowRects[0];
        var r1 = grid.RowRects[1];
        Assert.Equal(398, r0.Width); // full width inside the 1px border
        Assert.True(r0.Y > 1); // below the header band
        Assert.Equal(r0.Y + r0.Height, r1.Y);
    }

    [Fact]
    public void Programmatic_selection_sets_reference_item_and_fires_once()
    {
        var (_, _, grid) = Harness();
        var fires = 0;
        grid.SelectionChanged += () => fires++;
        var ada = grid.Items[0];

        grid.SelectedIndex = 0;
        Assert.Equal(0, grid.SelectedIndex);
        Assert.Same(ada, grid.SelectedItem);
        Assert.Equal(1, fires);

        grid.SelectedIndex = 0; // same value: silent
        Assert.Equal(1, fires);

        grid.SelectedItem = null;
        Assert.Null(grid.SelectedItem);
        Assert.Equal(2, fires);
    }

    [Fact]
    public void Out_of_range_and_foreign_selection_coerce_to_none()
    {
        var (_, _, grid) = Harness();
        var fires = 0;
        grid.SelectionChanged += () => fires++;

        grid.SelectedIndex = 99;
        Assert.Equal(-1, grid.SelectedIndex);
        Assert.Null(grid.SelectedItem);

        grid.SelectedItem = new Person { Name = "Foreign" };
        Assert.Equal(-1, grid.SelectedIndex);
        Assert.Equal(0, fires); // never effective: no event
    }

    [Fact]
    public void Selection_materializes_when_rows_arrive_late()
    {
        var (_, _, grid) = Harness();
        grid.Items.Clear();
        var fires = 0;
        grid.SelectionChanged += () => fires++;
        grid.SelectedIndex = 1;
        Assert.Equal(-1, grid.SelectedIndex); // no rows yet
        Assert.Equal(0, fires);

        grid.Items.Add(new Person { Name = "a" });
        Assert.Equal(-1, grid.SelectedIndex);
        grid.Items.Add(new Person { Name = "b" });
        Assert.Equal(1, grid.SelectedIndex);
        Assert.Equal("b", ((Person?)grid.SelectedItem)?.Name);
        Assert.Equal(1, fires);
    }

    [Fact]
    public void Removing_rows_tracks_selection_and_reset_clears()
    {
        var (_, _, grid) = Harness();
        var fires = 0;
        grid.SelectedIndex = 1; // Grace
        grid.SelectionChanged += () => fires++;

        grid.Items.RemoveAt(0); // row above shifts the index; Grace stays
        Assert.Equal(0, grid.SelectedIndex);
        Assert.Equal("Grace", ((Person?)grid.SelectedItem)?.Name);
        Assert.Equal(1, fires);

        grid.Items.RemoveAt(0); // the selected row: next slides in (none left)
        Assert.Equal(-1, grid.SelectedIndex);
        Assert.Null(grid.SelectedItem);
        Assert.Equal(2, fires);

        grid.Items.Add(new Person { Name = "x" });
        grid.SelectedIndex = 0;
        grid.Items.Clear(); // Reset drops everything
        Assert.Equal(-1, grid.SelectedIndex);
    }

    [Fact]
    public void Click_selects_row_and_misses_leave_selection()
    {
        var (router, _, grid) = Harness();
        var r1 = grid.RowRects[1];
        var fires = 0;
        grid.SelectionChanged += () => fires++;

        Down(router, 200, r1.Y + r1.Height / 2);
        Assert.Equal(1, grid.SelectedIndex);
        Assert.Equal(1, fires);

        Down(router, 200, r1.Y + r1.Height + 50); // below the rows
        Assert.Equal(1, grid.SelectedIndex);
        Assert.Equal(1, fires);
    }

    [Fact]
    public void Click_ignored_when_disabled()
    {
        var (router, _, grid) = Harness();
        var r0 = grid.RowRects[0];
        grid.IsEnabled = false;
        Down(router, 200, r0.Y + r0.Height / 2);
        Assert.Equal(-1, grid.SelectedIndex);
    }

    [Fact]
    public void Keyboard_moves_selection_only_when_focused()
    {
        var (router, manager, grid) = Harness();
        Key(router, "ArrowDown");
        Assert.Equal(-1, grid.SelectedIndex); // no focus yet

        Assert.True(manager.TryFocus(grid));
        Key(router, "ArrowDown");
        Assert.Equal(0, grid.SelectedIndex);
        Key(router, "ArrowDown");
        Assert.Equal(1, grid.SelectedIndex);
        Key(router, "ArrowUp");
        Assert.Equal(0, grid.SelectedIndex);
        Key(router, "End");
        Assert.Equal(1, grid.SelectedIndex);
        Key(router, "Home");
        Assert.Equal(0, grid.SelectedIndex);
    }

    [Fact]
    public void CellText_reads_paths_and_blanks_missing_values()
    {
        var (_, _, grid) = Harness();
        var name = grid.Columns[0];
        var row = new Person { Name = "Ada", Age = 36 };
        Assert.Equal("Ada", grid.CellText(row, name));
        Assert.Equal("36", grid.CellText(row, grid.Columns[1]));
        Assert.Equal(string.Empty, grid.CellText(row, new DataGridTextColumn { BindingPath = "Nope" }));
        Assert.Equal(string.Empty, grid.CellText(row, new DataGridTextColumn()));
        Assert.Equal(string.Empty, grid.CellText(null, name));
    }

    [Fact]
    public void Adding_rows_re_measures_without_manual_invalidate()
    {
        var (_, _, grid) = Harness();
        Assert.False(grid.IsMeasureDirty); // harness arranged clean
        grid.Items.Add(new Person { Name = "Katherine", Age = 47 });
        Assert.True(grid.IsMeasureDirty); // INCC subscription re-marks
    }

    [Fact]
    public void Xaml_parses_columns_with_kinds()
    {
        var result = XamlLoader.Load("""
            <Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
              <DataGrid>
                <DataGrid.Columns>
                  <DataGridTextColumn Header="Name" BindingPath="Name" Width="120"/>
                  <DataGridTextColumn Header="Age" BindingPath="Age" Width="*"/>
                  <DataGridTextColumn Header="Bio" BindingPath="Name"/>
                </DataGrid.Columns>
              </DataGrid>
            </Window>
            """);
        var window = Assert.IsType<Window>(result.Root);
        var grid = Assert.IsType<DataGrid>(window.Content);
        Assert.Equal(3, grid.Columns.Count);
        Assert.Equal("Name", grid.Columns[0].Header);
        Assert.Equal("Name", grid.Columns[0].BindingPath);
        Assert.Equal(GridLength.Pixel(120), grid.Columns[0].Width);
        Assert.Equal(GridLength.Star(), grid.Columns[1].Width);
        Assert.Equal(GridLength.Auto, grid.Columns[2].Width); // default
    }

    [Fact]
    public void Xaml_rejects_bad_columns()
    {
        var badWidth = Assert.Throws<XamlLoadException>(() => XamlLoader.Load("""
            <Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
              <DataGrid><DataGrid.Columns>
                <DataGridTextColumn Header="X" Width="wide"/>
              </DataGrid.Columns></DataGrid>
            </Window>
            """));
        Assert.Contains("Width", badWidth.Message);

        var badAttr = Assert.Throws<XamlLoadException>(() => XamlLoader.Load("""
            <Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
              <DataGrid><DataGrid.Columns>
                <DataGridTextColumn Nope="1"/>
              </DataGrid.Columns></DataGrid>
            </Window>
            """));
        Assert.Contains("Header/Binding/BindingPath/SortMemberPath/Width", badAttr.Message);

        var direct = Assert.Throws<XamlLoadException>(() => XamlLoader.Load("""
            <Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
              <DataGrid><DataGridTextColumn Header="X"/></DataGrid>
            </Window>
            """));
        Assert.Contains("DataGrid.Columns", direct.Message);

        var nonColumn = Assert.Throws<XamlLoadException>(() => XamlLoader.Load("""
            <Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
              <DataGrid><DataGrid.Columns><Button Content="nope"/></DataGrid.Columns></DataGrid>
            </Window>
            """));
        Assert.Contains("DataGridTextColumn", nonColumn.Message);
    }

    // Minimal synthetic-MS-BAML builder (same framing as the other suites).
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
        .Raw(new byte[] { 0x00, 0x00, 0x60, 0x00, 0x00, 0x00, 0x60, 0x00, 0x01, 0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0x00 })
        .ToArray();

    [Fact]
    public void Baml_datagrid_loads_columns_and_pending_selection()
    {
        // DataGrid/DataGridTextColumn have no well-known short ids (verified
        // against a real fixture: they always travel by TypeInfo).
        const string pf = "PresentationFramework";
        const string tGrid = "System.Windows.Controls.DataGrid";
        const string tCol = "System.Windows.Controls.DataGridTextColumn";
        const short mContent = -14; // ContentControl.Content
        const short cString = -615;
        var b = new B();
        b.Raw(Header()).Raw(0x01);
        b.Sized(0x1C, p => p.U16(0).Str("HelloWorld, Version=1.0.0.0"));
        b.Sized(0x1C, p => p.U16(1).Str(pf));
        b.Sized(0x1D, p => p.U16(0).U16(0).Str("HelloWorld.MainWindow"));
        b.Sized(0x1D, p => p.U16(1).U16(1).Str(tGrid));
        b.Sized(0x1D, p => p.U16(2).U16(1).Str(tCol));
        b.Raw(0x03).I16(0).Raw(0x00);
        b.Raw(0x2E).I16(mContent);
        b.Raw(0x03).I16(1).Raw(0x00); // DataGrid by TypeInfo id 1
        b.Sized(0x1F, p => p.U16(0).I16(-553).Raw(0x00).Str("SelectedIndex"));
        b.Sized(0x24, p => p.I16(0).Str("1").I16(0)); // any int32 converter
        b.Sized(0x1F, p => p.U16(1).I16(1).Raw(0x00).Str("Columns"));
        b.Sized(0x1F, p => p.U16(2).I16(2).Raw(0x00).Str("Header"));
        b.Sized(0x1F, p => p.U16(3).I16(2).Raw(0x00).Str("Width"));
        b.Raw(0x0B).U16(1); // PropertyIListStart(Columns attr)
        foreach (var (header, width) in new[] { ("Name", "120"), ("Age", "*") })
        {
            b.Raw(0x03).I16(2).Raw(0x00); // DataGridTextColumn by TypeInfo id 2
            b.Sized(0x24, p => p.I16(2).Str(header).I16(cString));
            b.Sized(0x24, p => p.I16(3).Str(width).I16(0)); // converter by TypeInfo
            b.Raw(0x04);
        }
        b.Raw(0x0C); // PropertyIListEnd
        b.Raw(0x04).Raw(0x04).Raw(0x02);

        using var stream = new MemoryStream(b.ToArray());
        var window = XamlLoader.LoadComponent<Window>(stream);
        var grid = Assert.IsType<DataGrid>(window.Content);
        Assert.Equal(["Name", "Age"], grid.Columns.Select(c => c.Header));
        Assert.Equal(GridLength.Pixel(120), grid.Columns[0].Width);
        Assert.Equal(GridLength.Star(), grid.Columns[1].Width);
        // No rows in BAML (real BAML cannot carry row data either): the
        // SelectedIndex attribute stays pending until rows arrive in code.
        Assert.Equal(-1, grid.SelectedIndex);
        grid.Items.Add(new Person { Name = "a" });
        grid.Items.Add(new Person { Name = "b" });
        Assert.Equal(1, grid.SelectedIndex);
    }

    [Fact]
    public void Baml_rejects_complex_binding_property()
    {
        // A real Binding arrives as a complex-property subtree (record 0x07),
        // which needs markup-extension support: precise boundary, not a gap.
        const string pf = "PresentationFramework";
        const string tGrid = "System.Windows.Controls.DataGrid";
        var b = new B();
        b.Raw(Header()).Raw(0x01);
        b.Sized(0x1C, p => p.U16(0).Str("HelloWorld, Version=1.0.0.0"));
        b.Sized(0x1C, p => p.U16(1).Str(pf));
        b.Sized(0x1D, p => p.U16(0).U16(0).Str("HelloWorld.MainWindow"));
        b.Sized(0x1D, p => p.U16(1).U16(1).Str(tGrid));
        b.Raw(0x03).I16(0).Raw(0x00);
        b.Raw(0x2E).I16(-14);
        b.Raw(0x03).I16(1).Raw(0x00);
        b.Sized(0x1F, p => p.U16(0).I16(1).Raw(0x00).Str("Columns"));
        b.Raw(0x0B).U16(0);
        b.Raw(0x07).Raw(0x00); // complex property: Binding markup extension

        using var stream = new MemoryStream(b.ToArray());
        var ex = Assert.Throws<XamlLoadException>(() => XamlLoader.LoadComponent<Window>(stream));
        Assert.Contains("0x07", ex.Message);
    }

    [Fact]
    public void Baml_rejects_non_column_in_columns_list()
    {
        const string pf = "PresentationFramework";
        const string tGrid = "System.Windows.Controls.DataGrid";
        const short tTextBlock = -638;
        var b = new B();
        b.Raw(Header()).Raw(0x01);
        b.Sized(0x1C, p => p.U16(0).Str("HelloWorld, Version=1.0.0.0"));
        b.Sized(0x1C, p => p.U16(1).Str(pf));
        b.Sized(0x1D, p => p.U16(0).U16(0).Str("HelloWorld.MainWindow"));
        b.Sized(0x1D, p => p.U16(1).U16(1).Str(tGrid));
        b.Raw(0x03).I16(0).Raw(0x00);
        b.Raw(0x2E).I16(-14);
        b.Raw(0x03).I16(1).Raw(0x00);
        b.Sized(0x1F, p => p.U16(0).I16(1).Raw(0x00).Str("Columns"));
        b.Raw(0x0B).U16(0);
        b.Raw(0x03).I16(tTextBlock).Raw(0x00);
        b.Raw(0x04).Raw(0x0C).Raw(0x04).Raw(0x04).Raw(0x02);

        using var stream = new MemoryStream(b.ToArray());
        var ex = Assert.Throws<XamlLoadException>(() => XamlLoader.LoadComponent<Window>(stream));
        Assert.Contains("DataGridTextColumn", ex.Message);
    }
}
