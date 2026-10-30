using System.ComponentModel;
using System.Reflection;

namespace DesktopWasm.Wpf
{

// HandyControl entry startup scope (slice-76): the startup splash image plus
// the chat-box audio player. Both are real behavior over existing session
// infrastructure, never fabricated pixels or sound.
//
// SplashScreen (WindowsBase System.Windows, rides the default converter
// rule) resolves its resource through the same pack stores as Source
// dictionaries and BitmapImage (registered pack text, converted-assembly
// manifest and .g.resources bytes) and decodes it with the shared Skia
// pipeline, so the converted HandyControlDemo App.OpenSplashScreen shows
// the real Resources/Img/Cover.png. There is no native splash window in
// the session: visibility is retained state the host overlay reads, and
// WpfAppBoot closes auto-close splashes when the main window resolves
// (WPF closes them when the main window is shown).
//
// SoundPlayer (System.Windows.Extensions System.Media, needs the explicit
// converter override like the other non-System.Windows.* rows) is a
// retained state machine over the same no-decode-pipeline contract as the
// session MediaPlayer: Load parses a real WAVE header, Play/PlayLooping/
// PlaySync retain playing state, Stop clears it. There is no audio sink in
// the session, so playing emits no audible output (documented boundary).

/// <summary>Startup splash image (matches System.Windows.SplashScreen).</summary>
public class SplashScreen
{
    private static readonly List<SplashScreen> Shown = new();
    private static readonly object ShownGate = new();

    /// <summary>
    /// Test seam for the resource lookup (defaults to the pack stores).
    /// Same (resourceName, assemblyName) -> bytes contract as
    /// <see cref="WpfAppBoot.FindPackBytes"/>.
    /// </summary>
    internal static Func<string, string?, byte[]?>? ResourceOverride;

    private DispatcherTimer? _closeTimer;

    /// <summary>Retains the resource name (matches SplashScreen(string)).</summary>
    public SplashScreen(string resourceName) : this(null, resourceName)
    {
    }

    /// <summary>Retains the resource assembly and name (matches SplashScreen(Assembly, string)).</summary>
    public SplashScreen(Assembly? resourceAssembly, string resourceName)
    {
        ArgumentNullException.ThrowIfNull(resourceName);
        ResourceAssembly = resourceAssembly;
        ResourceName = resourceName;
    }

    /// <summary>Splash resource path (matches the ctor argument).</summary>
    public string ResourceName { get; }

    /// <summary>Resource assembly (null scans the session assemblies).</summary>
    public Assembly? ResourceAssembly { get; }

    /// <summary>Whether the splash is currently shown.</summary>
    public bool IsVisible { get; private set; }

    /// <summary>Whether the splash closes with the main window.</summary>
    public bool AutoClose { get; private set; }

    /// <summary>Topmost hint (retained; the session has no z-order sink).</summary>
    public bool TopMost { get; private set; }

    /// <summary>Decoded splash image (set by Show).</summary>
    public BitmapImage? Image { get; private set; }

    /// <summary>Shows the splash (matches Show(bool)).</summary>
    public void Show(bool autoClose) => Show(autoClose, false);

    /// <summary>Shows the splash (matches Show(bool, bool)).</summary>
    public void Show(bool autoClose, bool topMost)
    {
        if (IsVisible)
            throw new InvalidOperationException("SplashScreen.Show may only be called once per instance.");
        var bytes = (ResourceOverride ?? WpfAppBoot.FindPackBytes)(
            ResourceName, ResourceAssembly?.GetName().Name)
            ?? throw new FileNotFoundException(
                $"SplashScreen resource '{ResourceName}' was not found" +
                (ResourceAssembly is null ? "." : $" in assembly '{ResourceAssembly.GetName().Name}'."));
        var image = new BitmapImage();
        image.StreamSource = new MemoryStream(bytes);
        image.EndInit();
        if (image.PixelWidth <= 0 || image.PixelHeight <= 0)
            throw new InvalidDataException($"SplashScreen resource '{ResourceName}' decoded to an empty image.");
        Image = image;
        AutoClose = autoClose;
        TopMost = topMost;
        IsVisible = true;
        lock (ShownGate) Shown.Add(this);
    }

    /// <summary>Closes the splash after the fadeout (matches Close(TimeSpan)).</summary>
    public void Close(TimeSpan fadeoutDuration)
    {
        if (!IsVisible)
            throw new InvalidOperationException("SplashScreen.Close needs a shown splash.");
        if (fadeoutDuration < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(fadeoutDuration), "Fadeout duration cannot be negative.");
        _closeTimer?.Stop();
        _closeTimer = null;
        if (fadeoutDuration == TimeSpan.Zero)
        {
            Hide();
            return;
        }
        var timer = new DispatcherTimer { Interval = fadeoutDuration };
        timer.Tick += (_, _) => { timer.Stop(); Hide(); };
        _closeTimer = timer;
        timer.Start();
    }

