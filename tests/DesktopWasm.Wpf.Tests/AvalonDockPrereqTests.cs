using System.Runtime.InteropServices;
using DesktopWasm.Core;

namespace DesktopWasm.Wpf.Tests;

/// <summary>Slice-60 AvalonDock prerequisites: ComponentResourceKey,
/// GeneratorStatus + ItemContainerGenerator.Status/StatusChanged, HwndHost,
/// ToolBar + OverflowMode, and the InputBinding/KeyBinding/
/// InputBindingCollection surfaces with key-dispatch.</summary>
[ResetAmbientState]
public sealed class AvalonDockPrereqTests
{
    private sealed class StubHost : HwndHost
    {
        protected override HandleRef BuildWindowCore(HandleRef hwndParent) => default;
        protected override void DestroyWindowCore(HandleRef hwnd)
        {
        }
    }

    [Fact]
    public void ComponentResourceKey_defaults_and_identity()
    {
        var empty = new ComponentResourceKey();
        Assert.Null(empty.TypeInTargetAssembly);
        Assert.Null(empty.ResourceId);
        Assert.Null(empty.Assembly);

        var key = new ComponentResourceKey(typeof(ToolBar), "ButtonStyleKey");
        Assert.Same(typeof(ToolBar), key.TypeInTargetAssembly);
        Assert.Equal("ButtonStyleKey", key.ResourceId);
        Assert.Same(typeof(ToolBar).Assembly, key.Assembly);
        Assert.IsAssignableFrom<ResourceKey>(key);

        var same = new ComponentResourceKey(typeof(ToolBar), "ButtonStyleKey");
        Assert.Equal(key, same);
        Assert.Equal(key.GetHashCode(), same.GetHashCode());
        Assert.NotEqual(key, new ComponentResourceKey(typeof(ToolBar), "MenuStyleKey"));
        Assert.NotEqual(key, new ComponentResourceKey(typeof(Button), "ButtonStyleKey"));
        Assert.False(key.Equals("ButtonStyleKey"));
        Assert.Contains("ToolBar", key.ToString());
        Assert.Contains("ButtonStyleKey", key.ToString());
    }

    [Fact]
    public void GeneratorStatus_values_match_wpf()
    {
        Assert.Equal(0, (int)GeneratorStatus.NotStarted);
        Assert.Equal(1, (int)GeneratorStatus.GeneratingContainers);
        Assert.Equal(2, (int)GeneratorStatus.ContainersGenerated);
        Assert.Equal(3, (int)GeneratorStatus.Error);
    }

    [Fact]
    public void ItemContainerGenerator_reports_real_generation_transitions()
    {
        var items = new ItemsControl();
        Assert.Equal(GeneratorStatus.NotStarted, items.ItemContainerGenerator.Status);
        var seen = new List<GeneratorStatus>();
        items.ItemContainerGenerator.StatusChanged += (_, _) => seen.Add(items.ItemContainerGenerator.Status);

        var container = items.ItemContainerGenerator.ContainerFromItem("a");
        Assert.NotNull(container);
        Assert.Equal(GeneratorStatus.ContainersGenerated, items.ItemContainerGenerator.Status);
        Assert.Equal(new[] { GeneratorStatus.GeneratingContainers, GeneratorStatus.ContainersGenerated }, seen);

        // Cache hits and nulls stay silent: no re-generation happened.
        seen.Clear();
        Assert.Same(container, items.ItemContainerGenerator.ContainerFromItem("a"));
        Assert.Null(items.ItemContainerGenerator.ContainerFromItem(null));
        Assert.Empty(seen);
        Assert.Equal(GeneratorStatus.ContainersGenerated, items.ItemContainerGenerator.Status);
    }

    [Fact]
    public void HwndHost_is_an_unrealized_handle_boundary()
    {
        var host = new StubHost();
        Assert.Equal(IntPtr.Zero, host.Handle);
        Assert.False(host.IsDisposed);

        // Retained hooks never fire from the host (no Win32 pump).
        var hooks = 0;
        host.MessageHook += (IntPtr _, int _, IntPtr _, IntPtr _, ref bool _) => { hooks++; return IntPtr.Zero; };
        Assert.Equal(0, hooks);

        var dpis = new List<string>();
        host.DpiChanged += (_, e) => dpis.Add(e.NewDpi.DpiScaleX + "x" + e.NewDpi.DpiScaleY);
        host.UpdateWindowPos(); // no-op without a window
        Assert.False(host.IsDisposed);

        host.Dispose();
        host.Dispose(); // idempotent
        Assert.True(host.IsDisposed);
        Assert.NotNull(HwndHost.DpiChangedEvent);
    }

