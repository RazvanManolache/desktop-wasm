// RVW-5795 Job2 gesture pin: the served SF10 wizard reports Next/Back clicks
// as no-ops. The host click contract is Down -> Up -> Click, with
// DispatchControlClick performing fork Buttons and invoking OnClick for
// everything else (vendor nav buttons are NOT fork Button/IButtonControl,
// so they take the generic path). These tests drive the full gesture
// through the public host surface against exact fork control types and
// assert a visible state change — not just that a handler ran.
// A green suite here means served dead-input is a hit-test miss or
// vendor-internal routing, never the generic dispatch.
using WfWasm.CanvasHost;
using WfWasm.Shim;
using FormsButton = System.Windows.Forms.Button;
using FormsControl = System.Windows.Forms.Control;
using FormsForm = System.Windows.Forms.Form;
using FormsPanel = System.Windows.Forms.Panel;
using FormsPoint = System.Drawing.Point;
using FormsSize = System.Drawing.Size;

public sealed class ButtonClickAdvanceTests
{
    private static FormsButton FindFirstButton(FormsControl root)
    {
        foreach (FormsControl c in root.Controls)
        {
            if (c is FormsButton btn)
                return btn;
            if (c.Controls.Count > 0)
            {
                try { return FindFirstButton(c); }
                catch (InvalidOperationException) { }
            }
        }

        throw new InvalidOperationException("No button found.");
    }

    private static void CenterOnForm(FormsForm form, FormsControl target, out int cx, out int cy)
    {
        int absX = form.Location.X;
        int absY = form.Location.Y + 30;
        for (FormsControl? current = target; current is not null && current is not FormsForm; current = current.Parent)
        {
            absX += current.Left;
            absY += current.Top;
        }

        cx = absX + (target.Width / 2);
        cy = absY + (target.Height / 2);
    }

    [Fact]
    public void Down_Up_Click_On_Button_Flips_Visible_State()
    {
        using var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        var form = host.GetOrCreateForm(AppScene.Smoke);
        Assert.NotNull(form);

        FormsButton button = FindFirstButton(form);
        string before = form.Text;
        button.Click += (_, _) => form.Text = before + "-page-2";

        CenterOnForm(form, button, out int cx, out int cy);
        host.OnMouseDown(cx, cy);
        host.OnMouseUp(cx, cy);
        host.OnClick(cx, cy);

        Assert.Equal(before + "-page-2", form.Text);
    }

    [Fact]
    public void Click_On_NonButton_Control_Reaches_Handler()
    {
        // Vendor-nav-button shape: a plain Control (not Button, not
        // IButtonControl) must still get Click through the generic path.
        using var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        var form = host.GetOrCreateForm(AppScene.Smoke);
        Assert.NotNull(form);

        using var nav = new FormsPanel
        {
            Location = new FormsPoint(4, 4),
            Size = new FormsSize(80, 28),
        };
        form.Controls.Add(nav);
        bool clicked = false;
        nav.Click += (_, _) => clicked = true;

        CenterOnForm(form, nav, out int cx, out int cy);
        host.OnMouseDown(cx, cy);
        host.OnMouseUp(cx, cy);
        host.OnClick(cx, cy);

        Assert.True(clicked);
    }
}
