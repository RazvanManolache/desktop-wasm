using WfWasm.CanvasHost;
using WfWasm.Shim;
using FormsControl = System.Windows.Forms.Control;
using FormsForm = System.Windows.Forms.Form;
using FormsTextBox = System.Windows.Forms.TextBox;

namespace WfWasm.CanvasHost.Tests;

// RVW0900-shell-4: remaining caret paths (Shift-extend, Home/End, Up/Down).
// Real fork TextBox through WinFormsAppHost.OnKeyDown. Contract: no-shift
// arrows move/collapse (slice 3); Shift+arrows extend from a host-tracked
// anchor (vendor motion resets it); Home/End jump; Up/Down move by line in
// multiline boxes and to the ends in single-line boxes.
public sealed class ExtendedCaretTests
{
    private static (WinFormsAppHost Host, FormsTextBox Box) FocusedBox(bool multiline = false)
    {
        var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        FormsForm form = host.GetOrCreateForm(AppScene.Smoke);
        Assert.NotNull(form);
        var textBox = new FormsTextBox { Multiline = multiline };
        textBox.SetBounds(20, 20, 220, 30);
        form.Controls.Add(textBox);
        int absX = form.Location.X;
        int absY = form.Location.Y + 30;
        for (FormsControl? current = textBox; current is not null && current is not FormsForm; current = current.Parent)
        {
            absX += current.Left;
            absY += current.Top;
        }
        host.OnMouseDown(absX + (textBox.Width / 2), absY + (textBox.Height / 2));
        return (host, textBox);
    }

    private static void Type(WinFormsAppHost host, string text)
    {
        foreach (char c in text)
            host.OnKeyDown(c.ToString());
    }

    [Fact]
    public void ShiftRight_Selects_Forward()
    {
        var (host, textBox) = FocusedBox();
        using (host)
        {
            Type(host, "abc");
            textBox.SelectionStart = 0;
            host.OnKeyDown("ArrowRight", shift: true);
            Assert.Equal(0, textBox.SelectionStart);
            Assert.Equal(1, textBox.SelectionLength);
            host.OnKeyDown("ArrowRight", shift: true);
            Assert.Equal(2, textBox.SelectionLength);
            host.OnKeyDown("ArrowRight", shift: true);
            host.OnKeyDown("ArrowRight", shift: true);
            Assert.Equal(0, textBox.SelectionStart);
            Assert.Equal(3, textBox.SelectionLength);
        }
    }

    [Fact]
    public void ShiftLeft_Selects_Backward()
    {
        var (host, textBox) = FocusedBox();
        using (host)
        {
            Type(host, "abc");
            host.OnKeyDown("ArrowLeft", shift: true);
            Assert.Equal(2, textBox.SelectionStart);
            Assert.Equal(1, textBox.SelectionLength);
            host.OnKeyDown("ArrowLeft", shift: true);
            Assert.Equal(1, textBox.SelectionStart);
            Assert.Equal(2, textBox.SelectionLength);
        }
    }

    [Fact]
    public void Shift_Select_Then_Type_Replaces()
    {
        var (host, textBox) = FocusedBox();
        using (host)
        {
            Type(host, "abc");
            textBox.SelectionStart = 0;
            host.OnKeyDown("ArrowRight", shift: true);
            host.OnKeyDown("ArrowRight", shift: true);
            host.OnKeyDown("X", shift: true);
            Assert.Equal("Xc", textBox.Text);
            Assert.Equal(1, textBox.SelectionStart);
        }
    }

    [Fact]
    public void Anchor_Persists_Across_Reversal()
    {
        var (host, textBox) = FocusedBox();
        using (host)
        {
            Type(host, "abc");
            textBox.SelectionStart = 0;
            host.OnKeyDown("ArrowRight", shift: true);
            host.OnKeyDown("ArrowRight", shift: true);
            Assert.Equal((0, 2), (textBox.SelectionStart, textBox.SelectionLength));
            host.OnKeyDown("ArrowLeft", shift: true);
            Assert.Equal((0, 1), (textBox.SelectionStart, textBox.SelectionLength));
            host.OnKeyDown("ArrowLeft", shift: true);
            Assert.Equal((0, 0), (textBox.SelectionStart, textBox.SelectionLength));
        }
    }

    [Fact]
    public void Vendor_Motion_Resets_Anchor_Safely()
    {
        var (host, textBox) = FocusedBox();
        using (host)
        {
            Type(host, "abcd");
            textBox.KeyDown += (_, _) => textBox.Select(1, 2);
            host.OnKeyDown("ArrowRight", shift: true);
            Assert.Equal((1, 3), (textBox.SelectionStart, textBox.SelectionLength));
        }
    }

    [Fact]
    public void Home_End_Jump()
    {
        var (host, textBox) = FocusedBox();
        using (host)
        {
            Type(host, "abc");
            host.OnKeyDown("Home");
            Assert.Equal(0, textBox.SelectionStart);
            host.OnKeyDown("End");
            Assert.Equal(3, textBox.SelectionStart);
            textBox.Select(0, 3);
            host.OnKeyDown("End");
            Assert.Equal(3, textBox.SelectionStart);
            Assert.Equal(0, textBox.SelectionLength);
        }
    }

    [Fact]
    public void Up_Down_SingleLine_Go_To_Ends()
    {
        var (host, textBox) = FocusedBox();
        using (host)
        {
            Type(host, "abc");
            textBox.SelectionStart = 1;
            host.OnKeyDown("ArrowDown");
            Assert.Equal(3, textBox.SelectionStart);
            host.OnKeyDown("ArrowUp");
            Assert.Equal(0, textBox.SelectionStart);
        }
    }

    [Fact]
    public void Up_Down_Multiline_Move_By_Line()
    {
        var (host, textBox) = FocusedBox(multiline: true);
        using (host)
        {
            textBox.Text = "ab\ncde\nf";
            textBox.SelectionStart = 4;
            host.OnKeyDown("ArrowUp");
            Assert.Equal(1, textBox.SelectionStart);
            host.OnKeyDown("ArrowDown");
            Assert.Equal(4, textBox.SelectionStart);
            host.OnKeyDown("ArrowDown");
            Assert.Equal(8, textBox.SelectionStart);
            host.OnKeyDown("ArrowDown");
            Assert.Equal(8, textBox.SelectionStart);
            host.OnKeyDown("ArrowUp");
            Assert.Equal(4, textBox.SelectionStart);
        }
    }
}
