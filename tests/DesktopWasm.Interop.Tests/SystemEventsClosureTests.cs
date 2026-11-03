extern alias Fork;
extern alias Win32Events;
extern alias WpfEng;

using DesktopWasm.Core;
using DesktopWasm.Interop;
using DesktopWasm.Packaging;
using DesktopWasm.Wpf;
using ForkForms = Fork::System.Windows.Forms;
using WpfSys = WpfEng::Microsoft.Win32;

namespace DesktopWasm.Interop.Tests;

/// <summary>
/// SystemEvents per-closure proof with packaging input. The joint policy
/// (<see cref="JointPrecedence"/>, packaging lane owned) decides every
/// overlapped reference; these tests pin the mixed-closure routing the
/// interop sessions run under, prove the real provider identity is shared
/// (not split) on the WinForms side, and demonstrate the explicit broadcast
/// bridge a mixed session needs (WPF adapter raise → real WinForms state).
/// No engine types are removed or modified anywhere in this proof.
/// </summary>
public sealed class SystemEventsClosureTests
{
    [Theory]
    // Real controls route to the real fork in any WinForms-capable closure.
    // A pure-WPF closure naming a non-shim control is a gap by policy
    // (nothing there may bind the fork behind the back of the closure).
    [InlineData("System.Windows.Forms", "System.Windows.Forms", "Control",
        FrameworkCapabilities.WinForms, JointTargetKind.WinFormsEngine, "WfWasm.WinForms")]
    [InlineData("System.Windows.Forms", "System.Windows.Forms", "Control",
        FrameworkCapabilities.Mixed, JointTargetKind.WinFormsEngine, "WfWasm.WinForms")]
    [InlineData("System.Windows.Forms", "System.Windows.Forms", "Control",
        FrameworkCapabilities.Wpf, JointTargetKind.Gap, "")]
    // Shim-overlapped names: pure-WPF binds the adapter, WinForms-capable binds the fork.
    [InlineData("System.Windows.Forms", "System.Windows.Forms", "Screen",
        FrameworkCapabilities.Wpf, JointTargetKind.WpfEngine, "DesktopWasm.Wpf")]
    [InlineData("System.Windows.Forms", "System.Windows.Forms", "Screen",
        FrameworkCapabilities.Mixed, JointTargetKind.WinFormsEngine, "WfWasm.WinForms")]
    [InlineData("System.Windows.Forms", "System.Windows.Forms", "Screen",
        FrameworkCapabilities.WinForms, JointTargetKind.WinFormsEngine, "WfWasm.WinForms")]
    public void WinForms_routing_follows_closure_capability(
        string assembly, string ns, string name, FrameworkCapabilities caps,
        JointTargetKind expectedKind, string expectedAssembly)
    {
        var route = JointPrecedence.ResolveType(assembly, ns, name, caps);
        Assert.Equal(expectedKind, route.Kind);
        Assert.Equal(expectedAssembly, route.TargetAssembly);
        Assert.NotEmpty(route.Reason);
    }

    [Theory]
    // Broadcast members: WinForms-only binds the real assembly, pure-WPF the adapter.
    [InlineData("Microsoft.Win32.SystemEvents", "Microsoft.Win32", "SessionEndingEventArgs",
        FrameworkCapabilities.WinForms, JointTargetKind.SystemEventsAssembly, "Microsoft.Win32.SystemEvents")]
    [InlineData("Microsoft.Win32.SystemEvents", "Microsoft.Win32", "UserPreferenceChangedEventArgs",
        FrameworkCapabilities.Wpf, JointTargetKind.WpfEngine, "DesktopWasm.Wpf")]
    [InlineData("Microsoft.Win32.SystemEvents", "Microsoft.Win32", "SessionEndingEventArgs",
        FrameworkCapabilities.Mixed, JointTargetKind.SystemEventsAssembly, "Microsoft.Win32.SystemEvents")]
    public void SystemEvents_routing_follows_closure_capability(
        string assembly, string ns, string name, FrameworkCapabilities caps,
        JointTargetKind expectedKind, string expectedAssembly)
    {
        var route = JointPrecedence.ResolveType(assembly, ns, name, caps);
        Assert.Equal(expectedKind, route.Kind);
        Assert.Equal(expectedAssembly, route.TargetAssembly);
        Assert.NotEmpty(route.Reason);
    }

