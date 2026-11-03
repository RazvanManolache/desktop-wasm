// WC0755 triage: Syncfusion ComboBoxAdv popup-item pick routes to the
// underlying panel with no selection (served sf05f trace, import slice3).
// Served control tree (DumpControlTree in trace):
//   PopupHost(Form, modeless, 182x104)
//     ComboBoxPopupContainer(182x104)
//       ComboListBox(182x104)
// Served down-press reaches the popup (no panel down-hit line) via
// DownTrackedDock/DialogOnDown, but TryCommitSfListItem returns false at
// the SfListView name gate (WinFormsAppHost.cs:4468-4470): the served list
// is a ComboListBox, not an SfListView (FindControlByNamePart matches TYPE
// names, :4553). Generic OnMouseDown on a ListBox does not select headless
// (no OnMouseDown override in fork ListBox.cs), so nothing commits; the
// popup closes and the click falls to the panel (expected post-close
// pattern per PopupFormClickTests header).
// Stand-ins use fork types only (no vendor execution): a ListBox subclass
// whose TYPE name contains "ComboListBox" inside a "Popup"-named
// borderless Form tracked in WasmDialogHost (the served registry — the
// served PopupHost IS tracked there; WasmPopupHost correctly excludes
// Forms by design, so this is not a popup-tracking defect).
using WfWasm.CanvasHost;
using WfWasm.Shim;
using FormsControl = System.Windows.Forms.Control;
using FormsForm = System.Windows.Forms.Form;
using FormsListBox = System.Windows.Forms.ListBox;
using FormsBorderStyle = System.Windows.Forms.FormBorderStyle;
using FormsPoint = System.Drawing.Point;
using FormsSize = System.Drawing.Size;

public sealed class PopupComboCommitTests
{
    // Type name must contain "Popup" (TryCommitSfListItem win gate).
    private sealed class ComboPopupHost : FormsForm { }

    // Type name must contain "ComboListBox" (the served list type).
    // Healthy stand-in: fixed row stride like a laid-out vendor list
    // (served rows paint legible; stride is not the defect).
    private sealed class ComboListBoxStandin : FormsListBox
    {
        public new int ItemHeight => 15;
    }

    private static (WinFormsAppHost host, ComboPopupHost popup, ComboListBoxStandin list, int clickX, int clickY) OpenPopup()
    {
        var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        var form = host.GetOrCreateForm(AppScene.Smoke);
        Assert.NotNull(form);

        // Park the popup below the smoke form: points inside the smoke
        // title strip return early as title drags (pre-routing) and would
        // never reach the dock route under test.
        int px = form.Location.X + 50;
        int py = form.Location.Y + form.Height + 40;
        var popup = new ComboPopupHost
        {
            FormBorderStyle = FormsBorderStyle.None,
            Location = new FormsPoint(px, py),
            Size = new FormsSize(182, 104),
        };
        var list = new ComboListBoxStandin
        {
            Location = new FormsPoint(0, 0),
            Size = new FormsSize(182, 104),
        };
        list.Items.Add("en-US");
        list.Items.Add("fr-FR");
        list.Items.Add("de-DE");
        popup.Controls.Add(list);
        // Bounds must be applied AFTER handle creation: pre-handle sets do
        // not stick headless (tracked at 0,0), unlike the served vendor
        // popup which is positioned live by ShowPopup.
        _ = popup.Handle;
        popup.Location = new FormsPoint(px, py);
        popup.Size = new FormsSize(182, 104);
        popup.Visible = true;
        System.Windows.Forms.WasmDialogHost.Track(popup, modal: false);
        // Row 1 center in popup-local coords (stride 15, served row geometry).
        return (host, popup, list, px + 91, py + 15 + 7);
    }

    [Fact]
    public void ModelessPopupForm_IsVisible_In_DialogSnapshot()
    {
        // Precondition probe: the served PopupHost IS tracked+visible in
        // WasmDialogHost (served [VAPI] track line). If this fails
        // in-process, the funnel (not the commit gate) is the gap.
        var (host, popup, list, _, _) = OpenPopup();
        using (popup)
        using (list)
        try
        {
            Assert.True(popup.Visible, "popup must be visible");
            Assert.True(popup.IsHandleCreated, "popup must have a handle");
            Assert.Contains(System.Windows.Forms.WasmDialogHost.Snapshot(),
                w => ReferenceEquals(w.Form, popup));
        }
        finally
        {
            System.Windows.Forms.WasmDialogHost.Untrack(popup);
            host.Dispose();
        }
    }

    [Fact]
    public void Popup_Down_Reaches_ComboList_Row_Control()
    {
        // Delivery pin (PASSES): the press is not swallowed by the panel —
        // it reaches the popup list by either the generic mousedown or the
        // row-commit path. Durable across the fix (fix consumes the press
        // via commit instead of raising MouseDown).
        var (host, popup, list, clickX, clickY) = OpenPopup();
        using (popup)
        using (list)
        try
        {
            bool downSeen = false;
            list.MouseDown += (_, _) => downSeen = true;
            host.OnMouseDown(clickX, clickY);
            Assert.True(downSeen || list.SelectedIndex == 1,
                "press must reach the popup list (generic mousedown or row commit), not the panel");
        }
        finally
        {
            System.Windows.Forms.WasmDialogHost.Untrack(popup);
            host.Dispose();
        }
    }

    [Fact]
    public void Popup_Row_Press_Commits_Row_Selection()
    {
        // TRIAGE PIN, FAILS until the commit gate accepts ComboListBox
        // (WinFormsAppHost.cs:4468-4470). Owner: runtime Host regions are
        // vendor's per WC0755 handoff — fix extends TryCommitSfListItem to
        // the served ComboListBox shape (ItemHeight/SelectedIndex row math
        // already generalizes). Do not "fix" by weakening this test.
        var (host, popup, list, clickX, clickY) = OpenPopup();
        using (popup)
        using (list)
        try
        {
            Assert.Equal(-1, list.SelectedIndex);
            host.OnMouseDown(clickX, clickY);
            Assert.Equal(1, list.SelectedIndex);
        }
        finally
        {
            System.Windows.Forms.WasmDialogHost.Untrack(popup);
            host.Dispose();
        }
    }
}
