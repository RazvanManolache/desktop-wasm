extern alias Fork;
extern alias WasmDraw;

using System.Reflection;
using DesktopWasm.Core;
using DesktopWasm.Graphics;
using DesktopWasm.Interop;
using DesktopWasm.Wpf;
using ForkForms = Fork::System.Windows.Forms;
using WfColor = WasmDraw::System.Drawing.Color;
using WfSize = WasmDraw::System.Drawing.Size;
using WfiHost = System.Windows.Forms.Integration.WindowsFormsHost;

namespace DesktopWasm.Interop.Tests;

/// <summary>
/// Mixed input/focus/dispose through the real browser host stack
/// (<see cref="WasmWpfHost"/> + <see cref="WasmInputRouter"/>, the exact
/// managed path under the browser canvas): a WPF window hosting the real
/// bridge over a real fork control. Clicks, focused keys and teardown all
/// cross the WPF/WinForms boundary here — not via the bridge's direct
/// <c>Dispatch*</c> surface (covered by <c>WindowsFormsHostBridgeTests</c>)
/// and not via <c>MixedGuestSession</c> halves (covered by
/// <c>MixedSessionTests</c>/<c>MixedTabTests</c>). No synthesized UI is
/// claimed as original TestApp success; this is the mixed pilot path.
/// </summary>
public sealed class BridgeHostPathTests
{
    private static readonly WasmWindowHandle Handle = new(1);

    private sealed record HostHarness(
        WasmWpfHost Host,
        WasmInputRouter Router,
        Window Window,
        WfiHost Bridge);

    private static HostHarness Create(ForkForms.Control child, bool tick = true)
    {
        var router = new WasmInputRouter();
        var host = new WasmWpfHost(new WasmDispatcherPump(), router);
        var bridge = new WfiHost { Child = child };
        var window = new Window { Content = bridge };
        host.AddWindow(window, new WpfSize(800, 600));
        // Text-painting guests (Button captions, TextBox text) cannot
        // rasterize off-browser; input-only harnesses skip the Tick and
        // drive focus/keys with no render, like the direct Dispatch tests.
        if (tick)
            host.Tick(new FrameContext(1, 16.6));
        return new HostHarness(host, router, window, bridge);
    }

