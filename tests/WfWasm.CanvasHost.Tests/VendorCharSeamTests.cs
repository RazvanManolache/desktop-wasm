using System;
using System.Reflection;
using WfWasm.CanvasHost;
using WfWasm.Shim;
using FormsControl = System.Windows.Forms.Control;
using FormsForm = System.Windows.Forms.Form;
using FormsMessage = System.Windows.Forms.Message;
using FormsTextBox = System.Windows.Forms.TextBox;

namespace WfWasm.CanvasHost.Tests;

// RVW-9594 Job1: vendor-char seam for masked/numeric editors.
// A Syncfusion SfNumericTextBox overrides Text (manual sets parse Value but
// leave the vendor display text empty headless: typed pixels 0, watermark
// persists) and overrides ProcessKeyMessage (its WM_CHAR path syncs display
// + value together). The host must route printable chars through the vendor
// override when one exists and keep the stock manual splice otherwise.
// Live oracle: verification/rvw1096-import gate_job2d/2e on frozen bytes;
// these tests pin the dispatch contract in-process with faithful mocks.
public sealed class DesyncVendorBox : FormsTextBox
{
    private string _display = "";
    public int? ParsedValue { get; private set; }
#pragma warning disable CS8765 // Mock mirrors the vendor: setter parses, display syncs only via the char seam.
    public override string Text
    {
        get => _display;
        set => ParsedValue = int.TryParse(value, out int v) ? v : (int?)null;
    }
#pragma warning restore CS8765
    // NOTE: `new`, not `override`: the fork's protected-internal member cannot
    // be overridden from this test assembly (CS0507), but the host resolves
    // the seam by name and invokes the declared method directly, which is
    // exactly the dispatch the real (overriding) vendor control takes.
    private new bool ProcessKeyMessage(ref FormsMessage m)
    {
        if (m.Msg != VendorCharSeamTests.WmChar)
            return false;
        _display += (char)m.WParam;
        ParsedValue = int.TryParse(_display, out int v) ? v : null;
        return true;
    }
}

public sealed class DecliningVendorBox : FormsTextBox
{
    private new bool ProcessKeyMessage(ref FormsMessage m) => false;
}

// LostFocus-mode vendor shape: the Text setter parses Value but never syncs
// the display (deferred to the mask engine), and the char seam always
// declines. Only the EDIT-insert fallback (WindowText write) commits.
public sealed class LostFocusVendorBox : FormsTextBox
{
    public int? ParsedValue { get; private set; }
#pragma warning disable CS8765
    public override string Text
    {
        get => base.Text;
        set => ParsedValue = int.TryParse(value, out int v) ? v : (int?)null;
    }
#pragma warning restore CS8765
    private new bool ProcessKeyMessage(ref FormsMessage m) => false;
}

// Arms edit mode through its own focus override: the char seam declines
// until OnEnter runs (the served prefilled-numeric shape).
public sealed class EditArmBox : FormsTextBox
{
    private string _display = "";
    private bool _editArmed;
    public int? ParsedValue { get; private set; }
#pragma warning disable CS8765
    public override string Text
    {
        get => _display;
        set => ParsedValue = int.TryParse(value, out int v) ? v : (int?)null;
    }
#pragma warning restore CS8765
    public int EnterCount { get; private set; }
    public int GotFocusCount { get; private set; }
    protected override void OnEnter(EventArgs e)
    {
        base.OnEnter(e);
        EnterCount++;
    }
    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        GotFocusCount++;
        _editArmed = true;
    }
    private new bool ProcessKeyMessage(ref FormsMessage m)
    {
        if (m.Msg != VendorCharSeamTests.WmChar)
            return false;
        if (!_editArmed)
            return false;
        _display += (char)m.WParam;
        ParsedValue = int.TryParse(_display, out int v) ? v : null;
        return true;
    }
}

// Declines the pristine char but accepts it once Value is parsed (the
// served plain-NumericTextBox shape: post-manual state consumes).
public sealed class DeclineFirstBox : FormsTextBox
{
    private string _display = "";
    public int? ParsedValue { get; private set; }
#pragma warning disable CS8765
    public override string Text
    {
        get => _display;
        set => ParsedValue = int.TryParse(value, out int v) ? v : (int?)null;
    }
#pragma warning restore CS8765
    private new bool ProcessKeyMessage(ref FormsMessage m)
    {
        if (m.Msg != VendorCharSeamTests.WmChar)
            return false;
        if (!ParsedValue.HasValue)
            return false;
        _display += (char)m.WParam;
        return true;
    }
}

public sealed class VendorCharSeamTests
{
    internal const int WmChar = 0x0102;

    private static (int X, int Y) CenterOn(FormsForm form, FormsControl control)
    {
        int absX = form.Location.X;
        int absY = form.Location.Y + 30;
        for (FormsControl? current = control; current is not null && current is not FormsForm; current = current.Parent)
        {
            absX += current.Left;
            absY += current.Top;
        }
        return (absX + (control.Width / 2), absY + (control.Height / 2));
    }

    private static void FocusBox(WinFormsAppHost host, FormsForm form, FormsTextBox box)
    {
        box.SetBounds(20, 20, 220, 30);
        form.Controls.Add(box);
        try
        {
            var (x, y) = CenterOn(form, box);
            host.OnMouseDown(x, y);
        }
        catch
        {
            form.Controls.Remove(box);
            throw;
        }
    }

