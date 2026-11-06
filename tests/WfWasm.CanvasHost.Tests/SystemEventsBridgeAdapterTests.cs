// CREDIT1300-RT4-1215: bridge-adapter contract. Broadcast in (the exact
// delegates registered on the real assembly) -> engine adapter receives
// (recording core); non-overlapped names pass through untouched (the bridge
// claims only the three engine-raisable names); engine absent fails closed.
using WfWasm.CanvasHost;

public sealed class SystemEventsBridgeAdapterTests
{
    private static (List<(string Method, object?[] Args)> Calls, Func<string, object?[], bool> Core) Recorder()
    {
        var calls = new List<(string Method, object?[] Args)>();
        return (calls, (method, args) => { calls.Add((method, args)); return true; });
    }

    [Fact]
    public void RealDisplayHandler_ForwardsToEngineRaise()
    {
        var (calls, core) = Recorder();
        var prev = SystemEventsBridgeAdapter.RaiseEngine;
        SystemEventsBridgeAdapter.RaiseEngine = core;
        try
        {
            SystemEventsBridgeAdapter.OnRealDisplaySettingsChanged(new object(), EventArgs.Empty);
        }
        finally
        {
            SystemEventsBridgeAdapter.RaiseEngine = prev;
        }
        var only = Assert.Single(calls);
        Assert.Equal("RaiseDisplaySettingsChanged", only.Method);
        Assert.Empty(only.Args);
    }

    [Fact]
    public void RealPreferenceHandler_ForwardsCategoryInt()
    {
        var (calls, core) = Recorder();
        var prev = SystemEventsBridgeAdapter.RaiseEngine;
        SystemEventsBridgeAdapter.RaiseEngine = core;
        try
        {
            SystemEventsBridgeAdapter.OnRealUserPreferenceChanged(
                new object(),
                new Microsoft.Win32.UserPreferenceChangedEventArgs(
                    Microsoft.Win32.UserPreferenceCategory.General));
        }
        finally
        {
            SystemEventsBridgeAdapter.RaiseEngine = prev;
        }
        var only = Assert.Single(calls);
        Assert.Equal("RaiseUserPreferenceChanged", only.Method);
        Assert.Equal([4], only.Args);
    }

    [Fact]
    public void RealPowerHandler_ForwardsModeInt()
    {
        var (calls, core) = Recorder();
        var prev = SystemEventsBridgeAdapter.RaiseEngine;
        SystemEventsBridgeAdapter.RaiseEngine = core;
        try
        {
            SystemEventsBridgeAdapter.OnRealPowerModeChanged(
                new object(),
                new Microsoft.Win32.PowerModeChangedEventArgs(
                    Microsoft.Win32.PowerModes.Suspend));
        }
        finally
        {
            SystemEventsBridgeAdapter.RaiseEngine = prev;
        }
        var only = Assert.Single(calls);
        Assert.Equal("RaisePowerModeChanged", only.Method);
        Assert.Equal([3], only.Args);
    }

    [Fact]
    public void OverlapSet_IsExactlyTheThreeEngineRaisableNames()
    {
        Assert.True(SystemEventsBridgeAdapter.IsForwarded("DisplaySettingsChanged"));
        Assert.True(SystemEventsBridgeAdapter.IsForwarded("UserPreferenceChanged"));
        Assert.True(SystemEventsBridgeAdapter.IsForwarded("PowerModeChanged"));
        // Non-overlapped broadcast names bind the real assembly direct —
        // the bridge claims nothing here (pass through untouched).
        foreach (var name in new[]
        {
            "SessionSwitch", "SessionEnding", "SessionEnded", "LowMemory",
            "PaletteChanged", "TimeChanged", "InstalledFontsChanged",
            "DisplaySettingsChanging", "UserPreferenceChanging",
            "TimerElapsed", "EventsThreadShutdown", "SystemColorsChanged",
            null!, string.Empty,
        })
            Assert.False(SystemEventsBridgeAdapter.IsForwarded(name));
    }

    [Fact]
    public void ContractNumerics_AgreeAcrossBothAssemblies()
    {
        // The bridge passes ints across the reflection boundary, so the
        // numeric contract is load-bearing. Real side asserted live;
        // engine side pinned to DesktopWasm.Wpf/SystemEvents.cs:15-31,109-115
        // (engine declares none of the gap members per RT3, but these two
        // enums are its own adapter surface — read, not assumed).
        Assert.Equal(4, (int)Microsoft.Win32.UserPreferenceCategory.General);
        Assert.Equal(2, (int)Microsoft.Win32.UserPreferenceCategory.Color);
        Assert.Equal(3, (int)Microsoft.Win32.PowerModes.Suspend);
        Assert.Equal(1, (int)Microsoft.Win32.PowerModes.Resume);
    }

    [Fact]
    public void EngineAbsent_FailClosedNoThrow()
    {
        var prev = SystemEventsBridgeAdapter.RaiseEngine;
        SystemEventsBridgeAdapter.RaiseEngine = SystemEventsBridgeAdapter.DefaultRaiseEngine;
        try
        {
            // The test process loads no DesktopWasm.Wpf: every forward
            // declines without throwing (best-effort, adapter precedent).
            Assert.False(SystemEventsBridgeAdapter.ForwardDisplaySettingsChanged());
            Assert.False(SystemEventsBridgeAdapter.ForwardUserPreferenceChanged(4));
            Assert.False(SystemEventsBridgeAdapter.ForwardPowerModeChanged(3));
        }
        finally
        {
            SystemEventsBridgeAdapter.RaiseEngine = prev;
        }
    }

    [Fact]
    public void EnsureSubscribed_IdempotentNoThrow()
    {
        SystemEventsBridgeAdapter.EnsureSubscribed();
        SystemEventsBridgeAdapter.EnsureSubscribed();
        Assert.True(SystemEventsBridgeAdapter.IsSubscribed);
    }
}
