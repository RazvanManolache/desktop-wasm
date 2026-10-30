namespace DesktopWasm.Wpf;

// Media/power/basecompat slice (ScreenToGif cluster, shim, not port).
//
// Covered gaps: AlignmentX/AlignmentY (PresentationCore System.Windows.Media,
// used by ScreenToGif transition ImageBrushes), MediaPlayer +
// ExceptionEventArgs (PresentationCore System.Windows.Media, used by the
// ViewModel preview players), DrawingContext.DrawVideo (the frame-export
// render path), PowerModes/PowerModeChangedEventArgs/Handler +
// SystemEvents.PowerModeChanged (Microsoft.Win32.SystemEvents, used by the
// Editor/NewRecorder/Recorder/Webcam suspend guards), and
// BaseCompatibilityPreferences (+ nested
// HandleDispatcherRequestProcessingFailureOptions; WindowsBase
// System.Windows, set by App/Options startup).
//
// Verified against the real WPF ref pack (microsoft.windowsdesktop.app.ref
// 9.0.20, Mono.Cecil read): AlignmentX Left=0/Center=1/Right=2, AlignmentY
// Top=0/Center=1/Bottom=2, nested options Continue=0/Throw=1/Reset=2,
// PowerModes Resume=1/StatusChange=2/Suspend=3, MediaPlayer : Animatable
// (public ctor; Open(Uri)/Play/Pause/Stop/Close; Volume/Balance/SpeedRatio/
// Position/Source/ScrubbingEnabled/IsMuted/CanPause/NaturalDuration/
// NaturalVideoWidth/Height/HasAudio/HasVideo/IsBuffering/BufferingProgress/
// DownloadProgress; MediaOpened/MediaEnded EventHandler,
// MediaFailed EventHandler<ExceptionEventArgs>, BufferingStarted/Ended,
// ScriptCommand; Changed inherited), ExceptionEventArgs : EventArgs with an
// internal ctor and ErrorException, DrawVideo(MediaPlayer, Rect),
// PowerModeChangedEventArgs public ctor + Mode, BaseCompatibilityPreferences
// static with FlowDispatcherSynchronizationContextPriority,
// HandleDispatcherRequestProcessingFailure,
// InlineDispatcherSynchronizationContextSend and
// ReuseDispatcherSynchronizationContextInstance.
//
// Engine deviations (documented, no silent zeros): there is no Animatable,
// so MediaPlayer derives from DispatcherObject and declares Changed
// itself (converted `_player.Changed += ...` binds); ExceptionEventArgs
// takes a public ctor (WPF keeps it internal; converted code only
// receives it); there is no decode pipeline, so Open resolves
// synchronously with MediaOpened and NaturalDuration stays Automatic
// (converted HasTimeSpan guards land on Zero), Play with no source raises
// MediaFailed instead of WPF's silent no-op so session error paths run,
// and DrawVideo paints a deterministic dark surface when a source is open
// (nothing when closed) instead of a decoded frame.

/// <summary>Horizontal tile alignment (matches System.Windows.Media.AlignmentX).</summary>
public enum AlignmentX
{
    Left = 0,
    Center = 1,
    Right = 2,
}

/// <summary>Vertical tile alignment (matches System.Windows.Media.AlignmentY).</summary>
public enum AlignmentY
{
    Top = 0,
    Center = 1,
    Bottom = 2,
}

/// <summary>Media failure payload (matches System.Windows.Media.ExceptionEventArgs).</summary>
public sealed class ExceptionEventArgs : EventArgs
{
    /// <summary>Wraps the decode/session failure (WPF keeps this ctor internal; the engine needs a public one for tests and drivers).</summary>
    public ExceptionEventArgs(Exception errorException)
    {
        ErrorException = errorException ?? throw new ArgumentNullException(nameof(errorException));
    }

    /// <summary>The failure that raised <see cref="MediaPlayer.MediaFailed"/>.</summary>
    public Exception ErrorException { get; }
}

/// <summary>
/// Session media player (matches System.Windows.Media.MediaPlayer).
/// A retained state machine over an explicit source: Open resolves
/// synchronously (no decode pipeline in the session, so duration stays
/// Automatic and HasAudio/HasVideo stay false), Play/Pause/Stop drive the
/// playing flag, Position is retained and seekable, and reaching a known
/// duration through Position raises MediaEnded. Volume clamps to 0..1 and
/// Balance to -1..1 like the underlying pipeline.
/// </summary>
public class MediaPlayer : DispatcherObject
{
    private Uri? _source;
    private TimeSpan _position;
    private Duration _naturalDuration = Duration.Automatic;
    private double _volume = 0.5;
    private double _balance;
    private double _speedRatio = 1;
    private bool _isMuted;
    private bool _scrubbingEnabled;
    private bool _playing;

    /// <summary>Raised when a source resolves (synchronously in the session).</summary>
    public event EventHandler? MediaOpened;

    /// <summary>Raised when Position reaches a known NaturalDuration.</summary>
    public event EventHandler? MediaEnded;

    /// <summary>Raised when playback cannot start (Play with no source).</summary>
    public event EventHandler<ExceptionEventArgs>? MediaFailed;

    // The session never buffers, so these parity events never fire in-assembly;
    // they exist so converted subscriptions bind instead of failing loudly.
#pragma warning disable CS0067 // Event is never used (parity surface, never raised)
    /// <summary>Buffering boundary: the session never buffers, kept for converted subscriptions.</summary>
    public event EventHandler? BufferingStarted;

