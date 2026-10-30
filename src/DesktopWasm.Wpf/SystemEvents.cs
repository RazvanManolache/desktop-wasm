namespace Microsoft.Win32;

// System-preference change surface (matches the Microsoft.Win32.SystemEvents
// subset the gallery MainWindow/HeaderTile subscribe to: UserPreferenceChanged
// refreshes title-bar buttons and tile brushes from SystemParameters and
// resources). Windows broadcasts preference changes from the OS; the browser
// has no OS broadcast, so the host drives this event from browser preference
// signals (prefers-color-scheme / forced-colors media queries) through
// RaiseUserPreferenceChanged, and sessions/tests drive it directly. A
// subscribed handler running on raise is real behavior, not a suppression:
// without the event the gallery's refresh code would never run in the
// browser at all.

/// <summary>Preference category (matches Microsoft.Win32.UserPreferenceCategory).</summary>
public enum UserPreferenceCategory
{
    Accessibility = 1,
    Color = 2,
    Desktop = 3,
    General = 4,
    Icon = 5,
    Keyboard = 6,
    Menu = 7,
    Mouse = 8,
    Policy = 9,
    Power = 10,
    Screensaver = 11,
    Window = 12,
    Locale = 13,
    VisualStyle = 14,
}

/// <summary>Preference-change handler (matches Microsoft.Win32.UserPreferenceChangedEventHandler).</summary>
public delegate void UserPreferenceChangedEventHandler(object sender, UserPreferenceChangedEventArgs e);

/// <summary>Preference-change payload (matches Microsoft.Win32.UserPreferenceChangedEventArgs).</summary>
public sealed class UserPreferenceChangedEventArgs : EventArgs
{
    public UserPreferenceChangedEventArgs(UserPreferenceCategory category)
    {
        Category = category;
    }

    public UserPreferenceCategory Category { get; }
}

/// <summary>
/// System event bridge (matches Microsoft.Win32.SystemEvents for the
/// UserPreferenceChanged member the gallery uses). The browser host calls
/// <see cref="RaiseUserPreferenceChanged"/> when a media-query preference
/// flips; handlers run synchronously like the gallery's Dispatcher.Invoke
/// refresh path (the engine pump is single-threaded).
/// </summary>
public static class SystemEvents
{
    private static readonly object Sender = new();

    /// <summary>Raised when a system preference changes (theme, contrast, locale...).</summary>
    public static event UserPreferenceChangedEventHandler? UserPreferenceChanged;

    /// <summary>
    /// Drives <see cref="UserPreferenceChanged"/> (host media-query flips
    /// and tests call this; there is no OS broadcast in the browser).
    /// Preference broadcasts also invalidate system metrics, so
    /// <see cref="SystemParameters"/> raises its static change (converted
    /// DynamicResource consumers and the gallery opacity coupling refresh).
    /// </summary>
    public static void RaiseUserPreferenceChanged(UserPreferenceCategory category)
    {
        UserPreferenceChanged?.Invoke(Sender, new UserPreferenceChangedEventArgs(category));
        DesktopWasm.Wpf.SystemParameters.RaiseStaticPropertyChanged(category.ToString());
    }

    /// <summary>
    /// Raised when display settings change (matches
    /// Microsoft.Win32.SystemEvents.DisplaySettingsChanged, which the
    /// OxyPlot ExampleBrowser MainWindow subscribes to for DPI/theme
    /// refresh). The browser has no OS broadcast, so the host drives this
    /// event from display/DPI signals through
    /// <see cref="RaiseDisplaySettingsChanged"/>, and sessions/tests drive
    /// it directly.
    /// </summary>
    public static event EventHandler? DisplaySettingsChanged;

    /// <summary>
    /// Drives <see cref="DisplaySettingsChanged"/> (host display signals
    /// and tests call this; there is no OS broadcast in the browser).
    /// </summary>
    public static void RaiseDisplaySettingsChanged()
    {
        DisplaySettingsChanged?.Invoke(Sender, EventArgs.Empty);
    }

    /// <summary>Raised when the power state changes (the converted recorder suspend guards refresh from it).</summary>
    public static event PowerModeChangedEventHandler? PowerModeChanged;

    /// <summary>
    /// Drives <see cref="PowerModeChanged"/> (host power signals and tests
    /// call this; there is no OS broadcast in the browser). Converted
    /// Editor/NewRecorder/Recorder/Webcam suspend guards pause capture work
    /// off this event.
    /// </summary>
    public static void RaisePowerModeChanged(PowerModes mode)
    {
        PowerModeChanged?.Invoke(Sender, new PowerModeChangedEventArgs(mode));
    }
}

/// <summary>Power state (matches Microsoft.Win32.PowerModes: Resume=1/StatusChange=2/Suspend=3).</summary>
public enum PowerModes
{
    Resume = 1,
    StatusChange = 2,
    Suspend = 3,
}

/// <summary>Power-change handler (matches Microsoft.Win32.PowerModeChangedEventHandler).</summary>
public delegate void PowerModeChangedEventHandler(object sender, PowerModeChangedEventArgs e);

/// <summary>Power-change payload (matches Microsoft.Win32.PowerModeChangedEventArgs).</summary>
public sealed class PowerModeChangedEventArgs : EventArgs
{
    public PowerModeChangedEventArgs(PowerModes mode)
    {
        Mode = mode;
    }

    public PowerModes Mode { get; }
}