    [Fact]
    public void VendorOverride_Consumes_Char_Skips_Manual_Splice()
    {
        // Red on the pre-seam host (manual set leaves display "").
        using var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        FormsForm form = host.GetOrCreateForm(AppScene.Smoke);
        Assert.NotNull(form);
        var box = new DesyncVendorBox();
        FocusBox(host, form, box);
        try
        {
            host.OnKeyDown("2");
            Assert.Equal("2", box.Text);
            Assert.Equal(2, box.ParsedValue);
            host.OnKeyDown("7");
            Assert.Equal("27", box.Text);
            Assert.Equal(27, box.ParsedValue);
        }
        finally
        {
            form.Controls.Remove(box);
        }
    }

    [Fact]
    public void StockTextBox_Keeps_Manual_Commit_Exactly_Once()
    {
        using var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        FormsForm form = host.GetOrCreateForm(AppScene.Smoke);
        Assert.NotNull(form);
        var textBox = new FormsTextBox();
        FocusBox(host, form, textBox);
        try
        {
            int presses = 0;
            textBox.KeyPress += (_, _) => presses++;
            host.OnKeyDown("a");
            Assert.Equal("a", textBox.Text);
            host.OnKeyDown("b");
            Assert.Equal("ab", textBox.Text);
            Assert.Equal(2, presses);
        }
        finally
        {
            form.Controls.Remove(textBox);
        }
    }

    [Fact]
    public void VendorDeclined_Falls_Back_To_Manual_Path()
    {
        using var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        FormsForm form = host.GetOrCreateForm(AppScene.Smoke);
        Assert.NotNull(form);
        var box = new DecliningVendorBox();
        FocusBox(host, form, box);
        try
        {
            int presses = 0;
            box.KeyPress += (_, _) => presses++;
            host.OnKeyDown("5");
            Assert.Equal("5", box.Text);
            Assert.Equal(1, presses);
        }
        finally
        {
            form.Controls.Remove(box);
        }
    }

    [Fact]
    public void VendorDeclineFirst_Retries_Seam_After_Manual_Value()
    {
        // Red without the post-manual retry (display stays desynced).
        using var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        FormsForm form = host.GetOrCreateForm(AppScene.Smoke);
        Assert.NotNull(form);
        var box = new DeclineFirstBox();
        FocusBox(host, form, box);
        try
        {
            int presses = 0;
            box.KeyPress += (_, _) => presses++;
            host.OnKeyDown("4");
            Assert.Equal("4", box.Text);
            Assert.Equal(4, box.ParsedValue);
            Assert.Equal(1, presses);
            host.OnKeyDown("2");
            Assert.Equal("42", box.Text);
        }
        finally
        {
            form.Controls.Remove(box);
        }
    }

    [Fact]
    public void VendorEditArm_Rearms_Declining_Seam_After_Manual_Value()
    {
        // Red without the edit-mode arm (decline persists, display desyncs).
        using var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        FormsForm form = host.GetOrCreateForm(AppScene.Smoke);
        Assert.NotNull(form);
        var box = new EditArmBox();
        box.Text = "123";
        FocusBox(host, form, box);
        int enterAtFocus = box.EnterCount;
        try
        {
            host.OnKeyDown("4");
            Assert.Equal("4", box.Text);
            Assert.Equal(4, box.ParsedValue);
        }
        finally
        {
            form.Controls.Remove(box);
        }
        // Focus-click raises OnEnter but never OnGotFocus headless
        // (enterAtFocus=1, gotAtFocus=0 without the arm); the arm below
        // completes the transition so the seam consumes.
        Assert.Equal(1, enterAtFocus);
    }

    [Fact]
    public void VendorEditInsert_Commits_When_Setter_Drops_And_Seam_Declines()
    {
        // Red without the EDIT-insert fallback (display stays empty).
        using var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        FormsForm form = host.GetOrCreateForm(AppScene.Smoke);
        Assert.NotNull(form);
        var box = new LostFocusVendorBox();
        FocusBox(host, form, box);
        try
        {
            host.OnKeyDown("4");
            Assert.Equal("4", box.Text);
            Assert.Equal(4, box.ParsedValue);
            host.OnKeyDown("2");
            Assert.Equal("42", box.Text);
        }
        finally
        {
            form.Controls.Remove(box);
        }
    }

    [Fact]
    public void VendorSeam_Caches_Per_Type_And_Ignores_NonText()
    {
        using var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        FormsForm form = host.GetOrCreateForm(AppScene.Smoke);
        Assert.NotNull(form);
        FieldInfo? field = typeof(WinFormsAppHost).GetField("s_vendorCharSeams", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(field);
        var cache = Assert.IsAssignableFrom<System.Collections.Concurrent.ConcurrentDictionary<Type, MethodInfo?>>(field.GetValue(null));
        // Drive this test's own seam lookup so the assertions below do not
        // depend on sibling-test execution order (the cache is static).
        var box = new DesyncVendorBox();
        FocusBox(host, form, box);
        try
        {
            host.OnKeyDown("2");
            Assert.Equal("2", box.Text);
        }
        finally
        {
            form.Controls.Remove(box);
        }
        Assert.True(cache.TryGetValue(typeof(DesyncVendorBox), out MethodInfo? seam));
        Assert.NotNull(seam);
        // Non-text controls never take the seam: no resolution, no throw.
        var btn = new System.Windows.Forms.Button();
        btn.SetBounds(20, 60, 120, 30);
        form.Controls.Add(btn);
        try
        {
            var (x, y) = CenterOn(form, btn);
            host.OnMouseDown(x, y);
            host.OnKeyDown("2");
        }
        finally
        {
            form.Controls.Remove(btn);
        }
        Assert.False(cache.ContainsKey(typeof(System.Windows.Forms.Button)));
    }
}