    /// <summary>Buffering boundary: the session never buffers, kept for converted subscriptions.</summary>
    public event EventHandler? BufferingEnded;
#pragma warning restore CS0067

    /// <summary>State invalidation (Animatable parity; the engine has no Animatable).</summary>
    public event EventHandler? Changed;

    /// <summary>Open media (matches the WPF default Volume 0.5; upstream sets 0 explicitly).</summary>
    public MediaPlayer()
    {
    }

    /// <summary>Current source (null when closed).</summary>
    public Uri? Source => _source;

    /// <summary>Playback position (retained; seeks clamp at zero and raise <see cref="MediaEnded"/> at a known duration).</summary>
    public TimeSpan Position
    {
        get => _position;
        set
        {
            if (value < TimeSpan.Zero) value = TimeSpan.Zero;
            _position = value;
            OnChanged();
            if (_naturalDuration.HasTimeSpan && _position >= _naturalDuration.TimeSpan)
            {
                _playing = false;
                MediaEnded?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    /// <summary>Volume 0..1 (default 0.5; out-of-range values clamp).</summary>
    public double Volume
    {
        get => _volume;
        set { _volume = Math.Clamp(value, 0, 1); OnChanged(); }
    }

    /// <summary>Balance -1..1 (default 0; out-of-range values clamp).</summary>
    public double Balance
    {
        get => _balance;
        set { _balance = Math.Clamp(value, -1, 1); OnChanged(); }
    }

    /// <summary>Playback rate (default 1; retained).</summary>
    public double SpeedRatio
    {
        get => _speedRatio;
        set { _speedRatio = value; OnChanged(); }
    }

    /// <summary>Muted flag (retained).</summary>
    public bool IsMuted
    {
        get => _isMuted;
        set { _isMuted = value; OnChanged(); }
    }

    /// <summary>Scrubbing flag (retained; upstream preview players set true).</summary>
    public bool ScrubbingEnabled
    {
        get => _scrubbingEnabled;
        set { _scrubbingEnabled = value; OnChanged(); }
    }

    /// <summary>Pause support (true while a source is open).</summary>
    public bool CanPause => _source is not null;

    /// <summary>Media length (Automatic until decoded; the session never decodes).</summary>
    public Duration NaturalDuration => _naturalDuration;

    /// <summary>Frame width (0 without a decode pipeline).</summary>
    public int NaturalVideoWidth => 0;

    /// <summary>Frame height (0 without a decode pipeline).</summary>
    public int NaturalVideoHeight => 0;

    /// <summary>Audio presence (false without a decode pipeline).</summary>
    public bool HasAudio => false;

    /// <summary>Video presence (false without a decode pipeline).</summary>
    public bool HasVideo => false;

    /// <summary>Buffering state (false; the session never buffers).</summary>
    public bool IsBuffering => false;

    /// <summary>Buffering progress (0; the session never buffers).</summary>
    public double BufferingProgress => 0;

    /// <summary>Download progress (0; the session never downloads).</summary>
    public double DownloadProgress => 0;

    /// <summary>Opens a source and resolves it synchronously with <see cref="MediaOpened"/>.</summary>
    public void Open(Uri? source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _source = source;
        _position = TimeSpan.Zero;
        _naturalDuration = Duration.Automatic;
        _playing = false;
        MediaOpened?.Invoke(this, EventArgs.Empty);
        OnChanged();
    }

    /// <summary>Starts playback (raises <see cref="MediaFailed"/> with no source; WPF stays silently stopped there).</summary>
    public void Play()
    {
        if (_source is null)
        {
            MediaFailed?.Invoke(this, new ExceptionEventArgs(new InvalidOperationException("No media source is open.")));
            return;
        }
        _playing = true;
        OnChanged();
    }

    /// <summary>Pauses playback (no-op without a source).</summary>
    public void Pause()
    {
        if (!_playing) return;
        _playing = false;
        OnChanged();
    }

    /// <summary>Stops playback and rewinds to zero.</summary>
    public void Stop()
    {
        _playing = false;
        _position = TimeSpan.Zero;
        OnChanged();
    }

    /// <summary>Closes the source (no events, like WPF).</summary>
    public void Close()
    {
        _source = null;
        _position = TimeSpan.Zero;
        _playing = false;
        OnChanged();
    }

    private void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);
}

/// <summary>
/// Dispatcher compatibility switches (matches System.Windows.BaseCompatibilityPreferences).
/// Retained settings only: the session has no dispatcher-backoff machinery,
/// so the switches never change behavior, but converted startup code
/// (App/Options quota workaround) reads and writes them.
/// </summary>
public static class BaseCompatibilityPreferences
{
    /// <summary>Request-processing failure handling (default Continue).</summary>
    public static HandleDispatcherRequestProcessingFailureOptions HandleDispatcherRequestProcessingFailure { get; set; }
        = HandleDispatcherRequestProcessingFailureOptions.Continue;

    /// <summary>Dispatcher synchronization-context reuse (default false).</summary>
    public static bool ReuseDispatcherSynchronizationContextInstance { get; set; }

    /// <summary>Dispatcher synchronization-context send inlining (default false).</summary>
    public static bool InlineDispatcherSynchronizationContextSend { get; set; }

    /// <summary>Dispatcher synchronization-context priority flow (default false).</summary>
    public static bool FlowDispatcherSynchronizationContextPriority { get; set; }

    /// <summary>Failure handling (matches the nested WPF enum: Continue=0/Throw=1/Reset=2).</summary>
    public enum HandleDispatcherRequestProcessingFailureOptions
    {
        Continue = 0,
        Throw = 1,
        Reset = 2,
    }
}
