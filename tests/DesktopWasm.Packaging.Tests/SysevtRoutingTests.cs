using System.Reflection;

namespace DesktopWasm.Packaging.Tests;

/// <summary>
/// Packaging side of the per-closure SystemEvents proof (interop proved
/// the mixed broadcast bridge headlessly with zero engine edits,
/// reports/dw-interop-20261030T1404Z.md). Every broadcast member routes to
/// exactly one provider per closure kind; the mixed adapter overlap is an
/// explicit named collision, never a silent win. Packaging project + tests
/// only; no engine/shim/Convert/Rewrite edits.
/// </summary>
public sealed class SysevtRoutingTests
{
    private static readonly string[] ShimMembers =
    [
        "SystemEvents",
        "UserPreferenceChangedEventHandler",
        "UserPreferenceChangedEventArgs",
        "UserPreferenceCategory",
        "PowerModeChangedEventArgs",
        "PowerModeChangedEventHandler",
        "PowerModes",
    ];

    private static readonly string[] BroadcastOnlyMembers =
    [
        "SessionEndingEventArgs",
        "SessionSwitchEventArgs",
        "SessionEndedEventArgs",
    ];

    [Fact]
    public void ShimInventory_IsSeven_MatchingInteropSlice()
    {
        Assert.Equal(7, JointMap.WpfSystemEventsShimTypes.Count);
        foreach (var member in ShimMembers)
            Assert.Contains("Microsoft.Win32." + member, JointMap.WpfSystemEventsShimTypes);
    }

    [Theory]
    [InlineData("SystemEvents")]
    [InlineData("UserPreferenceChangedEventHandler")]
    [InlineData("UserPreferenceChangedEventArgs")]
    [InlineData("UserPreferenceCategory")]
    [InlineData("PowerModeChangedEventArgs")]
    [InlineData("PowerModeChangedEventHandler")]
    [InlineData("PowerModes")]
    public void ShimMember_WinFormsOnly_BindsRealAssembly(string member)
    {
        var route = JointPrecedence.ResolveType(
            "Microsoft.Win32.SystemEvents", "Microsoft.Win32", member, FrameworkCapabilities.WinForms);

        Assert.Equal(JointTargetKind.SystemEventsAssembly, route.Kind);
        Assert.Equal("Microsoft.Win32.SystemEvents", route.TargetAssembly);
    }

    [Theory]
    [InlineData("SystemEvents")]
    [InlineData("UserPreferenceChangedEventHandler")]
    [InlineData("UserPreferenceChangedEventArgs")]
    [InlineData("UserPreferenceCategory")]
    [InlineData("PowerModeChangedEventArgs")]
    [InlineData("PowerModeChangedEventHandler")]
    [InlineData("PowerModes")]
    public void ShimMember_PureWpf_BindsEngineAdapter(string member)
    {
        var route = JointPrecedence.ResolveType(
            "Microsoft.Win32.SystemEvents", "Microsoft.Win32", member, FrameworkCapabilities.Wpf);

        Assert.Equal(JointTargetKind.WpfEngine, route.Kind);
        Assert.Equal("DesktopWasm.Wpf", route.TargetAssembly);
    }

    [Theory]
    [InlineData("SystemEvents")]
    [InlineData("UserPreferenceChangedEventHandler")]
    [InlineData("UserPreferenceChangedEventArgs")]
    [InlineData("UserPreferenceCategory")]
    [InlineData("PowerModeChangedEventArgs")]
    [InlineData("PowerModeChangedEventHandler")]
    [InlineData("PowerModes")]
    public void ShimMember_Mixed_IsExplicitCollision_NamingBothClaimants(string member)
    {
        var route = JointPrecedence.ResolveType(
            "Microsoft.Win32.SystemEvents", "Microsoft.Win32", member, FrameworkCapabilities.Mixed);

        Assert.Equal(JointTargetKind.Collision, route.Kind);
        Assert.Contains("SystemEvents assembly", route.Reason);
        Assert.Contains("WPF engine adapter", route.Reason);
        Assert.Contains("explicit", route.Reason);
        Assert.Contains(member, route.Reason);
    }

