using WfWasm.CanvasHost;
using WfWasm.Renderer;
using WfWasm.Shim;
using FormsButton = System.Windows.Forms.Button;
using FormsForm = System.Windows.Forms.Form;
using FormsPanel = System.Windows.Forms.Panel;

/// <summary>
/// UNSTALL1510: Paint Colors More/Less. The dialog carries a More section
/// of extra controls low in the client area; collapse shrinks the form so
/// they fall outside the client rect. Desktop clips them; the host must
/// too — by repainting on geometry change AND by skipping outside-client
/// children in the dialog paint path. No Visible=false, no control
/// deletion. Companion proof: closing a dialog must also invalidate, or
/// the dead dialog's pixels persist (fork Close marks nothing).
/// </summary>
public sealed class ColorsCollapseClipTests
{
    [Fact]
    public void FirstPaintPreservesExplicitCollapsedWidthAndHeight()
    {
        var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        var dlg = new FormsForm { Text = "Colors", Width = 209, Height = 217, AutoSize = false };
        dlg.Controls.Add(new FormsButton { Text = "RGB outside", Left = 320, Top = 24, Width = 56, Height = 23 });
        dlg.Controls.Add(new FormsButton { Text = "Alpha outside", Left = 20, Top = 240, Width = 100, Height = 23 });
        dlg.Show();
        try
        {
            var renderer = new RecordingRenderer();
            host.Paint(renderer);
            Assert.Equal(209, dlg.Width);
            Assert.Equal(217, dlg.Height);
            Assert.DoesNotContain(renderer.Commands.OfType<DrawTextCommand>(), c =>
                c.Text is "RGB outside" or "Alpha outside");
        }
        finally { Cleanup(host, dlg); }
    }

    // Paint.NET Colors shape: an extra section low in a 300-tall dialog.
    private static (WinFormsAppHost host, FormsForm dlg, FormsButton extra) CreateColorsDialog()
    {
        var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        var dlg = new FormsForm { Text = "Colors", Width = 400, Height = 300 };
        var extra = new FormsButton
        {
            Name = "opacityValue",
            Text = "Opacity",
            Left = 20,
            Top = 250,
            Width = 120,
            Height = 30,
        };
        dlg.Controls.Add(extra);
        dlg.ShowDialog();
        return (host, dlg, extra);
    }

    private static void Cleanup(WinFormsAppHost host, FormsForm dlg)
    {
        System.Windows.Forms.WasmDialogHost.Untrack(dlg);
        host.Dispose();
        dlg.Dispose();
    }

    [Fact]
    public void Dialog_Resize_Marks_Repaint_Gate_Dirty()
    {
        // Collapse with no dirty mark = no frame paints = stale child
        // pixels stay visible outside the shrunken form. The host must
        // invalidate on dialog resize.
        var (host, dlg, extra) = CreateColorsDialog();
        try
        {
            host.Paint(new RecordingRenderer());
            System.Windows.Forms.WasmRepaintGate.DrainDirty(out _);
            Assert.False(System.Windows.Forms.WasmRepaintGate.HasDirty);
            dlg.Height = 190;
            Assert.True(
                System.Windows.Forms.WasmRepaintGate.HasDirty,
                "dialog collapse must invalidate the repaint gate");
        }
        finally
        {
            Cleanup(host, dlg);
        }
    }

    [Fact]
    public void Collapsed_Dialog_Skips_Children_Outside_Client()
    {
        // The dialog paint path intersects children with the actual
        // post-collapse client rect: the extra section paints expanded but
        // must vanish collapsed.
        var (host, dlg, extra) = CreateColorsDialog();
        try
        {
            var expanded = new RecordingRenderer();
            host.Paint(expanded);
            Assert.Contains(
                expanded.Commands.OfType<DrawTextCommand>(),
                c => c.Text == "Opacity");
            dlg.Height = 190;
            var collapsed = new RecordingRenderer();
            host.Paint(collapsed);
            Assert.DoesNotContain(
                collapsed.Commands.OfType<DrawTextCommand>(),
                c => c.Text == "Opacity");
            int childAbsY = dlg.Location.Y + 30 + extra.Top;
            Assert.DoesNotContain(
                collapsed.Commands.OfType<FillRoundedRectCommand>(),
                c => c.Y == childAbsY && c.X == dlg.Location.X + extra.Left);
        }
        finally
        {
            Cleanup(host, dlg);
        }
    }