    private void Hide()
    {
        _closeTimer?.Stop();
        _closeTimer = null;
        IsVisible = false;
        lock (ShownGate) Shown.Remove(this);
    }

    /// <summary>
    /// Closes every shown auto-close splash (called by WpfAppBoot when the
    /// main window resolves, matching WPF closing auto-close splashes when
    /// the main window is shown).
    /// </summary>
    internal static void CloseAutoShown()
    {
        SplashScreen[] snapshot;
        lock (ShownGate) snapshot = Shown.ToArray();
        foreach (var splash in snapshot)
        {
            if (splash is { IsVisible: true, AutoClose: true })
                splash.Hide();
        }
    }

    /// <summary>Session reset for tests (hides everything, clears the seam).</summary>
    internal static void ResetForTests()
    {
        ResourceOverride = null;
        SplashScreen[] snapshot;
        lock (ShownGate) snapshot = Shown.ToArray();
        foreach (var splash in snapshot) splash.Hide();
        lock (ShownGate) Shown.Clear();
    }
}

} // namespace DesktopWasm.Wpf

namespace System.Media
{

/// <summary>
/// WAV audio player (matches System.Media.SoundPlayer): retained location,
/// stream, timeout and load state over a real WAVE header parse. There is
/// no audio sink in the session, so Play/PlayLooping/PlaySync retain
/// playing state without audible output (the MediaPlayer
/// no-decode-pipeline contract); Stop clears it. Binary serialization
/// (ISerializable) has no session path and is not carried: converted code
/// never touches it.
/// </summary>
public class SoundPlayer : Component
{
    private string _soundLocation = string.Empty;
    private Stream? _stream;
    private int _loadTimeout = 10000;
    private bool _loadCompleted;
    private bool _playing;
    private bool _looping;

    /// <summary>Empty player (matches SoundPlayer()).</summary>
    public SoundPlayer()
    {
    }

    /// <summary>Player over a path (matches SoundPlayer(string)).</summary>
    public SoundPlayer(string soundLocation) : this()
    {
        SoundLocation = soundLocation;
    }

    /// <summary>Player over a stream (matches SoundPlayer(Stream)).</summary>
    public SoundPlayer(Stream stream) : this()
    {
        Stream = stream;
    }

    /// <summary>Whether a load completed (matches IsLoadCompleted).</summary>
    public bool IsLoadCompleted => _loadCompleted;

    /// <summary>Load timeout in milliseconds (matches LoadTimeout, default 10000).</summary>
    public int LoadTimeout
    {
        get => _loadTimeout;
        set
        {
            if (value < 0)
                throw new ArgumentOutOfRangeException(nameof(value), "LoadTimeout cannot be negative.");
            _loadTimeout = value;
        }
    }

    /// <summary>Audio path (matches SoundLocation, default empty).</summary>
    [DefaultValue("")]
    public string SoundLocation
    {
        get => _soundLocation;
        set
        {
            var next = value ?? string.Empty;
            if (string.Equals(_soundLocation, next, StringComparison.Ordinal)) return;
            _soundLocation = next;
            Unload();
            OnSoundLocationChanged(EventArgs.Empty);
        }
    }

    /// <summary>Audio stream (matches Stream).</summary>
    [DefaultValue(null)]
    public Stream? Stream
    {
        get => _stream;
        set
        {
            if (ReferenceEquals(_stream, value)) return;
            _stream = value;
            Unload();
            OnStreamChanged(EventArgs.Empty);
        }
    }

    /// <summary>User tag (matches Tag).</summary>
    public object? Tag { get; set; }

    /// <summary>Whether playback was started and not stopped (session state; no audio sink).</summary>
    internal bool IsPlaying => _playing;

    /// <summary>Whether looping playback was started (session state; no audio sink).</summary>
    internal bool IsLooping => _looping;

    /// <summary>Parsed WAVE format of the loaded sound (session state).</summary>
    internal WavFormat? LoadedFormat { get; private set; }

    /// <summary>Raised when an async load completes (matches LoadCompleted).</summary>
    public event AsyncCompletedEventHandler? LoadCompleted;

    /// <summary>Raised when SoundLocation changes (matches SoundLocationChanged).</summary>
    public event EventHandler? SoundLocationChanged;

    /// <summary>Raised when Stream changes (matches StreamChanged).</summary>
    public event EventHandler? StreamChanged;

