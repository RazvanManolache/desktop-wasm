using WfWasm.CanvasHost;
using WfWasm.Shim;
using FormsControl = System.Windows.Forms.Control;
using FormsForm = System.Windows.Forms.Form;
using FormsTextBox = System.Windows.Forms.TextBox;

namespace WfWasm.CanvasHost.Tests;

// RVW0900-shell: host caret bookkeeping must survive a throwing TextChanged
// handler (Syncfusion AutoComplete's suggestion init throws
// InvalidOperationException "ColumnInfo cannot be set" exactly once per session
// from inside the Text store; the text IS stored but the SelectionStart advance
// was skipped, so later keystrokes land stale: Uni -> niU). Uses the real fork
// TextBox; a one-shot throwing TextChanged stands in for the vendor handler
// (vendor-binary repro is infeasible in-process: headless, no window handles).
public sealed class TextCaretRegressionTests
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

    [Fact]
    public void Typing_Uni_Yields_Uni()
    {
        var (host, textBox) = FocusedBox();
        using (host)
        {
            host.OnKeyDown("U", shift: true);
            host.OnKeyDown("n");
            host.OnKeyDown("i");
            Assert.Equal("Uni", textBox.Text);
            Assert.Equal(3, textBox.SelectionStart);
        }
    }

    [Fact]
    public void Throwing_TextChanged_Still_Advances_Caret()
    {
        var (host, textBox) = FocusedBox();
        using (host)
        {
            // Vendor stand-in: throws once, after the text was stored.
            bool armed = true;
            textBox.TextChanged += (_, _) =>
            {
                if (armed)
                {
                    armed = false;
                    throw new InvalidOperationException("ColumnInfo cannot be set.");
                }
            };
            // The vendor failure must still surface to the existing reporter path.
            Assert.Throws<InvalidOperationException>(() => host.OnKeyDown("U", shift: true));
            Assert.Equal("U", textBox.Text);
            // ... but the caret must advance past the stored char anyway.
            Assert.Equal(1, textBox.SelectionStart);
            // Vendor settles after its one-time init failure: the rest lands in order.
            host.OnKeyDown("n");
            host.OnKeyDown("i");
            Assert.Equal("Uni", textBox.Text);
            Assert.Equal(3, textBox.SelectionStart);
        }
    }

    [Fact]
    public void Middle_Insertion_At_Moved_Caret()
    {
        var (host, textBox) = FocusedBox();
        using (host)
        {
            host.OnKeyDown("a");
            host.OnKeyDown("c");
            textBox.SelectionStart = 1;
            host.OnKeyDown("B", shift: true);
            Assert.Equal("aBc", textBox.Text);
            Assert.Equal(2, textBox.SelectionStart);
        }
    }

    [Fact]
    public void Selection_Replacement()
    {
        var (host, textBox) = FocusedBox();
        using (host)
        {
            foreach (char c in "abcd")
                host.OnKeyDown(c.ToString());
            textBox.Select(1, 2);
            host.OnKeyDown("X", shift: true);
            Assert.Equal("aXd", textBox.Text);
            Assert.Equal(2, textBox.SelectionStart);
        }
    }

    [Fact]
    public void Backspace_Deletes_Before_Caret()
    {
        var (host, textBox) = FocusedBox();
        using (host)
        {
            foreach (char c in "abc")
                host.OnKeyDown(c.ToString());
            textBox.SelectionStart = 2;
            host.OnKeyDown("Backspace");
            Assert.Equal("ac", textBox.Text);
            Assert.Equal(1, textBox.SelectionStart);
            textBox.Select(0, 2);
            host.OnKeyDown("Backspace");
            Assert.Equal("", textBox.Text);
            Assert.Equal(0, textBox.SelectionStart);
        }
    }

    [Fact]
    public void Backspace_With_Throwing_TextChanged_Still_Moves_Caret()
    {
        var (host, textBox) = FocusedBox();
        using (host)
        {
            foreach (char c in "ab")
                host.OnKeyDown(c.ToString());
            bool armed = true;
            textBox.TextChanged += (_, _) =>
            {
                if (armed)
                {
                    armed = false;
                    throw new InvalidOperationException("ColumnInfo cannot be set.");
                }
            };
            Assert.Throws<InvalidOperationException>(() => host.OnKeyDown("Backspace"));
            Assert.Equal("a", textBox.Text);
            Assert.Equal(1, textBox.SelectionStart);
        }
    }
}
