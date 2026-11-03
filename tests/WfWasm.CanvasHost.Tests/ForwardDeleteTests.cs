using WfWasm.CanvasHost;
using WfWasm.Shim;
using FormsControl = System.Windows.Forms.Control;
using FormsForm = System.Windows.Forms.Form;
using FormsTextBox = System.Windows.Forms.TextBox;

namespace WfWasm.CanvasHost.Tests;

// RVW0900-shell-5: Delete-key forward delete (previously unhandled: Delete hit
// the no-KeyChar early return and did nothing). Symmetric to Backspace:
// caret-safe, selection-aware. Real fork TextBox through
// WinFormsAppHost.OnKeyDown.
public sealed class ForwardDeleteTests
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
    public void Delete_Removes_After_Caret()
    {
        var (host, textBox) = FocusedBox();
        using (host)
        {
            Type(host, "abc");
            host.OnKeyDown("ArrowLeft");
            host.OnKeyDown("Delete");
            Assert.Equal("ab", textBox.Text);
            Assert.Equal(2, textBox.SelectionStart);
        }
    }

    [Fact]
    public void Delete_At_End_NoOp()
    {
        var (host, textBox) = FocusedBox();
        using (host)
        {
            Type(host, "ab");
            host.OnKeyDown("Delete");
            Assert.Equal("ab", textBox.Text);
            Assert.Equal(2, textBox.SelectionStart);
        }
    }

    [Fact]
    public void Delete_At_Start()
    {
        var (host, textBox) = FocusedBox();
        using (host)
        {
            Type(host, "abc");
            textBox.SelectionStart = 0;
            host.OnKeyDown("Delete");
            Assert.Equal("bc", textBox.Text);
            Assert.Equal(0, textBox.SelectionStart);
        }
    }

    [Fact]
    public void Delete_Replaces_Selection()
    {
        var (host, textBox) = FocusedBox();
        using (host)
        {
            Type(host, "abcd");
            textBox.Select(1, 2);
            host.OnKeyDown("Delete");
            Assert.Equal("ad", textBox.Text);
            Assert.Equal(1, textBox.SelectionStart);
        }
    }

    [Fact]
    public void Delete_With_Throwing_TextChanged_Still_Moves_Caret()
    {
        var (host, textBox) = FocusedBox();
        using (host)
        {
            Type(host, "ab");
            textBox.SelectionStart = 1;
            bool armed = true;
            textBox.TextChanged += (_, _) =>
            {
                if (armed)
                {
                    armed = false;
                    throw new InvalidOperationException("ColumnInfo cannot be set.");
                }
            };
            Assert.Throws<InvalidOperationException>(() => host.OnKeyDown("Delete"));
            Assert.Equal("a", textBox.Text);
            Assert.Equal(1, textBox.SelectionStart);
        }
    }

    [Fact]
    public void Delete_Then_Type()
    {
        var (host, textBox) = FocusedBox();
        using (host)
        {
            Type(host, "abc");
            host.OnKeyDown("ArrowLeft");
            host.OnKeyDown("Delete");
            host.OnKeyDown("X", shift: true);
            Assert.Equal("abX", textBox.Text);
            Assert.Equal(3, textBox.SelectionStart);
        }
    }
}
