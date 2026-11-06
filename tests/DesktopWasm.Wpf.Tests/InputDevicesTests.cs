using DesktopWasm.Core;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Input devices + dispatcher ops (Step 38): key event data, keyboard
/// state, gestures/converters/interop, focus-contract events, timer ticks
/// and operation status/wait/abort.
/// </summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class InputDevicesTests : IDisposable
{
    private readonly WasmInputRouter _router = new();
    private readonly WpfInputManager _manager;
    private readonly List<DispatcherTimer> _timers = new();

    public InputDevicesTests()
    {
        WpfAnimationClock.ResetForTests();
        // Rebind the loop pump to this thread (see ThreadingTests).
        WasmDispatcher.Current = new WasmDispatcher(new WasmDispatcherPump());
        _manager = new WpfInputManager(_router);
    }

    public void Dispose()
    {
        WpfAnimationClock.ResetForTests();
        foreach (var timer in _timers) timer.Stop();
        _timers.Clear();
    }

    private sealed class ProbeBox : FrameworkElement
    {
        public override bool Focusable => true;
        protected override WpfSize MeasureCore(WpfSize available) => new(10, 10);
        protected override void ArrangeCore(WpfRect finalRect) { }
        protected override void OnRender(DrawingContext dc) { }
    }

    private DispatcherTimer NewTimer()
    {
        var timer = new DispatcherTimer();
        _timers.Add(timer);
        return timer;
    }

    private void DispatchKey(string key, bool isDown, bool ctrl = false, bool shift = false, bool alt = false) =>
        _router.DispatchKeyboard(new WasmKeyArgs(new WasmWindowHandle(1), key, isDown, ctrl, shift, alt));

    [Fact]
    public void Key_events_fire_with_key_state_and_handled_sticks()
    {
        var box = new ProbeBox();
        _manager.AddRoot(box);
        Assert.True(box.Focus());

        var seen = new List<string>();
        box.PreviewKeyDown += (_, e) => seen.Add($"preview:{e.Key}:{e.IsDown}");
        box.PreviewKeyUp += (_, e) => seen.Add($"preview:{e.Key}:{e.IsDown}");
        box.KeyDown += (_, e) => { seen.Add($"down:{e.Key}"); e.Handled = true; };
        box.KeyUp += (_, e) => seen.Add($"up:{e.Key}:{e.IsUp}:{e.IsRepeat}");

        DispatchKey("a", isDown: true);
        Assert.Equal(["preview:A:True", "down:A"], seen);
        Assert.True(box.IsKeyboardFocused);

        seen.Clear();
        DispatchKey("a", isDown: false);
        Assert.Equal(["preview:A:False", "up:A:True:False"], seen);
    }

    [Fact]
    public void Handled_preview_suppresses_bubble_and_defaults()
    {
        var box = new ProbeBox();
        _manager.AddRoot(box);
        Assert.True(box.Focus());

        var bubbled = 0;
        box.KeyDown += (_, _) => bubbled++;
        box.PreviewKeyDown += (_, e) => e.Handled = true;

        DispatchKey("Tab", isDown: true);
        Assert.Equal(0, bubbled);
        Assert.Same(box, _manager.Focused);
    }

    [Fact]
    public void Unhandled_tab_still_moves_focus_between_boxes()
    {
        var first = new ProbeBox();
        var second = new ProbeBox();
        _manager.AddRoot(first);
        _manager.AddRoot(second);
        first.Measure(new WpfSize(100, 100));
        first.Arrange(new WpfRect(0, 0, 100, 100));
        second.Measure(new WpfSize(100, 100));
        second.Arrange(new WpfRect(0, 0, 100, 100));
        Assert.True(first.Focus());

        DispatchKey("Tab", isDown: true);
        Assert.Same(second, _manager.Focused);
    }

    [Fact]
    public void Keyboard_tracks_modifiers_and_held_keys()
    {
        var box = new ProbeBox();
        _manager.AddRoot(box);
        Assert.True(box.Focus());

        Assert.Equal(ModifierKeys.None, Keyboard.Modifiers);
        DispatchKey("Control", isDown: true, ctrl: true);
        Assert.Equal(ModifierKeys.Control, Keyboard.Modifiers);
        Assert.True(Keyboard.IsKeyDown(Key.LeftCtrl));

        DispatchKey("a", isDown: true, ctrl: true);
        Assert.True(Keyboard.IsKeyDown(Key.A));

        DispatchKey("a", isDown: false, ctrl: true);
        Assert.False(Keyboard.IsKeyDown(Key.A));
        Assert.True(Keyboard.IsKeyDown(Key.LeftCtrl));

        DispatchKey("Control", isDown: false);
        Assert.Equal(ModifierKeys.None, Keyboard.Modifiers);
        Assert.False(Keyboard.IsKeyDown(Key.LeftCtrl));
    }

    [Fact]
    public void Focus_contract_events_fire_and_keyboard_follows()
    {
        var first = new ProbeBox();
        var second = new ProbeBox();
        _manager.AddRoot(first);
        _manager.AddRoot(second);

        var log = new List<string>();
        first.GotKeyboardFocus += (_, e) => log.Add($"got:{ReferenceEquals(e.NewFocus, first)}");
        first.LostKeyboardFocus += (_, e) => log.Add($"lost:{ReferenceEquals(e.OldFocus, first)}");
        first.GotFocus += (_, _) => log.Add("gotfocus");
        first.LostFocus += (_, _) => log.Add("lostfocus");
        // Converted TextFieldAssist subscribes add_IsKeyboardFocusWithinChanged:
        // within tracks focus here, so it fires with the focused flip.
        first.IsKeyboardFocusWithinChanged += (_, e) => log.Add($"within:{e.NewValue}");

        Assert.True(first.Focus());
        Assert.True(first.IsKeyboardFocused);
        Assert.True(first.IsKeyboardFocusWithin);
        Assert.Contains("within:True", log);
        Assert.Same(first, Keyboard.FocusedElement);
        Assert.Same(first, Keyboard.PrimaryDevice.FocusedElement);

        Assert.True(second.Focus());
        Assert.False(first.IsKeyboardFocused);
        Assert.Same(second, Keyboard.FocusedElement);
        Assert.Contains("got:True", log);
        Assert.Contains("lost:True", log);
        Assert.Contains("gotfocus", log);
        Assert.Contains("lostfocus", log);

        Assert.Same(second, Keyboard.Focus(second));
        Assert.Null(Keyboard.Focus(null));
    }

    [Fact]
    public void Keyboard_focused_element_matches_genuine_wpf_contract()
    {
        // Converted binaries (TestApp/AvalonDock joint closure) bind
        // Keyboard.get_FocusedElement with the genuine-WPF IInputElement
        // return contract. A narrower engine type resolves no method and
        // surfaces as MissingMethodException once the joint package loads
        // in-process during full runs.
        Assert.Equal(typeof(IInputElement), typeof(Keyboard).GetProperty("FocusedElement")!.PropertyType);
        Assert.Equal(typeof(IInputElement), typeof(KeyboardDevice).GetProperty("FocusedElement")!.PropertyType);
    }

    [Fact]
    public void Gestures_match_convert_and_display()
    {
        var save = new KeyGesture(Key.N, ModifierKeys.Control);
        Assert.Equal("Ctrl+N", save.GetDisplayStringForCulture(System.Globalization.CultureInfo.InvariantCulture));

        var device = Keyboard.PrimaryDevice;
        device.Modifiers = ModifierKeys.Control;
        try
        {
            Assert.True(save.Matches(new object(), KeyDownArgs(device, Key.N)));
            device.Modifiers = ModifierKeys.None;
            Assert.False(save.Matches(new object(), KeyDownArgs(device, Key.N)));
        }
        finally { device.Modifiers = ModifierKeys.None; }

        var converted = Assert.IsType<KeyGesture>(new KeyGestureConverter().ConvertFrom(null, null, "Ctrl+Shift+F5"));
        Assert.Equal(Key.F5, converted.Key);
        Assert.Equal(ModifierKeys.Control | ModifierKeys.Shift, converted.Modifiers);

        Assert.Equal(Key.Escape, new KeyConverter().ConvertFrom(null, null, "escape"));
        Assert.Equal("A", new KeyConverter().ConvertTo(null, null, Key.A, typeof(string)));
        Assert.Equal(ModifierKeys.Control | ModifierKeys.Alt, new ModifierKeysConverter().ConvertFrom(null, null, "Ctrl+Alt"));
        Assert.Throws<FormatException>(() => new KeyGestureConverter().ConvertFrom(null, null, "Ctrl+Bogus"));
    }

    [Fact]
    public void KeyInterop_round_trips_documented_ranges()
    {
        Assert.Equal(65, KeyInterop.VirtualKeyFromKey(Key.A));
        Assert.Equal(Key.A, KeyInterop.KeyFromVirtualKey(65));
        Assert.Equal(48, KeyInterop.VirtualKeyFromKey(Key.D0));
        Assert.Equal(112, KeyInterop.VirtualKeyFromKey(Key.F1));
        Assert.Equal(37, KeyInterop.VirtualKeyFromKey(Key.Left));
        Assert.Equal(13, KeyInterop.VirtualKeyFromKey(Key.Enter));
        Assert.Equal(32, KeyInterop.VirtualKeyFromKey(Key.Space));
        Assert.Equal(Key.None, KeyInterop.KeyFromVirtualKey(9999));
        Assert.Equal(0, KeyInterop.VirtualKeyFromKey(Key.DeadCharProcessed));
    }

    [Fact]
    public void DispatcherTimer_ticks_on_interval_and_stops()
    {
        var timer = NewTimer();
        timer.Interval = TimeSpan.FromSeconds(1);
        var ticks = 0;
        timer.Tick += (_, _) => ticks++;
        timer.Start();
        Assert.True(timer.IsEnabled);

        var start = DateTime.UtcNow;
        DispatcherTimer.TickAll(start.AddMilliseconds(500));
        Assert.Equal(0, ticks);
        DispatcherTimer.TickAll(start.AddSeconds(1.5));
        Assert.Equal(1, ticks);

        timer.Stop();
        Assert.False(timer.IsEnabled);
        DispatcherTimer.TickAll(start.AddSeconds(5));
        Assert.Equal(1, ticks);

        Assert.Throws<ArgumentOutOfRangeException>(() => timer.Interval = TimeSpan.FromSeconds(-1));
    }

    [Fact]
    public void DispatcherOperation_runs_waits_and_aborts()
    {
        var dispatcher = Dispatcher.CurrentDispatcher;

        var op = dispatcher.BeginInvoke((Func<int>)(() => 41 + 1));
        Assert.Equal(DispatcherOperationStatus.Pending, op.Status);
        Assert.True(op.Wait(TimeSpan.FromSeconds(5)));
        Assert.Equal(DispatcherOperationStatus.Completed, op.Status);
        Assert.Equal(42, op.Result);

        var gated = dispatcher.BeginInvoke((Action)(() => { }));
        Assert.True(gated.Abort());
        Assert.Equal(DispatcherOperationStatus.Aborted, gated.Status);

        var completed = dispatcher.BeginInvoke((Action)(() => { }));
        Assert.True(completed.Wait(TimeSpan.FromSeconds(5)));
        Assert.False(completed.Abort());
    }

    [Fact]
    public void Faulted_operations_fault_the_task_unless_handled()
    {
        var dispatcher = Dispatcher.CurrentDispatcher;

        var failed = dispatcher.BeginInvoke((Action)(() => throw new InvalidOperationException("boom")));
        Assert.True(failed.Wait(TimeSpan.FromSeconds(5)));
        Assert.True(failed.Task.IsFaulted);
        Assert.Throws<InvalidOperationException>(() => failed.Result);

        var seen = new List<string>();
        void Handler(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            seen.Add(e.Exception.Message);
            e.Handled = true;
        }
        dispatcher.UnhandledException += Handler;
        try
        {
            var swallowed = dispatcher.BeginInvoke((Action)(() => throw new InvalidOperationException("swallowed")));
            Assert.True(swallowed.Wait(TimeSpan.FromSeconds(5)));
            Assert.False(swallowed.Task.IsFaulted);
            Assert.Equal(["swallowed"], seen);
        }
        finally { dispatcher.UnhandledException -= Handler; }

        var expired = dispatcher.BeginInvoke((Action)(() => { }));
        Assert.True(expired.Abort());
        Assert.False(expired.Wait(TimeSpan.FromMilliseconds(50)));
    }

    [Fact]
    public void Invoke_honors_the_unhandled_filter()
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        var seen = new List<string>();
        void Handler(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            seen.Add(e.Exception.Message);
            e.Handled = true;
        }
        dispatcher.UnhandledException += Handler;
        try
        {
            dispatcher.Invoke(() => throw new InvalidOperationException("handled-boom"));
            Assert.Equal(["handled-boom"], seen);
        }
        finally { dispatcher.UnhandledException -= Handler; }

        Assert.Throws<InvalidOperationException>(() => dispatcher.Invoke(() => throw new InvalidOperationException("unhandled-boom")));
    }

    [Fact]
    public void Presentation_source_reports_no_target()
    {
        Assert.Null(PresentationSource.FromVisual(new ProbeBox()));
    }

    private static KeyEventArgs KeyDownArgs(KeyboardDevice device, Key key) =>
        new KeyEventArgsProbe(device, key);

    private sealed class KeyEventArgsProbe : KeyEventArgs
    {
        public KeyEventArgsProbe(KeyboardDevice? device, Key key)
            : base(device, Environment.TickCount, key, Key.None, isDown: true, isRepeat: false)
        {
        }
    }
}
