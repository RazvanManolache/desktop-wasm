extern alias WasmDraw;
using System;
using System.Data;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using Xunit;
using Xunit.Abstractions;

namespace WfWasm.WinForms.Tests;

public class SyncfusionVirtualListViewTests
{
    private readonly ITestOutputHelper _output;

    public SyncfusionVirtualListViewTests(ITestOutputHelper output)
    {
        _output = output;
    }
    private static readonly string StagingDir = Path.GetFullPath(Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory,
        @"..\..\..\..\..\inputs\winforms\syncfusion-staging\syncfusion-auto-complete"));

    static SyncfusionVirtualListViewTests()
    {
        AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
        {
            var reqName = new AssemblyName(args.Name).Name;
            if (reqName == "System.Windows.Forms" || reqName == "System.Windows.Forms.Primitives")
            {
                return typeof(System.Windows.Forms.Form).Assembly;
            }
            if (reqName == "System.Drawing" || reqName == "System.Drawing.Common" || reqName == "System.Drawing.Primitives")
            {
                return typeof(WasmDraw::System.Drawing.Point).Assembly;
            }
            var name = reqName + ".dll";
            var path = Path.Combine(StagingDir, name);
            if (File.Exists(path))
            {
                return Assembly.LoadFrom(path);
            }
            return null;
        };
    }

    [Fact]
    public void TestVirtualListViewRetrieveVirtualItemDirectly()
    {
        var dllPath = Path.Combine(StagingDir, "Syncfusion.Tools.Windows.dll");
        Assert.True(File.Exists(dllPath), $"File not found: {dllPath}");
        var asm = Assembly.LoadFrom(dllPath);
        var vlvType = asm.GetType("Syncfusion.Windows.Forms.Tools.VirtualListView");
        Assert.NotNull(vlvType);

        using var vlv = (ListView)Activator.CreateInstance(vlvType)!;
        _ = vlv.Handle; // force handle

        // Initially VirtualMode is true, VirtualListSize is 0
        Assert.True(vlv.VirtualMode);
        Assert.Equal(0, vlv.VirtualListSize);

        // What happens if VirtualListSize is set to 2 without DataSource?
        vlv.VirtualListSize = 2;
        var exNoDataSource = Assert.Throws<InvalidOperationException>(() => vlv.Items[0]);
        Assert.Contains("ListView virtualization requires a valid ListViewItem", exNoDataSource.Message);

        // Now set DataSource to a DataView
        var dt = new DataTable();
        dt.Columns.Add("Flag");
        dt.Columns.Add("Country");
        dt.Rows.Add("USA", "0");
        dt.Rows.Add("UK", "1");

        vlv.Columns.Add("Flag", 150);
        vlv.Columns.Add("Country", 150);

        var propImg = vlvType.GetProperty("ImageColumnIndex");
        propImg!.SetValue(vlv, 1);

        var prop = vlvType.GetProperty("DataSource");
        prop!.SetValue(vlv, dt.DefaultView);

        // What is VirtualListSize now?
        Assert.Equal(2, vlv.VirtualListSize);
        Assert.Equal(2, vlv.Items.Count);

        // Now access Items[0]
        var item0 = vlv.Items[0];
        Assert.NotNull(item0);
        Assert.Equal("USA", item0.Text);

        // What happens if VirtualListSize > m_view.Count?
        // VirtualListView accesses m_view[3], throwing IndexOutOfRangeException from DataView
        vlv.VirtualListSize = 5;
        var exOutOfBounds = Assert.Throws<IndexOutOfRangeException>(() => vlv.Items[3]);
        Assert.NotNull(exOutOfBounds);
    }

    [Fact]
    public void TestVirtualListViewFocusedItemBehaviorInVirtualMode()
    {
        var dllPath = Path.Combine(StagingDir, "Syncfusion.Tools.Windows.dll");
        Assert.True(File.Exists(dllPath), $"File not found: {dllPath}");
        var asm = Assembly.LoadFrom(dllPath);
        var vlvType = asm.GetType("Syncfusion.Windows.Forms.Tools.VirtualListView")!;
        using var vlv = (ListView)Activator.CreateInstance(vlvType)!;
        _ = vlv.Handle;

        // In virtual mode without DataSource, RetrieveVirtualItem cannot provide items.
        vlv.VirtualListSize = 3;

        // Reproduce: ListView in virtual mode without items throws when FocusedItem is evaluated
        // if an item index has focus, or direct Items[i] throws InvalidOperationException.
        var ex = Assert.Throws<InvalidOperationException>(() => _ = vlv.Items[0]);
        Assert.Contains("ListView virtualization requires a valid ListViewItem", ex.Message);
    }
}
