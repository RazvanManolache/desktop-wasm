// WC1119: the host ShowPopup suppress site (TryVendorActivate show.Invoke
// catch, WinFormsAppHost.cs) swallows the vendor throw silently, so the
// served sf05f UpdateDropDownButtonBounds throw never surfaces in host
// logs. Stand-ins use fork Control types in a Syncfusion-named namespace
// (IsVendorControl is FullName-gated; no vendor execution): a throwing
// parameterless ShowDropDown drives the exact suppress path headless.
using System.Text;
using WfWasm.CanvasHost;
using WfWasm.Shim;
using FormsControl = System.Windows.Forms.Control;
using FormsForm = System.Windows.Forms.Form;

namespace Syncfusion.WC1119Standins
{
    // Type name contains "DropDown" (TryVendorActivate route) and
    // "ComboBoxAdv" (the WC1119 suppress-log name gate).
    public sealed class ComboBoxAdvDropDownStandin : FormsControl
    {
        public void ShowDropDown() => throw new InvalidOperationException("boom-wc1119-suppress");
    }

    // Same route, outside the log name gate: suppression must stay silent.
    public sealed class SomeOtherDropDownStandin : FormsControl
    {
        public void ShowDropDown() => throw new InvalidOperationException("boom-wc1119-other");
    }
}

public sealed class ShowPopupSuppressLogTests
{
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

    private static string ClickAndCapture(WinFormsAppHost host, int cx, int cy)
    {
        // Full Down -> Up -> Click gesture: the vendor ShowPopup path
        // (TryVendorActivate) runs on the OnClick dispatch, not the press.
        var sb = new StringBuilder();
        var old = Console.Out;
        Console.SetOut(new StringWriter(sb));
        try
        {
            host.OnMouseDown(cx, cy);
            host.OnMouseUp(cx, cy);
            host.OnClick(cx, cy);
        }
        finally
        {
            Console.SetOut(old);
        }

        return sb.ToString();
    }

    [Fact]
    public void ShowPopupSuppress_Logs_Type_And_Stack_For_ComboBoxAdv_Family()
    {
        // FAILS until the WC1119 suppress log lands: the throw from the
        // vendor ShowPopup path must surface as [SUPPRESSED-EXC] with the
        // exception ToString (type + message + stack). Do not "fix" by
        // weakening this test.
        using var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        var form = host.GetOrCreateForm(AppScene.Smoke);
        Assert.NotNull(form);

        using var combo = new Syncfusion.WC1119Standins.ComboBoxAdvDropDownStandin
        {
            Location = new System.Drawing.Point(30, 60),
            Size = new System.Drawing.Size(120, 21),
        };
        form.Controls.Add(combo);
        bool downSeen = false;
        combo.MouseDown += (_, _) => downSeen = true;

        CenterOnForm(form, combo, out int cx, out int cy);
        string log = ClickAndCapture(host, cx, cy);
        form.Controls.Remove(combo);

        Assert.Contains("[SUPPRESSED-EXC]", log);
        Assert.Contains("ComboBoxAdvDropDownStandin", log);
        Assert.Contains("boom-wc1119-suppress", log);
        Assert.True(downSeen, "suppression must not swallow the click: generic dispatch still runs");
    }

    [Fact]
    public void ShowPopupSuppress_Stays_Silent_For_Unrelated_DropDown_Family()
    {
        // Name-gate pin: passes before AND after the WC1119 edit. An
        // unrelated Syncfusion DropDown throw is still suppressed (returns
        // false, click dispatches) but stays out of the log.
        using var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        var form = host.GetOrCreateForm(AppScene.Smoke);
        Assert.NotNull(form);

        using var other = new Syncfusion.WC1119Standins.SomeOtherDropDownStandin
        {
            Location = new System.Drawing.Point(30, 90),
            Size = new System.Drawing.Size(120, 21),
        };
        form.Controls.Add(other);
        bool downSeen = false;
        other.MouseDown += (_, _) => downSeen = true;

        CenterOnForm(form, other, out int cx, out int cy);
        string log = ClickAndCapture(host, cx, cy);
        form.Controls.Remove(other);

        Assert.DoesNotContain("[SUPPRESSED-EXC]", log);
        Assert.True(downSeen, "suppression must not swallow the click: generic dispatch still runs");
    }
}
