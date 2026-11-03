extern alias Fork;

using DesktopWasm.Core;
using DesktopWasm.Interop;
using DesktopWasm.Wpf;
using ForkForms = Fork::System.Windows.Forms;

namespace DesktopWasm.Interop.Tests;

/// <summary>
/// Tab order across engine boundaries under the real adapters, both
/// directions, with focus evidence. In-half Tab cycling uses each
/// engine's real tab machinery; the mixed session owns the boundary
/// transfer (neither half reports "no more stops" — both wrap internally).
/// </summary>
public sealed class MixedTabTests
{
    private sealed record Fixture(
        MixedGuestSession Session,
        Button WpfFirst,
        Button WpfSecond,
        ForkForms.Button WfFirst,
        ForkForms.Button WfSecond,
        ForkForms.Form WfForm);

    private static Fixture Create()
    {
        var wpfRoot = new Canvas();
        var wpfFirst = new Button { Content = "WPF 1" };
        var wpfSecond = new Button { Content = "WPF 2" };
        wpfRoot.AddChild(wpfFirst);
        wpfRoot.AddChild(wpfSecond);
        var wpf = new WpfGuestSession(wpfRoot, new WpfSize(800, 600));

        var wfForm = new ForkForms.Form { Text = "Mixed" };
        var wfFirst = new ForkForms.Button { Text = "WF 1" };
        var wfSecond = new ForkForms.Button { Text = "WF 2" };
        wfForm.Controls.Add(wfFirst);
        wfForm.Controls.Add(wfSecond);
        var wf = new WinFormsGuestSession(wfForm, new WpfSize(800, 600));

        return new Fixture(new MixedGuestSession(wpf, wf), wpfFirst, wpfSecond, wfFirst, wfSecond, wfForm);
    }

    [Fact]
    public void Tab_cycles_inside_wpf_half()
    {
        var f = Create();
        using (f.Session)
        {
            Assert.True(f.Session.FocusFirstInWpf());
            Assert.Same(f.WpfFirst, f.Session.Wpf.Manager.Focused);

            f.Session.DispatchKeyToWpf(new HostKeyEvent("Tab", IsDown: true));
            Assert.Same(f.WpfSecond, f.Session.Wpf.Manager.Focused);

            // WPF tab order wraps inside the half; it never leaks across.
            f.Session.DispatchKeyToWpf(new HostKeyEvent("Tab", IsDown: true));
            Assert.Same(f.WpfFirst, f.Session.Wpf.Manager.Focused);
            Assert.False(f.Session.WinFormsHasFocus);
        }
    }

    [Fact]
    public void Tab_cycles_inside_winforms_half()
    {
        var f = Create();
        using (f.Session)
        {
            Assert.True(f.Session.FocusFirstInWinForms());
            Assert.Same(f.WfFirst, f.WfForm.ActiveControl);

            f.Session.DispatchKeyToWinForms(new HostKeyEvent("Tab", IsDown: true));
            Assert.Same(f.WfSecond, f.WfForm.ActiveControl);

            f.Session.DispatchKeyToWinForms(new HostKeyEvent("Tab", IsDown: true));
            Assert.Same(f.WfFirst, f.WfForm.ActiveControl);
            Assert.False(f.Session.WpfHasFocus);
        }
    }

    [Fact]
    public void Boundary_transfer_crosses_both_directions_with_evidence()
    {
        var f = Create();
        using (f.Session)
        {
            Assert.True(f.Session.FocusFirstInWpf());
            Assert.True(f.Session.WpfHasFocus);

            f.Session.FocusNextHalf();
            Assert.True(f.Session.WinFormsHasFocus);
            Assert.False(f.Session.WpfHasFocus);
            Assert.True(f.WfFirst.Focused);

            f.Session.FocusNextHalf();
            Assert.True(f.Session.WpfHasFocus);
            Assert.False(f.Session.WinFormsHasFocus);

            f.Session.FocusPreviousHalf();
            Assert.True(f.Session.WinFormsHasFocus);
            Assert.Same(f.WfFirst, f.WfForm.ActiveControl);

            f.Session.FocusPreviousHalf();
            Assert.True(f.Session.WpfHasFocus);
            Assert.Same(f.WpfFirst, f.Session.Wpf.Manager.Focused);
        }
    }

    [Fact]
    public void Mixed_tab_cycle_covers_every_stop_in_order()
    {
        var f = Create();
        using (f.Session)
        {
            var seen = new List<string>();
            f.Session.FocusFirstInWpf();
            seen.Add("wpf1");

            f.Session.DispatchKeyToWpf(new HostKeyEvent("Tab", IsDown: true));
            seen.Add(f.Session.Wpf.Manager.Focused == f.WpfSecond ? "wpf2" : "?");

            f.Session.FocusNextHalf();
            seen.Add(f.WfForm.ActiveControl == f.WfFirst ? "wf1" : "?");

            f.Session.DispatchKeyToWinForms(new HostKeyEvent("Tab", IsDown: true));
            seen.Add(f.WfForm.ActiveControl == f.WfSecond ? "wf2" : "?");

            f.Session.FocusNextHalf();
            seen.Add(f.Session.WpfHasFocus ? "wpf1" : "?");

            Assert.Equal(["wpf1", "wpf2", "wf1", "wf2", "wpf1"], seen);
        }
    }
}