    private static (double X, double Y) Center(WfiHost bridge)
    {
        var rect = bridge.RenderRect;
        Assert.True(rect.Width > 0 && rect.Height > 0, "Bridge has no arranged rect after host Tick.");
        return (rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
    }

    private static void Down(WasmInputRouter router, double x, double y) =>
        router.DispatchPointer(new WasmPointerArgs(Handle, x, y, WasmPointerButton.Left, true));

    private static void Up(WasmInputRouter router, double x, double y) =>
        router.DispatchPointer(new WasmPointerArgs(Handle, x, y, WasmPointerButton.Left, false));

    private static void Key(WasmInputRouter router, string key, bool isDown) =>
        router.DispatchKeyboard(new WasmKeyArgs(Handle, key, isDown, false, false, false));

    [Fact]
    public void Click_through_host_stack_reaches_real_control()
    {
        // Panels paint a solid fill with no text, so the host Tick that
        // lays out the bridge never hits the off-browser text limit; the
        // real Control.Click surface still fires through the routed press.
        var harness = Create(new ForkForms.Panel
        {
            BackColor = WfColor.Red,
            Size = new WfSize(120, 40),
        });
        var panel = Assert.IsType<ForkForms.Panel>(harness.Bridge.Child);
        var clicks = 0;
        panel.Click += (_, _) => clicks++;

        var (x, y) = Center(harness.Bridge);
        Down(harness.Router, x, y);
        Up(harness.Router, x, y);

        Assert.Equal(1, clicks);
        harness.Host.Tick(new FrameContext(2, 33.2));
    }

    [Fact]
    public void Focused_key_through_host_stack_reaches_real_textbox()
    {
        var harness = Create(new ForkForms.TextBox
        {
            Text = string.Empty,
            Size = new WfSize(200, 24),
        }, tick: false);
        var box = Assert.IsType<ForkForms.TextBox>(harness.Bridge.Child);

        // WPF focus landing on the bridge focuses the real control; the
        // routed key then travels the browser input path, not DispatchKey.
        Assert.True(harness.Host.Input.TryFocus(harness.Bridge));
        Assert.Same(harness.Bridge, harness.Host.Input.Focused);
        Assert.True(box.Focused);

        ForkForms.Keys? seen = null;
        box.KeyDown += (_, e) => seen = e.KeyCode;
        Key(harness.Router, "B", isDown: true);
        Key(harness.Router, "B", isDown: false);

        Assert.Equal(ForkForms.Keys.B, seen);
    }

    [Fact]
    public void Focus_moving_to_wpf_reroutes_keys_away_from_guest()
    {
        var panel = new StackPanel();
        var wpfButton = new Button { Content = "WPF" };
        panel.AddChild(wpfButton);
        var bridge = new WfiHost
        {
            Child = new ForkForms.TextBox { Size = new WfSize(200, 24) },
        };
        panel.AddChild(bridge);

        var router = new WasmInputRouter();
        var host = new WasmWpfHost(new WasmDispatcherPump(), router);
        var window = new Window { Content = panel };
        host.AddWindow(window, new WpfSize(800, 600));

        var box = Assert.IsType<ForkForms.TextBox>(bridge.Child);
        Assert.True(host.Input.TryFocus(bridge));
        Assert.True(box.Focused);

        var guestKeys = 0;
        box.KeyDown += (_, _) => guestKeys++;
        var wpfKeys = 0;
        wpfButton.KeyDown += (_, _) => wpfKeys++;

        Assert.True(host.Input.TryFocus(wpfButton));
        Assert.Same(wpfButton, host.Input.Focused);

        Key(router, "B", isDown: true);
        Key(router, "B", isDown: false);

        Assert.Equal(0, guestKeys);
        Assert.Equal(1, wpfKeys);
    }

    [Fact]
    public void Dispose_mid_hosting_keeps_host_ticking_without_guest_pixels()
    {
        var harness = Create(new ForkForms.Panel
        {
            BackColor = WfColor.Red,
            Size = new WfSize(40, 30),
        });

        harness.Host.Tick(new FrameContext(2, 33.2));
        Assert.Contains(harness.Host.CurrentCommands, c => c is DrawCommand.BlitBitmap);

        harness.Bridge.Dispose();
        Assert.True(harness.Bridge.IsDisposed);

        // The host keeps ticking after guest teardown; the guest texture is
        // gone from the transport commands and routed input is silently dropped.
        harness.Host.Tick(new FrameContext(3, 50.0));
        Assert.DoesNotContain(harness.Host.CurrentCommands, c => c is DrawCommand.BlitBitmap);

        var (x, y) = (harness.Bridge.RenderRect.X + 1, harness.Bridge.RenderRect.Y + 1);
        Down(harness.Router, x, y);
        Up(harness.Router, x, y);
    }

    [Fact]
    public void Unload_disposes_bridge()
    {
        // Wiring half of the detach contract: the engine proves removal of a
        // loaded subtree raises Unloaded (FrameworkLifecycleTests); converted
        // windows arrive loaded via WpfAppBoot.Hydrate. Firing the event here
        // proves the bridge ends the guest lifetime when it fires.
        var harness = Create(new ForkForms.Panel { Size = new WfSize(40, 30) });
        Assert.False(harness.Bridge.IsDisposed);

        var field = typeof(FrameworkElement).GetField(
            "Unloaded", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        var handler = Assert.IsAssignableFrom<MulticastDelegate>(field.GetValue(harness.Bridge));
        Assert.NotNull(handler);
        handler.DynamicInvoke(harness.Bridge, new RoutedEventArgs { Source = harness.Bridge });

        Assert.True(harness.Bridge.IsDisposed);
    }

    [Fact]
    public void Unknown_key_through_host_stack_drops_without_guest_effect()
    {
        var harness = Create(new ForkForms.TextBox
        {
            Text = string.Empty,
            Size = new WfSize(200, 24),
        }, tick: false);
        var box = Assert.IsType<ForkForms.TextBox>(harness.Bridge.Child);
        Assert.True(harness.Host.Input.TryFocus(harness.Bridge));

        var guestKeys = 0;
        box.KeyDown += (_, _) => guestKeys++;

        // F24 is outside the guest key vocabulary: forwarded nowhere, and the
        // WPF side still observes the routed press+release (no swallowed input).
        var wpfDowns = 0;
        var wpfUps = 0;
        harness.Bridge.KeyDown += (_, _) => wpfDowns++;
        harness.Bridge.KeyUp += (_, _) => wpfUps++;
        Key(harness.Router, "F24", isDown: true);
        Key(harness.Router, "F24", isDown: false);

        Assert.Equal(0, guestKeys);
        Assert.Equal(1, wpfDowns);
        Assert.Equal(1, wpfUps);
    }
}