    [Fact]
    public void ToolBar_defaults_and_style_keys()
    {
        var bar = new ToolBar();
        Assert.Equal(Orientation.Horizontal, bar.Orientation);
        Assert.Equal(0, bar.Band);
        Assert.Equal(0, bar.BandIndex);
        Assert.False(bar.IsOverflowOpen);
        Assert.False(bar.HasOverflowItems);
        Assert.Equal(OverflowMode.AsNeeded, ToolBar.GetOverflowMode(bar));
        Assert.False(ToolBar.GetIsOverflowItem(bar));

        bar.Band = 2;
        bar.BandIndex = 1;
        bar.IsOverflowOpen = true;
        Assert.Equal(2, bar.Band);
        Assert.Equal(1, bar.BandIndex);
        Assert.True(bar.IsOverflowOpen);

        ToolBar.SetOverflowMode(bar, OverflowMode.Always);
        Assert.Equal(OverflowMode.Always, ToolBar.GetOverflowMode(bar));
        ToolBar.SetIsOverflowItem(bar, true);
        Assert.True(ToolBar.GetIsOverflowItem(bar));

        // Every style key is a ComponentResourceKey owned by ToolBar.
        foreach (var key in new[]
        {
            ToolBar.ButtonStyleKey, ToolBar.ToggleButtonStyleKey, ToolBar.SeparatorStyleKey,
            ToolBar.CheckBoxStyleKey, ToolBar.RadioButtonStyleKey, ToolBar.ComboBoxStyleKey,
            ToolBar.TextBoxStyleKey, ToolBar.MenuStyleKey,
        })
        {
            var component = Assert.IsType<ComponentResourceKey>(key);
            Assert.Same(typeof(ToolBar), component.TypeInTargetAssembly);
        }
        Assert.Equal("ButtonStyleKey",
            ((ComponentResourceKey)ToolBar.ButtonStyleKey).ResourceId);
        Assert.NotSame(ToolBar.ButtonStyleKey, ToolBar.MenuStyleKey);
    }

    [Fact]
    public void OverflowMode_values_match_wpf()
    {
        Assert.Equal(0, (int)OverflowMode.AsNeeded);
        Assert.Equal(1, (int)OverflowMode.Always);
        Assert.Equal(2, (int)OverflowMode.Never);
    }

    [Fact]
    public void KeyBinding_syncs_gesture_and_parts_both_ways()
    {
        var command = new RelayCommand(_ => { });
        var binding = new KeyBinding(command, new KeyGesture(Key.N, ModifierKeys.Control));
        Assert.Same(command, binding.Command);
        Assert.Equal(Key.N, binding.Key);
        Assert.Equal(ModifierKeys.Control, binding.Modifiers);
        var gesture = Assert.IsType<KeyGesture>(binding.Gesture);
        Assert.Equal(Key.N, gesture.Key);

        binding.Key = Key.S;
        binding.Modifiers = ModifierKeys.Control | ModifierKeys.Shift;
        gesture = Assert.IsType<KeyGesture>(binding.Gesture);
        Assert.Equal(Key.S, gesture.Key);
        Assert.Equal(ModifierKeys.Control | ModifierKeys.Shift, gesture.Modifiers);

        binding.Gesture = new KeyGesture(Key.O, ModifierKeys.Control);
        Assert.Equal(Key.O, binding.Key);
        Assert.Equal(ModifierKeys.Control, binding.Modifiers);
    }

