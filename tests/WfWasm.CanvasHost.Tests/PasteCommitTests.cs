using WfWasm.CanvasHost;
using WfWasm.Shim;
using FormsControl = System.Windows.Forms.Control;
using FormsForm = System.Windows.Forms.Form;
using FormsTextBox = System.Windows.Forms.TextBox;

namespace WfWasm.CanvasHost.Tests;

// RVW0900-shell-3: clipboard paste commits through WinFormsAppHost.CommitPaste
// (previously no paste path existed: clipboard Uni + Ctrl+V inserted nothing).
// Real fork TextBox; the browser @onpaste/readClipboard wiring feeds this API.
public sealed class PasteCommitTests
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
    public void Paste_Inserts_In_Order()
    {
        var (host, textBox) = FocusedBox();
        using (host)
        {
            host.CommitPaste("Uni");
            Assert.Equal("Uni", textBox.Text);
            Assert.Equal(3, textBox.SelectionStart);
        }
    }

    [Fact]
    public void Paste_Middle_Insertion()
    {
        var (host, textBox) = FocusedBox();
        using (host)
        {
            Type(host, "ac");
            textBox.SelectionStart = 1;
            host.CommitPaste("B");
            Assert.Equal("aBc", textBox.Text);
            Assert.Equal(2, textBox.SelectionStart);
        }
    }

    [Fact]
    public void Paste_Replaces_Selection()
    {
        var (host, textBox) = FocusedBox();
        using (host)
        {
            Type(host, "abcd");
            textBox.Select(1, 2);
            host.CommitPaste("X");
            Assert.Equal("aXd", textBox.Text);
            Assert.Equal(2, textBox.SelectionStart);
        }
    }

    [Fact]
    public void Paste_With_Throwing_TextChanged_Still_Advances()
    {
        var (host, textBox) = FocusedBox();
        using (host)
        {
            bool armed = true;
            textBox.TextChanged += (_, _) =>
            {
                if (armed)
                {
                    armed = false;
                    throw new InvalidOperationException("ColumnInfo cannot be set.");
                }
            };
            Assert.Throws<InvalidOperationException>(() => host.CommitPaste("U"));
            Assert.Equal("U", textBox.Text);
            Assert.Equal(1, textBox.SelectionStart);
            Type(host, "ni");
            Assert.Equal("Uni", textBox.Text);
        }
    }

    [Fact]
    public void Paste_Empty_And_Null_NoOp()
    {
        var (host, textBox) = FocusedBox();
        using (host)
        {
            Type(host, "ab");
            host.CommitPaste("");
            host.CommitPaste(null);
            Assert.Equal("ab", textBox.Text);
            Assert.Equal(2, textBox.SelectionStart);
        }
    }

    [Fact]
    public void Paste_Newline_Policy()
    {
        var (host, textBox) = FocusedBox();
        using (host)
        {
            host.CommitPaste("a\r\nb");
            Assert.Equal("ab", textBox.Text);
            textBox.Multiline = true;
            textBox.SelectionStart = 2;
            host.CommitPaste("c\nd");
            Assert.Equal("abc\nd", textBox.Text);
        }
    }
}