    [Fact]
    public void Mixed_adapter_claimed_broadcast_is_collision_naming_both_claimants()
    {
        // The exact case the joint policy calls out: a mixed assembly
        // referencing the broadcast handler both maps claim.
        var route = JointPrecedence.ResolveType(
            "Microsoft.Win32.SystemEvents", "Microsoft.Win32",
            "UserPreferenceChangedEventHandler", FrameworkCapabilities.Mixed);

        Assert.Equal(JointTargetKind.Collision, route.Kind);
        Assert.Contains("SystemEvents assembly", route.Reason, StringComparison.Ordinal);
        Assert.Contains("WPF engine adapter", route.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Real_provider_identity_is_shared_not_split_on_the_winforms_side()
    {
        // The fork binds the real assembly (its own ProjectReference); this
        // test project binds the same project output. Name+version identity
        // must agree exactly — the policy's own identity unit — or the
        // WinForms side would split broadcast delivery across two loads.
        var mine = typeof(Win32Events::Microsoft.Win32.SystemEvents).Assembly.GetName();
        var forkRef = typeof(ForkForms.Control).Assembly
            .GetReferencedAssemblies()
            .First(a => a.Name == "Microsoft.Win32.SystemEvents");
        Assert.Equal(mine.FullName, forkRef.FullName);
    }

    [Fact]
    public void Wpf_adapter_is_a_distinct_type_pinned_as_the_known_collision()
    {
        // Same simple name, different runtime identity: the documented
        // overlap, asserted so any NEW duplicate fails loudly instead of
        // silently splitting broadcast delivery.
        Assert.NotEqual(
            typeof(Win32Events::Microsoft.Win32.SystemEvents),
            typeof(WpfSys.SystemEvents));
        Assert.Equal("DesktopWasm.Wpf", typeof(WpfSys.SystemEvents).Assembly.GetName().Name);
    }

    [Fact]
    public void Wpf_adapter_broadcast_reaches_handlers_synchronously()
    {
        // The engine adapter's raise path is real behavior (gallery refresh
        // depends on it): a subscribed handler runs with the exact category.
        WpfSys.UserPreferenceCategory? seen = null;
        void Handler(object sender, WpfSys.UserPreferenceChangedEventArgs e) => seen = e.Category;

        WpfSys.SystemEvents.UserPreferenceChanged += Handler;
        try
        {
            WpfSys.SystemEvents.RaiseUserPreferenceChanged(WpfSys.UserPreferenceCategory.Color);
            Assert.Equal(WpfSys.UserPreferenceCategory.Color, seen);
        }
        finally
        {
            WpfSys.SystemEvents.UserPreferenceChanged -= Handler;
        }
    }

    [Fact]
    public void Real_provider_subscribe_roundtrips_headlessly_without_throw()
    {
        // The real assembly's headless contract: add/remove never throw.
        // (It has no raise path off-OS; the WPF adapter owns raising.)
        var handler = new Win32Events::Microsoft.Win32.UserPreferenceChangedEventHandler((_, _) => { });
        var ex = Record.Exception(() =>
        {
            Win32Events::Microsoft.Win32.SystemEvents.UserPreferenceChanged += handler;
            Win32Events::Microsoft.Win32.SystemEvents.UserPreferenceChanged -= handler;
        });
        Assert.Null(ex);
    }

    [Fact]
    public void Mixed_broadcast_bridge_wpf_raise_mutates_real_winforms_state()
    {
        // The explicit bridge the Collision reason demands, demonstrated in
        // a mixed session: a WPF broadcast mutates real WinForms state.
        // (The reverse has no raise path on the headless real provider;
        // recorded as the engine limit, not worked around.)
        using var wpf = new WpfGuestSession(new Button { Content = "B" }, new WpfSize(800, 600));
        var wfBox = new ForkForms.TextBox();
        using var wf = new WinFormsGuestSession(wfBox, new WpfSize(800, 600));
        using var session = new MixedGuestSession(wpf, wf);

        void Bridge(object sender, WpfSys.UserPreferenceChangedEventArgs e) =>
            wfBox.Text = $"pref:{e.Category}";
        WpfSys.SystemEvents.UserPreferenceChanged += Bridge;
        try
        {
            WpfSys.SystemEvents.RaiseUserPreferenceChanged(WpfSys.UserPreferenceCategory.Desktop);
            Assert.Equal("pref:Desktop", wfBox.Text);
        }
        finally
        {
            WpfSys.SystemEvents.UserPreferenceChanged -= Bridge;
        }
        _ = session;
    }
}