    [Theory]
    [InlineData("SessionEndingEventArgs")]
    [InlineData("SessionSwitchEventArgs")]
    [InlineData("SessionEndedEventArgs")]
    public void BroadcastOnlyMember_BindsRealAssembly_InEveryClosureKind(string member)
    {
        foreach (var capabilities in new[]
            { FrameworkCapabilities.None, FrameworkCapabilities.WinForms, FrameworkCapabilities.Wpf, FrameworkCapabilities.Mixed })
        {
            var route = JointPrecedence.ResolveType(
                "Microsoft.Win32.SystemEvents", "Microsoft.Win32", member, capabilities);

            Assert.True(route.Kind == JointTargetKind.SystemEventsAssembly,
                $"{member}@{capabilities} routed {route.Kind}, expected SystemEventsAssembly");
            Assert.Equal("Microsoft.Win32.SystemEvents", route.TargetAssembly);
        }
    }

    [Fact]
    public void WinFormsControl_InPureWpfClosure_IsGap_NeverSilentForkBind()
    {
        // Pins the code, not the gloss (interop correction): nothing may
        // bind the real fork behind a closure's back.
        var route = JointPrecedence.ResolveType(
            "System.Windows.Forms", "System.Windows.Forms", "Control", FrameworkCapabilities.Wpf);

        Assert.Equal(JointTargetKind.Gap, route.Kind);
        Assert.Contains("System.Windows.Forms.Control", route.Reason);
    }

    [Fact]
    public void MixedFixture_PrefChangedEvent_SurfacesOwnerNamedCollision()
    {
        using var assembly = FixtureBuilder.CreateMixedModule();
        var module = assembly.MainModule;
        var capabilities = JointCapabilities.Detect(module);
        Assert.Equal(FrameworkCapabilities.Mixed, capabilities);

        var collisions = new List<(string Owner, JointRoute Route)>();
        foreach (var type in module.Types)
            foreach (var evt in type.Events)
            {
                var scope = evt.EventType.Scope as Mono.Cecil.AssemblyNameReference;
                if (scope is null || !JointMap.OverlappedAssemblies.Contains(scope.Name)) continue;
                var fullName = evt.EventType.FullName;
                var dot = fullName.LastIndexOf('.');
                var route = JointPrecedence.ResolveType(scope.Name, fullName[..dot], fullName[(dot + 1)..], capabilities);
                if (route.Kind == JointTargetKind.Collision)
                    collisions.Add((type.FullName + "." + evt.Name, route));
            }

        var hit = Assert.Single(collisions);
        Assert.Equal("Fixtures.MixedForm.PrefChanged", hit.Owner);
        Assert.Contains("SystemEvents assembly", hit.Route.Reason);
        Assert.Contains("WPF engine adapter", hit.Route.Reason);
    }

    [Fact]
    public void ShimMembers_ResolveInEngine_BroadcastOnly_ResolveInRealImage()
    {
        // No duplicate incompatible identity: each name resolves in exactly
        // its routed provider (engine adapter vs real assembly).
        var engine = typeof(DesktopWasm.Wpf.Window).Assembly;
        foreach (var member in ShimMembers)
            Assert.True(engine.GetType("Microsoft.Win32." + member, throwOnError: false) is not null,
                $"Engine adapter missing '{member}' the map claims.");

        var real = LoadRealSystemEvents();
        Assert.True(real is not null, "Real SystemEvents input image absent; cannot prove non-shim identity.");
        foreach (var member in BroadcastOnlyMembers)
            Assert.True(real!.GetType("Microsoft.Win32." + member, throwOnError: false) is not null,
                $"Real SystemEvents image missing '{member}'.");
    }

    private static Assembly? LoadRealSystemEvents()
    {
        const string pinned = @"E:\AI\Code\DesktopWasm\inputs\winforms\targets\paint.net\Microsoft.Win32.SystemEvents.dll";
        try { return File.Exists(pinned) ? Assembly.LoadFrom(pinned) : null; }
        catch { return null; }
    }
}