    [Fact]
    public void InputBindingCollection_typed_and_list_shapes_agree()
    {
        var bindings = new InputBindingCollection();
        Assert.Equal(0, bindings.Count);
        var save = new KeyBinding(new RelayCommand(_ => { }), Key.S, ModifierKeys.Control);
        var open = new KeyBinding(new RelayCommand(_ => { }), new KeyGesture(Key.O, ModifierKeys.Control));
        Assert.Equal(0, bindings.Add(save));
        Assert.Equal(1, bindings.Add(open));
        Assert.Same(save, bindings[0]);
        Assert.True(bindings.Contains(open));
        Assert.Equal(1, bindings.IndexOf(open));

        bindings.Remove(save);
        Assert.Single(bindings);
        Assert.DoesNotContain(save, bindings.Cast<InputBinding>());

        System.Collections.IList list = bindings;
        Assert.Single(list.Cast<InputBinding>());
        Assert.True(list.Contains(open));
        list.Remove(open);
        Assert.Equal(0, bindings.Count);

        bindings.Add(open);
        // Ambient modifiers live on the shared device (the production path syncs
        // them in TrackKeyboard before dispatch); establish them explicitly so this
        // unit test does not depend on execution order.
        var priorModifiers = Keyboard.PrimaryDevice.Modifiers;
        try
        {
            Keyboard.PrimaryDevice.Modifiers = ModifierKeys.Control;
            Assert.NotNull(bindings.FindMatch(bindings, new KeyEventArgs(Keyboard.PrimaryDevice, 0, Key.O, Key.None, true, false)));
            Assert.Null(bindings.FindMatch(bindings, new KeyEventArgs(Keyboard.PrimaryDevice, 0, Key.N, Key.None, true, false)));
        }
        finally
        {
            Keyboard.PrimaryDevice.Modifiers = priorModifiers;
        }
    }

    private static readonly WasmWindowHandle Handle = new(1);

    private static (WasmInputRouter Router, WpfInputManager Manager, Window Root, Button Button) KeyHarness()
    {
        var router = new WasmInputRouter();
        var manager = new WpfInputManager(router);
        var root = new Window { Width = 400, Height = 300 };
        var button = new Button { Content = "Go", Width = 100, Height = 30 };
        root.Content = button;
        manager.AddRoot(root);
        var mil = new WasmMilCore(new DesktopWasm.Graphics.SkiaSurface(400, 300));
        mil.Render(root, new WpfSize(400, 300), new WpfRect(0, 0, 400, 300));
        Assert.True(manager.TryFocus(button));
        return (router, manager, root, button);
    }

    [Fact]
    public void Ctrl_n_binding_executes_and_marks_the_key_handled()
    {
        var (router, _, _, button) = KeyHarness();
        var calls = new List<object?>();
        var keydowns = 0;
        button.KeyDown += (_, _) => keydowns++;
        button.InputBindings.Add(new KeyBinding(
            new RelayCommand(p => calls.Add(p), _ => true),
            new KeyGesture(Key.N, ModifierKeys.Control)) { CommandParameter = "new-doc" });

        router.DispatchKeyboard(new WasmKeyArgs(Handle, "n", true, true, false, false));
        Assert.Equal(["new-doc"], calls);
        Assert.Equal(1, keydowns); // bubble ran first; the binding consumed it after

        calls.Clear();
        router.DispatchKeyboard(new WasmKeyArgs(Handle, "m", true, true, false, false));
        Assert.Empty(calls);
    }

    [Fact]
    public void Binding_without_can_execute_stays_silent_and_unhandled()
    {
        var (router, _, _, button) = KeyHarness();
        var calls = 0;
        button.InputBindings.Add(new KeyBinding(
            new RelayCommand(_ => calls++, _ => false),
            new KeyGesture(Key.S, ModifierKeys.Control)));

        router.DispatchKeyboard(new WasmKeyArgs(Handle, "s", true, true, false, false));
        Assert.Equal(0, calls);
    }

    [Fact]
    public void Ancestor_binding_covers_focused_child_like_wpf()
    {
        var router = new WasmInputRouter();
        var manager = new WpfInputManager(router);
        var root = new Window { Width = 400, Height = 300 };
        var panel = new StackPanel();
        var inner = new Button { Content = "Inner", Width = 80, Height = 24 };
        panel.AddChild(inner);
        root.Content = panel;
        manager.AddRoot(root);
        var mil = new WasmMilCore(new DesktopWasm.Graphics.SkiaSurface(400, 300));
        mil.Render(root, new WpfSize(400, 300), new WpfRect(0, 0, 400, 300));
        Assert.True(manager.TryFocus(inner));

        var calls = 0;
        panel.InputBindings.Add(new KeyBinding(
            new RelayCommand(_ => calls++), new KeyGesture(Key.P, ModifierKeys.Control)));
        router.DispatchKeyboard(new WasmKeyArgs(Handle, "p", true, true, false, false));
        Assert.Equal(1, calls);
    }
}
