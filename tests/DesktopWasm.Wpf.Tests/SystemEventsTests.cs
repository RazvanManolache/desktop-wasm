using DesktopWasm.Core;
using Microsoft.Win32;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Gallery preference slice: MainWindow and HeaderTile subscribe to
/// SystemEvents.UserPreferenceChanged and refresh visuals from
/// SystemParameters/resources on the dispatcher. The browser has no OS
/// broadcast, so the host drives the event; subscribed handlers must run
/// with the gallery's category payload.
/// </summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class SystemEventsTests : IDisposable
{
    private readonly Application? _previousApp = Application.Current;

    public SystemEventsTests()
    {
        WasmDispatcher.Current = new WasmDispatcher(new WasmDispatcherPump());
        Application.Current = new Application();
    }

    public void Dispose()
    {
        Application.Current = _previousApp;
    }

    [Fact]
    public void Category_contract_matches_wpf_numeric_values()
    {
        Assert.Equal(1, (int)UserPreferenceCategory.Accessibility);
        Assert.Equal(4, (int)UserPreferenceCategory.General);
        Assert.Equal(11, (int)UserPreferenceCategory.Screensaver);
        Assert.Equal(12, (int)UserPreferenceCategory.Window);
        Assert.Equal(13, (int)UserPreferenceCategory.Locale);
        Assert.Equal(14, (int)UserPreferenceCategory.VisualStyle);
    }

    [Fact]
    public void Gallery_refresh_pattern_runs_on_raise_with_category()
    {
        // Mirrors MainWindow.SystemEvents_UserPreferenceChanged: the handler
        // refreshes visuals on the dispatcher when preferences change.
        var refreshed = new List<UserPreferenceCategory>();
        void OnChanged(object sender, UserPreferenceChangedEventArgs e) =>
            Dispatcher.CurrentDispatcher.Invoke(() => refreshed.Add(e.Category));
        SystemEvents.UserPreferenceChanged += OnChanged;
        try
        {
            Assert.Empty(refreshed);
            SystemEvents.RaiseUserPreferenceChanged(UserPreferenceCategory.General);
            Assert.Equal([UserPreferenceCategory.General], refreshed);
            SystemEvents.RaiseUserPreferenceChanged(UserPreferenceCategory.Accessibility);
            Assert.Equal([UserPreferenceCategory.General, UserPreferenceCategory.Accessibility], refreshed);
        }
        finally
        {
            SystemEvents.UserPreferenceChanged -= OnChanged;
        }
    }

    [Fact]
    public void Unsubscribed_handlers_stop_refreshing()
    {
        var count = 0;
        void OnChanged(object sender, UserPreferenceChangedEventArgs e) => count++;
        SystemEvents.UserPreferenceChanged += OnChanged;
        SystemEvents.RaiseUserPreferenceChanged(UserPreferenceCategory.General);
        Assert.Equal(1, count);
        SystemEvents.UserPreferenceChanged -= OnChanged;
        SystemEvents.RaiseUserPreferenceChanged(UserPreferenceCategory.General);
        Assert.Equal(1, count);
    }

    [Fact]
    public void DisplaySettingsChanged_shape_matches_wpf_add_remove()
    {
        // The converted OxyPlot ExampleBrowser MainWindow binds
        // SystemEvents.add_DisplaySettingsChanged(EventHandler); the
        // engine must expose the exact add/remove pair or boot throws
        // MissingMethodException before any window shows.
        var eventInfo = typeof(SystemEvents).GetEvent(nameof(SystemEvents.DisplaySettingsChanged));
        Assert.NotNull(eventInfo);
        Assert.Equal(typeof(EventHandler), eventInfo.EventHandlerType);
        Assert.NotNull(eventInfo.AddMethod);
        Assert.NotNull(eventInfo.RemoveMethod);
        Assert.NotNull(typeof(SystemEvents).GetMethod(nameof(SystemEvents.RaiseDisplaySettingsChanged)));
    }

    [Fact]
    public void DisplaySettingsChanged_pattern_runs_on_raise()
    {
        // Mirrors the ExampleBrowser MainWindow subscription: the handler
        // refreshes display-dependent visuals when settings change.
        var calls = new List<(object? Sender, EventArgs Args)>();
        void OnChanged(object? sender, EventArgs e) => calls.Add((sender, e));
        SystemEvents.DisplaySettingsChanged += OnChanged;
        try
        {
            Assert.Empty(calls);
            SystemEvents.RaiseDisplaySettingsChanged();
            Assert.Single(calls);
            Assert.NotNull(calls[0].Sender);
            Assert.Same(EventArgs.Empty, calls[0].Args);
            SystemEvents.RaiseDisplaySettingsChanged();
            Assert.Equal(2, calls.Count);
        }
        finally
        {
            SystemEvents.DisplaySettingsChanged -= OnChanged;
        }
    }

    [Fact]
    public void DisplaySettingsChanged_unsubscribed_handlers_stop()
    {
        var count = 0;
        void OnChanged(object? sender, EventArgs e) => count++;
        SystemEvents.DisplaySettingsChanged += OnChanged;
        SystemEvents.RaiseDisplaySettingsChanged();
        Assert.Equal(1, count);
        SystemEvents.DisplaySettingsChanged -= OnChanged;
        SystemEvents.RaiseDisplaySettingsChanged();
        Assert.Equal(1, count);
    }
}