    /// <summary>Loads synchronously (matches Load; parses a real WAVE header).</summary>
    public void Load()
    {
        byte[] bytes;
        if (_stream is not null)
        {
            using var copy = new MemoryStream();
            _stream.CopyTo(copy);
            bytes = copy.ToArray();
        }
        else if (_soundLocation.Length > 0)
        {
            bytes = File.ReadAllBytes(_soundLocation);
        }
        else
        {
            throw new InvalidOperationException("SoundPlayer.Load needs SoundLocation or Stream.");
        }
        LoadedFormat = WavFormat.Parse(bytes);
        _loadCompleted = true;
    }

    /// <summary>
    /// Loads and raises LoadCompleted (matches LoadAsync; synchronous in the
    /// session like MediaPlayer.Open, then the completion event).
    /// </summary>
    public void LoadAsync()
    {
        Load();
        OnLoadCompleted(new AsyncCompletedEventArgs(null, false, null));
    }

    /// <summary>Starts playback (matches Play; retains state, no audio sink).</summary>
    public void Play()
    {
        EnsureLoaded();
        _playing = true;
        _looping = false;
    }

    /// <summary>Starts looping playback (matches PlayLooping; retains state, no audio sink).</summary>
    public void PlayLooping()
    {
        EnsureLoaded();
        _playing = true;
        _looping = true;
    }

    /// <summary>
    /// Validates and returns (matches PlaySync; without an audio sink there
    /// is no audible duration to block on, so it returns after load).
    /// </summary>
    public void PlaySync()
    {
        EnsureLoaded();
        _playing = false;
        _looping = false;
    }

    /// <summary>Stops playback (matches Stop).</summary>
    public void Stop()
    {
        _playing = false;
        _looping = false;
    }

    /// <summary>Raises LoadCompleted (matches OnLoadCompleted).</summary>
    protected virtual void OnLoadCompleted(AsyncCompletedEventArgs e) =>
        LoadCompleted?.Invoke(this, e);

    /// <summary>Raises SoundLocationChanged (matches OnSoundLocationChanged).</summary>
    protected virtual void OnSoundLocationChanged(EventArgs e) =>
        SoundLocationChanged?.Invoke(this, e);

    /// <summary>Raises StreamChanged (matches OnStreamChanged).</summary>
    protected virtual void OnStreamChanged(EventArgs e) =>
        StreamChanged?.Invoke(this, e);

    /// <summary>Stops playback on dispose (matches the Component contract).</summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing) Stop();
        base.Dispose(disposing);
    }

    private void EnsureLoaded()
    {
        if (!_loadCompleted) Load();
    }

    private void Unload()
    {
        Stop();
        _loadCompleted = false;
        LoadedFormat = null;
    }
}

/// <summary>
/// Parsed WAVE header (RIFF/WAVE/fmt/data) backing SoundPlayer.Load.
/// </summary>
internal sealed record WavFormat(int Channels, int SampleRate, int BitsPerSample, int DataBytes)
{
    /// <summary>Parses and validates WAVE bytes (precise InvalidDataException when not WAVE).</summary>
    public static WavFormat Parse(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        try
        {
            using var input = new MemoryStream(bytes, writable: false);
            using var reader = new BinaryReader(input);
            if (bytes.Length < 44 || new string(reader.ReadChars(4)) != "RIFF")
                throw new InvalidDataException("Sound data is not a WAVE file (missing RIFF).");
            reader.ReadInt32();
            if (new string(reader.ReadChars(4)) != "WAVE")
                throw new InvalidDataException("Sound data is not a WAVE file (missing WAVE).");
            int channels = 0, rate = 0, bits = 0, data = -1;
            while (input.Position + 8 <= input.Length)
            {
                var id = new string(reader.ReadChars(4));
                var size = reader.ReadInt32();
                if (size < 0 || input.Position + size > input.Length)
                    throw new InvalidDataException($"Sound data has a corrupt WAVE chunk '{id}'.");
                var next = input.Position + size;
                if (id == "fmt " && size >= 16)
                {
                    var format = reader.ReadInt16();
                    if (format != 1 && format != 3 && format != 0xFFFE)
                        throw new InvalidDataException($"Sound data uses unsupported WAVE format {format} (PCM/float only).");
                    channels = reader.ReadInt16();
                    rate = reader.ReadInt32();
                    reader.ReadInt32();
                    reader.ReadInt16();
                    bits = reader.ReadInt16();
                    if (channels <= 0 || rate <= 0 || (bits != 8 && bits != 16 && bits != 24 && bits != 32))
                        throw new InvalidDataException("Sound data has an invalid WAVE format chunk.");
                }
                else if (id == "data")
                {
                    data = size;
                }
                input.Position = next;
            }
            if (data < 0)
                throw new InvalidDataException("Sound data has no WAVE data chunk.");
            return new WavFormat(channels, rate, bits, data);
        }
        catch (EndOfStreamException ex)
        {
            throw new InvalidDataException("Sound data is truncated (not a complete WAVE file).", ex);
        }
    }
}

} // namespace System.Media
