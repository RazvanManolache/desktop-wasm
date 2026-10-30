namespace DesktopWasm.Packaging.Tests;

/// <summary>
/// Coordinator-review collision first: imported WpfTypeMap.SourceAssemblies
/// already includes System.Windows.Forms and Microsoft.Win32.SystemEvents
/// for WPF platform shims while real WinForms has its own fork and a
/// separate SystemEvents assembly. These tests pin the explicit
/// precedence/identity policy for those overlaps: real routes to real,
/// shims only where no WinForms capability exists, collision named loudly.
/// </summary>
public sealed class PrecedenceTests
{
    [Fact]
    public void WinFormsForm_InWinFormsAssembly_RoutesToRealFork()
    {
        var route = JointPrecedence.ResolveType(
            "System.Windows.Forms", "System.Windows.Forms", "Form", FrameworkCapabilities.WinForms);

        Assert.Equal(JointTargetKind.WinFormsEngine, route.Kind);
        Assert.Equal("WfWasm.WinForms", route.TargetAssembly);
        Assert.Equal("System.Windows.Forms.Form", route.TargetNamespace + "." + route.TargetName);
    }

    [Fact]
    public void WinFormsForm_InPureWpfAssembly_IsGap_NeverSilentShim()
    {
        var route = JointPrecedence.ResolveType(
            "System.Windows.Forms", "System.Windows.Forms", "Form", FrameworkCapabilities.Wpf);

        Assert.Equal(JointTargetKind.Gap, route.Kind);
    }

    [Fact]
    public void ShimScreen_InPureWpfAssembly_RoutesToWpfShim()
    {
        var route = JointPrecedence.ResolveType(
            "System.Windows.Forms", "System.Windows.Forms", "Screen", FrameworkCapabilities.Wpf);

        Assert.Equal(JointTargetKind.WpfEngine, route.Kind);
        Assert.Equal("DesktopWasm.Wpf", route.TargetAssembly);
    }

    [Fact]
    public void ShimScreen_InMixedAssembly_RealForkWins()
    {
        var route = JointPrecedence.ResolveType(
            "System.Windows.Forms", "System.Windows.Forms", "Screen", FrameworkCapabilities.Mixed);

        Assert.Equal(JointTargetKind.WinFormsEngine, route.Kind);
        Assert.Equal("WfWasm.WinForms", route.TargetAssembly);
    }

    [Fact]
    public void SystemEvents_InWinFormsOnly_BindsRealAssembly_NotWpfEngine()
    {
        var route = JointPrecedence.ResolveType(
            "Microsoft.Win32.SystemEvents", "Microsoft.Win32", "SystemEvents", FrameworkCapabilities.WinForms);

        Assert.Equal(JointTargetKind.SystemEventsAssembly, route.Kind);
        Assert.Equal("Microsoft.Win32.SystemEvents", route.TargetAssembly);
    }

    [Fact]
    public void SystemEvents_InPureWpf_BindsEngineAdapter()
    {
        var route = JointPrecedence.ResolveType(
            "Microsoft.Win32.SystemEvents", "Microsoft.Win32", "SystemEvents", FrameworkCapabilities.Wpf);

        Assert.Equal(JointTargetKind.WpfEngine, route.Kind);
        Assert.Equal("DesktopWasm.Wpf", route.TargetAssembly);
    }

    [Fact]
    public void SystemEvents_InMixedAssembly_IsVisibleCollision()
    {
        var route = JointPrecedence.ResolveType(
            "Microsoft.Win32.SystemEvents", "Microsoft.Win32", "SystemEvents", FrameworkCapabilities.Mixed);

        Assert.Equal(JointTargetKind.Collision, route.Kind);
        Assert.Contains("SystemEvents assembly", route.Reason);
        Assert.Contains("WPF engine adapter", route.Reason);
    }

    [Fact]
    public void NonShimBroadcast_InMixedAssembly_BindsRealAssembly()
    {
        var route = JointPrecedence.ResolveType(
            "Microsoft.Win32.SystemEvents", "Microsoft.Win32", "SessionEndingEventArgs", FrameworkCapabilities.Mixed);

        Assert.Equal(JointTargetKind.SystemEventsAssembly, route.Kind);
    }

    [Fact]
    public void WpfButton_RoutesToWpfEngineNamespace()
    {
        var route = JointPrecedence.ResolveType(
            "PresentationFramework", "System.Windows.Controls", "Button", FrameworkCapabilities.Wpf);

        Assert.Equal(JointTargetKind.WpfEngine, route.Kind);
        Assert.Equal("DesktopWasm.Wpf", route.TargetAssembly);
        Assert.Equal("DesktopWasm.Wpf", route.TargetNamespace);
    }

    [Fact]
    public void UnknownAssembly_IsGap()
    {
        var route = JointPrecedence.ResolveType(
            "Contoso.Telemetry", "Contoso", "Tracker", FrameworkCapabilities.Mixed);

        Assert.Equal(JointTargetKind.Gap, route.Kind);
    }
}
