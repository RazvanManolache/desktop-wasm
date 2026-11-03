using System.Reflection;
using System.Windows.Forms;
using WfWasm.CanvasHost;
using WfWasm.CanvasHost.Pages;
using WfWasm.Renderer;
using WfWasm.Shim;
using Form = System.Windows.Forms.Form;
using ListView = System.Windows.Forms.ListView;

namespace WfWasm.CanvasHost.Tests;

public sealed class VirtualListViewContainmentTests : IDisposable
{
    public VirtualListViewContainmentTests()
    {
        WasmRepaintGate.DrainDirty(out _);
    }

    public void Dispose()
    {
        WasmRepaintGate.DrainDirty(out _);
    }

    [Fact]
    public void ListView_VirtualMode_MissingItem_DoesNotThrowDuringRender()
    {
        using var host = new WinFormsAppHost();
        using var form = new Form { Width = 300, Height = 300, Visible = true };
        using var listView = new ListView
        {
            Width = 200,
            Height = 200,
            Visible = true,
            VirtualMode = true,
            VirtualListSize = 5
        };
        listView.Columns.Add("Col1", 100);
        form.Controls.Add(listView);

        // In virtual mode with no RetrieveVirtualItem handler, listView.Items[0] throws
        // InvalidOperationException("ListView virtualization requires a valid ListViewItem...").
        // Ensure that direct indexer throws to verify the reproduction condition.
        var ex = Record.Exception(() => { _ = listView.Items[0]; });
        Assert.NotNull(ex);
        Assert.IsType<InvalidOperationException>(ex);

        // Now test host rendering: RenderControls must contain indexer failures,
        // avoiding an unhandled throw that aborts paint and wedges the pump.
        typeof(WinFormsAppHost)
            .GetField("_activeForm", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(host, form);

        var renderer = new RecordingRenderer();
        WasmRepaintGate.MarkDirty(form);
        var paintEx = Record.Exception(() => host.Paint(renderer));

        Assert.Null(paintEx);
        Assert.True(renderer.Commands.Count > 0, "Host must successfully record canvas paint commands.");
    }

    [Fact]
    public void Paint_FrameDirty_AlwaysClearedEvenOnFault()
    {
        using var host = new WinFormsAppHost();
        using var form = new Form { Width = 300, Height = 300, Visible = true };

        typeof(WinFormsAppHost)
            .GetField("_activeForm", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(host, form);

        form.Controls.Add(new System.Windows.Forms.Label { Text = "injected-paint-fault", Visible = true });
        WasmRepaintGate.MarkDirty(form);
        var renderer = DispatchProxy.Create<ICanvas, PaintFaultProxy>();
        Assert.Throws<InvalidOperationException>(() => host.Paint(renderer));
        // IsFrameDirty deliberately returns true outside a frame. Inspect the
        // actual snapshot and inject a fault while it is live instead.
        Assert.Null(FrameSnapshot.GetValue(null));
    }

    [Fact]
    public async Task Home_OnKeyUp_Fault_DoesNotThrow()
    {
        var home = new Home();
        var keyUpMethod = typeof(Home).GetMethod("OnKeyUp", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(keyUpMethod);

        // Calling OnKeyUp with uninitialized state should be caught by the new try/catch guard
        // rather than throwing an unhandled exception into the Blazor circuit.
        var e = new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "u" };
        try
        {
            var task = (Task)keyUpMethod.Invoke(home, new object[] { e })!;
            var ex = await Record.ExceptionAsync(() => task);
            Assert.Null(ex);
        }
        finally
        {
            // Dispose the component and owned host even though this test
            // deliberately bypasses normal component initialization.
            typeof(Home).GetProperty("State", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
                .SetValue(home, new SceneState());
            home.Dispose();
            ((WinFormsAppHost)typeof(Home).GetField("_winFormsHost", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(home)!).Dispose();
        }
    }

    private static readonly FieldInfo FrameSnapshot = typeof(WinFormsAppHost)
        .GetField("s_frameDirty", BindingFlags.Static | BindingFlags.NonPublic)!;

    public class PaintFaultProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method?.Name == nameof(ICanvas.DrawTextAligned) && args?[0] as string == "injected-paint-fault")
            {
                Assert.NotNull(FrameSnapshot.GetValue(null));
                throw new InvalidOperationException("injected-paint-fault");
            }
            return null;
        }
    }

    [Fact]
    public void ListView_VirtualMode_RowClick_DoesNotThrow()
    {
        // Original-app repro (verifier syncfusion-unfreeze logs show this
        // exact text 6-7x per popup interaction): a virtual-mode ListView
        // with no valid RetrieveVirtualItem item throws from the indexer.
        // The click pump must contain it exactly like the render path does.
        using var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        var form = host.GetOrCreateForm(AppScene.Smoke)!;
        using var listView = new ListView
        {
            Name = "virtClickList",
            Visible = true,
            VirtualMode = true,
            VirtualListSize = 5
        };
        listView.Columns.Add("Col1", 100);
        listView.SetBounds(20, 80, 200, 160);
        form.Controls.Add(listView);

        var indexerEx = Record.Exception(() => { _ = listView.Items[0]; });
        Assert.IsType<InvalidOperationException>(indexerEx);

        // First body row: 24px header + 22px rows; +30 matches the scene
        // chrome convention used by PopupFocusContainmentTests.
        int x = form.Location.X + listView.Left + (listView.Width / 2);
        int y = form.Location.Y + 30 + listView.Top + 24 + 11;

        // Deterministic: drive the patched dispatch directly at row 0.
        var dispatch = typeof(WinFormsAppHost).GetMethod("DispatchControlClick", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var dispatchEx = Record.Exception(() => dispatch.Invoke(host,
            new object?[] { listView, x, y, System.Windows.Forms.MouseButtons.Left, 1, form.Location.X, form.Location.Y + 30 }));
        Assert.Null(dispatchEx);

        // End to end through the public input pump.
        var clickEx = Record.Exception(() => host.OnClick(x, y));
        Assert.Null(clickEx);
    }
}
