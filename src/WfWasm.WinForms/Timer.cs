using System.ComponentModel;

namespace System.Windows.Forms;

// Headless System.Windows.Forms.Timer (fork overlay: the upstream file is
// excluded in WfWasm.WinForms.csproj). The desktop implementation pumps
// WM_TIMER through a native window, which cannot exist in the browser —
// without this, every headless timer silently never ticks (Telerik
// animations freeze at their start values, clocks and polling UI stall).
// Same public contract (Tag included: Telerik's animation settings stash
// their element in it); ticks come from a System.Threading.Timer firing
// one-shot callbacks that re-arm by recreating the one-shot (Change-based
// reschedule delivered exactly one tick in the served runtime), so Tick
// handlers never overlap the way message coalescing prevents on desktop.
// Callbacks arrive on pool
// threads: control access from Tick follows the same Invoke marshaling
// rules as desktop. A throwing Tick is reported to the console and the
// timer keeps running instead of taking down the runtime, matching the
// desktop "dialog and continue" outcome as closely as headless allows.
[DefaultProperty(nameof(Interval))]
[DefaultEvent(nameof(Tick))]
[ToolboxItemFilter("System.Windows.Forms")]
public class Timer : Component
{
    private int _interval = 100;
    private bool _enabled;
    private System.Threading.Timer? _timer;
    private readonly Lock _lock = new();
    private int _firedOnce;
    private int _tickCount;

    public Timer()
    {
    }

    public Timer(IContainer container)
        : this()
    {
        ArgumentNullException.ThrowIfNull(container);

        container.Add(this);
    }

    [DefaultValue(null)]
    public object? Tag { get; set; }

    private protected EventHandler? _onTimer;

    public event EventHandler Tick
    {
        add => _onTimer += value;
        remove => _onTimer -= value;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            lock (_lock)
            {
                StopTimerLocked();
                _enabled = false;
            }
        }

        base.Dispose(disposing);
    }

    [DefaultValue(false)]
    public virtual bool Enabled
    {
        get => _enabled;
        set
        {
            lock (_lock)
            {
                if (_enabled == value)
                {
                    return;
                }

                _enabled = value;

                if (DesignMode)
                {
                    return;
                }

                if (value)
                {
                    StartTimerLocked();
                    Console.WriteLine($"[WfWasm.Timer] armed interval={_interval}");
                }
                else
                {
                    StopTimerLocked();
                }
            }
        }
    }

    [DefaultValue(100)]
    public int Interval
    {
        get => _interval;
        set
        {
            lock (_lock)
            {
                if (value < 1)
                {
                    throw new ArgumentOutOfRangeException(nameof(value), value, $"Timer interval must be at least 1ms, got {value}.");
                }

                if (_interval == value)
                {
                    return;
                }

                _interval = value;

                if (_enabled && !DesignMode)
                {
                    // Change the cadence without dropping the enabled state.
                    StartTimerLocked();
                }
            }
        }
    }

    protected virtual void OnTick(EventArgs e) => _onTimer?.Invoke(this, e);

    public void Start() => Enabled = true;

    public void Stop() => Enabled = false;

    public override string ToString() => $"{base.ToString()}, Interval: {Interval}";

    private void StartTimerLocked()
    {
        StopTimerLocked();
        try
        {
            _timer = new System.Threading.Timer(
                static state => ((Timer)state!).FireOnce(),
                this,
                _interval,
                System.Threading.Timeout.Infinite);
            _firedOnce = 0;
            _tickCount = 0;
        }
        catch (Exception ex)
        {
            // Served diagnosis needs the arming fault even when a caller
            // swallows it (silent never-ticks look identical otherwise).
            Console.Error.WriteLine($"[WfWasm.Timer] arm failed: {ex.GetType().Name}: {ex.Message}");
            throw;
        }
    }

    private void StopTimerLocked()
    {
        System.Threading.Timer? timer = _timer;
        _timer = null;
        timer?.Dispose();
    }

    /// <summary>Ticks delivered since the current arming (reset by Start).</summary>
    public int TickCount => System.Threading.Volatile.Read(ref _tickCount);

    private void FireOnce()
    {
        if (System.Threading.Interlocked.Exchange(ref _firedOnce, 1) == 0)
        {
            // One line per arming: proves Threading.Timer callbacks arrive
            // without spamming per-tick on fast cadences.
            Console.WriteLine("[WfWasm.Timer] first tick");
        }

        int n = System.Threading.Interlocked.Increment(ref _tickCount);
        if (n <= 3 || n % 50 == 0)
        {
            // Sparse continuation proof: with Change-based re-arm the
            // served wizard timer delivered exactly one tick (PSTALL
            // WC1534: armed + first tick, then a frozen progress bar and
            // zero pixels with no Tick-failed line). A climbing count
            // names repeat delivery; a stuck count names the re-arm.
            Console.WriteLine($"[WfWasm.Timer] tick #{n} interval={_interval}");
        }

        try
        {
            OnTick(EventArgs.Empty);
        }
        catch (Exception ex)
        {
            // Desktop shows the thread-exception dialog and keeps pumping;
            // headless the console is the dialog. Never let one bad Tick
            // kill the runtime or wedge the cadence.
            Console.Error.WriteLine($"[WfWasm.Timer] Tick failed: {ex.GetType().Name}: {ex.Message}");
        }

        lock (_lock)
        {
            if (!_enabled || DesignMode)
            {
                return;
            }

            // Re-arm by recreating the one-shot, not by Change: the
            // served runtime delivered the constructed one-shot exactly
            // once and then went silent (no failure, no reschedule), so
            // Change cannot be trusted here. Recreating after OnTick
            // keeps the non-overlap contract (the next callback cannot
            // start until this one finishes re-arming).
            try
            {
                StopTimerLocked();
                _firedOnce = 1;
                _timer = new System.Threading.Timer(
                    static state => ((Timer)state!).FireOnce(),
                    this,
                    _interval,
                    System.Threading.Timeout.Infinite);
            }
            catch (Exception ex)
            {
                // Served diagnosis needs the re-arm fault: a dead re-arm
                // with Enabled still true is indistinguishable from a
                // wedged handler otherwise.
                Console.Error.WriteLine($"[WfWasm.Timer] re-arm failed: {ex.GetType().Name}: {ex.Message}");
                _timer = null;
                _enabled = false;
            }
        }
    }
}
