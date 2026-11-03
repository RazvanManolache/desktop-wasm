using System.Reflection;
using WfWasm.CanvasHost;
using WfWasm.Shim;
using FormsButton = System.Windows.Forms.Button;
using FormsControl = System.Windows.Forms.Control;
using FormsForm = System.Windows.Forms.Form;
using FormsTextBox = System.Windows.Forms.TextBox;
using Xunit;

namespace WfWasm.CanvasHost.Tests;

public sealed class PopupFocusContainmentTests
{
    private sealed class MockPopupControl : FormsControl
    {
        public bool ClosedCalled;

        public void ClosePopup()
        {
            ClosedCalled = true;
            Hide();
        }

        protected override System.Windows.Forms.CreateParams CreateParams
        {
            get
            {
                System.Windows.Forms.CreateParams cp = base.CreateParams;
                cp.Style |= unchecked((int)0x80000000); // WS_POPUP
                cp.Style &= ~0x40000000; // ~WS_CHILD
                return cp;
            }
        }
    }

    private static (WinFormsAppHost Host, FormsTextBox TextBox, FormsForm Form) CreateHostWithActiveForm()
    {
        var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        FormsForm form = host.GetOrCreateForm(AppScene.Smoke);
        var textBox = new FormsTextBox { Name = "mainText" };
        textBox.SetBounds(20, 20, 220, 30);
        form.Controls.Add(textBox);

        int absX = form.Location.X + textBox.Left + (textBox.Width / 2);
        int absY = form.Location.Y + 30 + textBox.Top + (textBox.Height / 2);
        host.OnMouseDown(absX, absY);
        return (host, textBox, form);
    }

    private static FormsControl? GetKeyTarget(WinFormsAppHost host)
    {
        MethodInfo method = typeof(WinFormsAppHost).GetMethod("KeyTarget", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return (FormsControl?)method.Invoke(host, null);
    }

    [Fact]
    public void Popup_Dismiss_Escape_RestoresFocusToActiveForm()
    {
        var (host, textBox, form) = CreateHostWithActiveForm();
        using (host)
        {
            Assert.Equal(textBox, GetKeyTarget(host));

            using var popup = new MockPopupControl
            {
                Name = "vendorPopup",
                Left = 400,
                Top = 300,
                Width = 160,
                Height = 120
            };
            var child = new FormsButton { Name = "popChild", Left = 10, Top = 10, Width = 50, Height = 25 };
            popup.Controls.Add(child);
            popup.Visible = true;
            _ = popup.Handle;
            System.Windows.Forms.WasmPopupHost.Track(popup);

            try
            {
                // Route mouse down into popup child
                host.OnMouseDown(400 + 10 + 5, 300 + 10 + 5);

                // Focus was transferred to child inside popup
                Assert.Equal(child, GetKeyTarget(host));

                // Dismiss via Escape
                host.OnKeyDown("Escape");

                // The popup is now hidden
                Assert.False(popup.Visible);

                // VERIFICATION OF CONTAINMENT FIX:
                // Focus is cleanly restored to the active form's textBox.
                // KeyTarget no longer traps focus inside the hidden popup control.
                var targetAfterDismiss = GetKeyTarget(host);
                Assert.Equal(textBox, targetAfterDismiss);
            }
            finally
            {
                System.Windows.Forms.WasmPopupHost.Untrack(popup);
            }
        }
    }

    [Fact]
    public void Outside_Click_Dismisses_NonTelerik_Popup_And_RestoresFocus()
    {
        var (host, textBox, form) = CreateHostWithActiveForm();
        using (host)
        {
            using var popup = new MockPopupControl
            {
                Name = "genericPopup",
                Left = 400,
                Top = 300,
                Width = 160,
                Height = 120
            };
            var child = new FormsButton { Name = "popChild", Left = 10, Top = 10, Width = 50, Height = 25 };
            popup.Controls.Add(child);
            popup.Visible = true;
            _ = popup.Handle;
            System.Windows.Forms.WasmPopupHost.Track(popup);

            try
            {
                Assert.Contains(System.Windows.Forms.WasmPopupHost.Snapshot(), c => ReferenceEquals(c, popup));

                // Route mouse down into popup to establish focus
                host.OnMouseDown(400 + 10 + 5, 300 + 10 + 5);
                Assert.Equal(child, GetKeyTarget(host));

                // Click outside popup bounds (e.g. at 50, 50)
                host.OnMouseDown(50, 50);

                // VERIFICATION OF CONTAINMENT FIX:
                // 1. Generic / Syncfusion popup is cleanly dismissed via ClosePopup.
                // 2. Popup visibility is set to false and untracked from WasmPopupHost snapshot.
                // 3. Focus returns cleanly to active form's textBox.
                Assert.True(popup.ClosedCalled);
                Assert.False(popup.Visible);
                Assert.DoesNotContain(System.Windows.Forms.WasmPopupHost.Snapshot(), c => ReferenceEquals(c, popup));
                Assert.Equal(textBox, GetKeyTarget(host));
            }
            finally
            {
                System.Windows.Forms.WasmPopupHost.Untrack(popup);
            }
        }
    }

    [Fact]
    public void Inside_Click_Does_Not_Dismiss_Popup()
    {
        var (host, textBox, form) = CreateHostWithActiveForm();
        using (host)
        {
            using var popup = new MockPopupControl
            {
                Name = "insidePopup",
                Left = 400,
                Top = 300,
                Width = 160,
                Height = 120
            };
            popup.Visible = true;
            _ = popup.Handle;
            System.Windows.Forms.WasmPopupHost.Track(popup);

            try
            {
                // Click inside popup bounds (420, 320)
                host.OnMouseDown(420, 320);

                // Popup must stay open and tracked
                Assert.False(popup.ClosedCalled);
                Assert.True(popup.Visible);
                Assert.Contains(System.Windows.Forms.WasmPopupHost.Snapshot(), c => ReferenceEquals(c, popup));
            }
            finally
            {
                System.Windows.Forms.WasmPopupHost.Untrack(popup);
            }
        }
    }
}