    [Fact]
    public void Collapsed_Dialog_Skips_Nested_Children_Outside_Client()
    {
        // Ancestor intersection must hold at depth: a grandchild below the
        // collapsed client bottom vanishes even when its panel parent
        // straddles the boundary.
        var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        var dlg = new FormsForm { Text = "Colors", Width = 400, Height = 300 };
        var panel = new FormsPanel { Left = 10, Top = 150, Width = 380, Height = 140 };
        var deep = new FormsButton
        {
            Name = "deepValue",
            Text = "DeepOpacity",
            Left = 10,
            Top = 100,
            Width = 120,
            Height = 30,
        };
        panel.Controls.Add(deep);
        dlg.Controls.Add(panel);
        dlg.ShowDialog();
        try
        {
            var expanded = new RecordingRenderer();
            host.Paint(expanded);
            Assert.Contains(
                expanded.Commands.OfType<DrawTextCommand>(),
                c => c.Text == "DeepOpacity");
            dlg.Height = 190;
            var collapsed = new RecordingRenderer();
            host.Paint(collapsed);
            Assert.DoesNotContain(
                collapsed.Commands.OfType<DrawTextCommand>(),
                c => c.Text == "DeepOpacity");
        }
        finally
        {
            Cleanup(host, dlg);
        }
    }

    [Fact]
    public void Dialog_OK_Close_Marks_Repaint_Gate_Dirty()
    {
        // UNSTALL1510 residual: fork Close marks nothing, so a closed
        // dialog's pixels persist until an unrelated repaint. The settled
        // OK completion (CloseModalIfSettled on the real click path) must
        // invalidate. Driven end-to-end: real Button click -> vendor Click
        // sets DialogResult -> product completion closes + marks.
        var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        var dlg = new FormsForm { Text = "Colors", Width = 400, Height = 300 };
        var ok = new FormsButton
        {
            Name = "okButton",
            Text = "OK",
            Left = 200,
            Top = 100,
            Width = 85,
            Height = 24,
        };
        ok.Click += (_, _) => { dlg.DialogResult = System.Windows.Forms.DialogResult.OK; };
        dlg.Controls.Add(ok);
        dlg.ShowDialog();
        try
        {
            host.Paint(new RecordingRenderer());
            System.Windows.Forms.WasmRepaintGate.DrainDirty(out _);
            Assert.False(System.Windows.Forms.WasmRepaintGate.HasDirty);
            int cx = dlg.Location.X + ok.Left + (ok.Width / 2);
            int cy = dlg.Location.Y + 30 + ok.Top + (ok.Height / 2);
            host.OnClick(cx, cy);
            Assert.False(
                System.Windows.Forms.WasmDialogHost.TopModal(out _),
                "OK click must settle and close the modal");
            Assert.True(
                System.Windows.Forms.WasmRepaintGate.HasDirty,
                "dialog close must invalidate the repaint gate");
        }
        finally
        {
            Cleanup(host, dlg);
        }
    }

    [Fact]
    public void Dialog_SelfHide_Completion_Marks_Repaint_Gate_Dirty()
    {
        // Discriminating test: the documented vendor self-hide path
        // (hide/dispose in the OK handler, bypassing FormClosed) with no
        // host click involved, so no incidental click/focus mark exists.
        // Only the CloseModalIfSettled completion can invalidate here:
        // fails before the UNSTALL1510 marks, passes after.
        var (host, dlg, extra) = CreateColorsDialog();
        try
        {
            host.Paint(new RecordingRenderer());
            dlg.DialogResult = System.Windows.Forms.DialogResult.OK;
            dlg.Hide();
            System.Windows.Forms.WasmRepaintGate.DrainDirty(out _);
            Assert.False(System.Windows.Forms.WasmRepaintGate.HasDirty);
            var completion = typeof(WinFormsAppHost).GetMethod(
                "CloseModalIfSettled",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Assert.NotNull(completion);
            completion.Invoke(null, new object[] { dlg });
            Assert.True(
                System.Windows.Forms.WasmRepaintGate.HasDirty,
                "self-hide completion must invalidate the repaint gate");
        }
        finally
        {
            Cleanup(host, dlg);
        }
    }

    [Fact]
    public void Dialog_X_Close_Marks_Repaint_Gate_Dirty()
    {
        // Same residual through the chrome X zone (ModalOnClick close arm,
        // not CloseModalIfSettled): closing must invalidate as well.
        var (host, dlg, extra) = CreateColorsDialog();
        try
        {
            host.Paint(new RecordingRenderer());
            System.Windows.Forms.WasmRepaintGate.DrainDirty(out _);
            Assert.False(System.Windows.Forms.WasmRepaintGate.HasDirty);
            host.OnClick(dlg.Location.X + dlg.Width - 19, dlg.Location.Y + 15);
            Assert.False(
                System.Windows.Forms.WasmDialogHost.TopModal(out _),
                "X click must close the modal");
            Assert.True(
                System.Windows.Forms.WasmRepaintGate.HasDirty,
                "X close must invalidate the repaint gate");
        }
        finally
        {
            Cleanup(host, dlg);
        }
    }
}
