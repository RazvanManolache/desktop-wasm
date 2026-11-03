using WfWasm.CanvasHost;
using WfWasm.Shim;
using FormsControl = System.Windows.Forms.Control;
using FormsForm = System.Windows.Forms.Form;
using FormsTextBox = System.Windows.Forms.TextBox;

namespace WfWasm.CanvasHost.Tests;

// RVW0900-shell-3: arrow keys move the text caret in the host (previously no
// arrow-key caret path existed: ac + Left + B rendered acB). Real fork TextBox
// through WinFormsAppHost.OnKeyDown.
public sealed class ArrowCaretTests
{
    private static (WinFormsAppHost Host, FormsTextBox Box) FocusedBox()
    {
        var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        FormsForm form = host.GetOrCreateForm(AppScene.Smoke);
        Assert.NotNull(form);
        var textBox = new FormsTextBox();
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
    public void Left_Right_Move_And_Clamp()
    {
        var (host, textBox) = FocusedBox();
        using (host)
        {
            Type(host, "ab");
            host.OnKeyDown("ArrowLeft");
            Assert.Equal(1, textBox.SelectionStart);
            host.OnKeyDown("ArrowLeft");
            Assert.Equal(0, textBox.SelectionStart);
            host.OnKeyDown("ArrowLeft");
            Assert.Equal(0, textBox.SelectionStart);
            host.OnKeyDown("ArrowRight");
            Assert.Equal(1, textBox.SelectionStart);
            host.OnKeyDown("ArrowRight");
            host.OnKeyDown("ArrowRight");
            Assert.Equal(2, textBox.SelectionStart);
        }
    }

    [Fact]
    public void Middle_Insertion_After_Arrow()
    {
        var (host, textBox) = FocusedBox();
        using (host)
        {
            Type(host, "ac");
            host.OnKeyDown("ArrowLeft");
            host.OnKeyDown("B", shift: true);
            Assert.Equal("aBc", textBox.Text);
            Assert.Equal(2, textBox.SelectionStart);
        }
    }

    [Fact]
    public void Arrows_Collapse_Selection()
    {
        var (host, textBox) = FocusedBox();
        using (host)
        {
            Type(host, "abcd");
            textBox.Select(1, 2);
            host.OnKeyDown("ArrowLeft");
            Assert.Equal(1, textBox.SelectionStart);
            Assert.Equal(0, textBox.SelectionLength);
            textBox.Select(1, 2);
            host.OnKeyDown("ArrowRight");
            Assert.Equal(3, textBox.SelectionStart);
            Assert.Equal(0, textBox.SelectionLength);
        }
    }

    [Fact]
    public void Vendor_Moved_Caret_Is_Not_Double_Moved()
    {
        var (host, textBox) = FocusedBox();
        using (host)
        {
            Type(host, "ab");
            textBox.KeyDown += (_, _) => textBox.SelectionStart = 0;
            host.OnKeyDown("ArrowLeft");
            Assert.Equal(0, textBox.SelectionStart);
        }
    }

    [Fact]
    public void Handled_Arrow_Does_Not_Move()
    {
        var (host, textBox) = FocusedBox();
        using (host)
        {
            Type(host, "ab");
            textBox.KeyDown += (_, e) => e.Handled = true;
            host.OnKeyDown("ArrowLeft");
            Assert.Equal(2, textBox.SelectionStart);
        }
    }
}
