extern alias Fork;

using System.Reflection;
using DesktopWasm.Interop;
using DesktopWasm.Wpf;
using ForkForms = Fork::System.Windows.Forms;
using WfiHost = System.Windows.Forms.Integration.WindowsFormsHost;

namespace DesktopWasm.Interop.Tests;

/// <summary>
/// Runtime-loading side of the TestApp bridge gap: the packaged TestApp
/// closure references the net48 joint interface <c>WindowsFormsIntegration</c>
/// (GAC-only upstream, never packaged). This proves that reference resolves
/// at runtime to the real bridge provider in this assembly, whose child is a
/// real fork WinForms control — not a facade/stub, and with acyclic engine
/// boundaries intact.
/// </summary>
public sealed class BridgeResolutionTests
{
    [Theory]
    [InlineData("WindowsFormsIntegration")]
    [InlineData("windowsformsintegration")]
    [InlineData("WindowsFormsIntegration, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35")]
    public void JointInterface_resolves_to_real_bridge_provider(string displayName)
    {
        var assembly = WindowsFormsHostResolver.Resolve(displayName);
        Assert.NotNull(assembly);
        Assert.Same(typeof(WfiHost).Assembly, assembly);
        Assert.Equal("System.Windows.Forms.Integration.WindowsFormsHost",
            typeof(WfiHost).FullName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("System.Windows.Forms")]
    [InlineData("DesktopWasm.Wpf")]
    [InlineData("WfWasm.WinForms")]
    [InlineData("WindowsFormsIntegrationX")]
    [InlineData("not an assembly name \0 bad")]
    public void NonJointInterface_resolves_nowhere(string? displayName)
    {
        Assert.Null(WindowsFormsHostResolver.Resolve(displayName));
    }

    [Fact]
    public void ResolveHostType_maps_TestApp_joint_reference_to_real_host()
    {
        // Exact reference shape the converted TestApp carries: assembly
        // WindowsFormsIntegration + this full type name (see
        // AvalonDockClosureTests joint-scope assertion on real bytes).
        var type = WindowsFormsHostResolver.ResolveHostType(
            "WindowsFormsIntegration, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35",
            "System.Windows.Forms.Integration.WindowsFormsHost");
        Assert.Same(typeof(WfiHost), type);
    }

    [Theory]
    [InlineData("System.Windows.Forms", "System.Windows.Forms.Integration.WindowsFormsHost")]
    [InlineData("WindowsFormsIntegration", "System.Windows.Forms.Integration.ElementHost")]
    [InlineData("WindowsFormsIntegration", null)]
    public void ResolveHostType_rejects_wrong_scope_or_name(string? assembly, string? fullName)
    {
        Assert.Null(WindowsFormsHostResolver.ResolveHostType(assembly, fullName));
    }

    [Fact]
    public void Bridge_provider_hosts_real_fork_control()
    {
        Assert.True(typeof(FrameworkElement).IsAssignableFrom(typeof(WfiHost)));
        Assert.Equal(typeof(ForkForms.Control),
            typeof(WfiHost).GetProperty(nameof(WfiHost.Child))!.PropertyType);
    }

    [Fact]
    public void Engine_projects_do_not_depend_back_on_adapter()
    {
        var wpfReferences = typeof(DesktopWasm.Wpf.Window).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name);
        Assert.DoesNotContain("DesktopWasm.Interop", wpfReferences);
    }

    [Fact]
    public void Bridge_registration_is_idempotent_and_joint_only()
    {
        // BrowserHost wiring is a thin EnsureRegistered + Resolve pair over
        // this resolver; prove the shared semantics here: repeated resolve is
        // stable and joint-only (packaged assemblies stay with the package
        // image resolver).
        var first = WindowsFormsHostResolver.Resolve("WindowsFormsIntegration");
        var second = WindowsFormsHostResolver.Resolve("WindowsFormsIntegration");
        Assert.NotNull(first);
        Assert.Same(first, second);
        Assert.Null(WindowsFormsHostResolver.Resolve("TestApp"));
    }
}
