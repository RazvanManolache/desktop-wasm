using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using DesktopWasm.Wpf;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Slice-64 UIElement focus cluster: the converted AvalonDock closure failed
/// to JIT eight static constructors (DockingManager plus seven pane/pane-control
/// types) on MissingFieldException for UIElement.FocusableProperty. The engine
/// now declares the WPF-parity focus surface (FocusableProperty with the
/// Control true-default override, IsKeyboardFocusedChanged,
/// PreviewGot/PreviewLostKeyboardFocus, Control.IsTabStopProperty AddOwner'd
/// from KeyboardNavigation, and KeyboardNavigation TabNavigation/IsTabStop
/// attached DPs with Get/Set), all shapes live-verified against the 9.0.21
/// WindowsDesktop runtime. Staged oracles load the live staged AvalonDock bins
/// (sha256-verified, status gate bypassed in-probe only, manifests untouched).
/// Out of scope on purpose: the remaining UIElement audit rows (IsVisible/
/// RenderSize/mouse-capture/right-button/InputHitTest/IsHitTestVisible), the
/// Window chrome/events cluster, and the HwndSource trio the DockingManager
/// .cctor reaches next (pinned as the precise next blocker).
/// </summary>
[ResetAmbientState]
public sealed class Slice64FocusTests
{
    private const string PresentationNs = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    [Fact]
    public void Staged_avalondock_hashes_verify()
    {
        Assert.Empty(StagedAvalonDockFocusLoader.VerifyHashes());
    }

    [Fact]
    public void FocusableProperty_shape_matches_wpf()
    {
        Assert.Same(typeof(UIElement), UIElement.FocusableProperty.OwnerType);
        Assert.Equal(typeof(bool), UIElement.FocusableProperty.PropertyType);
        Assert.Equal(false, UIElement.FocusableProperty.DefaultValue);
        Assert.Same(UIElement.FocusableProperty, DependencyProperty.FromName(typeof(Button), "Focusable"));
        Assert.Same(UIElement.FocusableProperty, DependencyProperty.FromName(typeof(Border), "Focusable"));
    }

    [Fact]
    public void Control_metadata_keeps_the_true_default()
    {
        Assert.True(new Button().Focusable);
        Assert.True(new TextBox().Focusable);
        Assert.False(new Border().Focusable);
    }

    [Fact]
    public void Focusable_store_round_trips_through_clr_and_dp()
    {
        var button = new Button();
        button.Focusable = false;
        Assert.False(button.Focusable);
        Assert.Equal(false, button.GetValue(UIElement.FocusableProperty));
        button.SetValue(UIElement.FocusableProperty, true);
        Assert.True(button.Focusable);
        button.ClearValue(UIElement.FocusableProperty);
        Assert.True(button.Focusable);

        var plain = new Border();
        Assert.False(plain.Focusable);
        plain.SetValue(UIElement.FocusableProperty, true);
        Assert.True(plain.Focusable);
    }

    [Fact]
    public void Control_derived_defaults_stay_true()
    {
        Assert.True(new ListBox().Focusable);
        Assert.True(new TreeView().Focusable);
        Assert.True(new Thumb().Focusable);
        Assert.True(new TabControl().Focusable);
        Assert.True(new ToggleButton().Focusable);
        Assert.True(new PasswordBox().Focusable);
        Assert.True(new GridSplitter().Focusable);
    }

    [Fact]
    public void Separator_opts_out_of_the_control_default()
    {
        // Menu rows never take keyboard focus in the engine model: a
        // focusable separator would steal menu focus on press and drop the
        // open submenu (Menu_separators_take_layout_keyboard_skips_and_press_ignores).
        var separator = new Separator();
        Assert.False(separator.Focusable);
        Assert.Equal(false, separator.GetValue(UIElement.FocusableProperty));
        separator.SetValue(UIElement.FocusableProperty, true);
        Assert.True(separator.Focusable);
        separator.ClearValue(UIElement.FocusableProperty);
        Assert.False(separator.Focusable);
    }

    [Fact]
    public void Display_types_opt_out_of_the_control_default()
    {
        // Live-verified WPF defaults: display-only controls never take
        // keyboard focus, so each opts out of the Control true default.
        Assert.False(new Label().Focusable);
        Assert.False(new GroupBox().Focusable);
        Assert.False(new ToolBar().Focusable);
        Assert.False(new ProgressBar().Focusable);
        Assert.False(new UserControl().Focusable);
        Assert.False(new DataGridColumnHeader().Focusable);
        Assert.False(new GroupItem().Focusable);
    }

    [Fact]
    public void Expander_stays_focusable_but_leaves_the_tab_order()
    {
        // Live-verified WPF shape: Expander.Focusable is true but
        // IsTabStop is false, so Tab walks past group headers while
        // programmatic focus still lands.
        var expander = new Expander { Header = "Group" };
        Assert.True(expander.Focusable);
        Assert.False(expander.IsTabStop);
        Assert.False(KeyboardNavigation.GetIsTabStop(expander));
    }

    [Fact]
    public void Tab_walks_tab_stops_and_skips_headers()
    {
        var router = new DesktopWasm.Core.WasmInputRouter();
        var manager = new WpfInputManager(router);
        var root = new Window { Width = 400, Height = 300 };
        var panel = new StackPanel();
        var first = new Button { Content = "First", Width = 100, Height = 30 };
        var header = new Expander { Header = "Group", Width = 100, Height = 30 };
        var second = new Button { Content = "Second", Width = 100, Height = 30 };
        panel.AddChild(first);
        panel.AddChild(header);
        panel.AddChild(second);
        root.Content = panel;
        manager.AddRoot(root);
        var mil = new WasmMilCore(new DesktopWasm.Graphics.SkiaSurface(400, 300));
        mil.Render(root, new WpfSize(400, 300), new WpfRect(0, 0, 400, 300));
        Assert.True(manager.TryFocus(first));

        var handle = new DesktopWasm.Core.WasmWindowHandle(1);
        router.DispatchKeyboard(new DesktopWasm.Core.WasmKeyArgs(handle, "Tab", true, false, false, false));
        Assert.Same(second, manager.Focused);

        // Programmatic focus still lands on the non-tab-stop header.
        Assert.True(manager.TryFocus(header));
        Assert.Same(header, manager.Focused);
    }

    [Fact]
    public void Non_control_keep_sites_hold_their_defaults()
    {
        // Popup/ScrollViewer/DataGrid/Menu keep their engine-true CLR
        // overrides (FrameworkElement lineage, not Control); ToolTip keeps
        // false. Only the base and Control-derived overrides are
        // store-backed, so SetValue on these types stays out of scope.
        Assert.True(new Popup().Focusable);
        Assert.True(new ScrollViewer().Focusable);
        Assert.True(new DataGrid().Focusable);
        Assert.True(new Menu().Focusable);
        Assert.False(new ToolTip().Focusable);
    }

    [Fact]
    public void Trigger_dp_setter_applies_focusable()
    {
        var off = new Button();
        var style = new Style(typeof(Button));
        var trigger = new Trigger { Value = false };
        trigger.set_Property(UIElement.IsMouseOverProperty);
        trigger.Setters.Add(new Setter(UIElement.FocusableProperty, false));
        style.Triggers.Add(trigger);
        style.ApplyTo(off);
        Assert.False(off.Focusable);
        Assert.Equal(false, off.GetValue(UIElement.FocusableProperty));

        var on = new Button { Focusable = false };
        var restore = new Style(typeof(Button));
        var retrigger = new Trigger { Value = false };
        retrigger.set_Property(UIElement.IsMouseOverProperty);
        retrigger.Setters.Add(new Setter(UIElement.FocusableProperty, true));
        restore.Triggers.Add(retrigger);
        restore.ApplyTo(on);
        Assert.True(on.Focusable);
    }

    private static (WpfInputManager Manager, Button First, Button Second) FocusHarness()
    {
        var router = new DesktopWasm.Core.WasmInputRouter();
        var manager = new WpfInputManager(router);
        var root = new Window { Width = 400, Height = 300 };
        var panel = new StackPanel();
        var first = new Button { Content = "First", Width = 100, Height = 30 };
        var second = new Button { Content = "Second", Width = 100, Height = 30 };
        panel.AddChild(first);
        panel.AddChild(second);
        root.Content = panel;
        manager.AddRoot(root);
        var mil = new WasmMilCore(new DesktopWasm.Graphics.SkiaSurface(400, 300));
        mil.Render(root, new WpfSize(400, 300), new WpfRect(0, 0, 400, 300));
        return (manager, first, second);
    }

    [Fact]
    public void Focus_move_raises_previews_and_focused_changed_in_order()
    {
        var (manager, first, second) = FocusHarness();
        var order = new List<string>();
        first.PreviewGotKeyboardFocus += (_, _) => order.Add("first.PreviewGot");
        first.PreviewLostKeyboardFocus += (_, _) => order.Add("first.PreviewLost");
        first.IsKeyboardFocusedChanged += (_, e) =>
            order.Add($"first.Changed:{e.OldValue}->{e.NewValue}:{e.Property == UIElement.IsKeyboardFocusedProperty}");
        second.PreviewGotKeyboardFocus += (_, _) => order.Add("second.PreviewGot");
        second.IsKeyboardFocusedChanged += (_, e) =>
            order.Add($"second.Changed:{e.OldValue}->{e.NewValue}:{e.Property == UIElement.IsKeyboardFocusedProperty}");

        Assert.True(manager.TryFocus(first));
        Assert.Same(first, manager.Focused);
        Assert.True(manager.TryFocus(second));
        Assert.Same(second, manager.Focused);

        // Both previews fire pre-move (either Handled vetoes); then the old
        // holder releases (lost events plus the true->false flip) before the
        // new holder acquires (got events plus the false->true flip).
        Assert.Equal(
            [
                "first.PreviewGot",
                "first.Changed:False->True:True",
                "first.PreviewLost",
                "second.PreviewGot",
                "first.Changed:True->False:True",
                "second.Changed:False->True:True",
            ],
            order);
    }

    [Fact]
    public void Preview_veto_on_either_side_keeps_old_focus()
    {
        var (manager, first, second) = FocusHarness();
        var changed = 0;
        first.IsKeyboardFocusedChanged += (_, _) => changed++;
        second.IsKeyboardFocusedChanged += (_, _) => changed++;
        Assert.True(manager.TryFocus(first));

        second.PreviewGotKeyboardFocus += (s, e) => e.Handled = true;
        Assert.False(manager.TryFocus(second));
        Assert.Same(first, manager.Focused);

        second.PreviewGotKeyboardFocus += (s, e) => e.Handled = false;
        first.PreviewLostKeyboardFocus += (s, e) => e.Handled = true;
        Assert.False(manager.TryFocus(second));
        Assert.Same(first, manager.Focused);
        Assert.Equal(1, changed);
    }

    [Fact]
    public void Istabstop_property_shares_the_keyboardnavigation_instance()
    {
        Assert.Same(KeyboardNavigation.IsTabStopProperty, Control.IsTabStopProperty);
        Assert.Equal(typeof(KeyboardNavigation), Control.IsTabStopProperty.OwnerType);
        Assert.Same(Control.IsTabStopProperty, DependencyProperty.FromName(typeof(Button), "IsTabStop"));

        var button = new Button();
        Assert.True(button.IsTabStop);
        Assert.True(KeyboardNavigation.GetIsTabStop(button));
        button.IsTabStop = false;
        Assert.False(KeyboardNavigation.GetIsTabStop(button));
        Assert.Equal(false, button.GetValue(Control.IsTabStopProperty));
        KeyboardNavigation.SetIsTabStop(button, true);
        Assert.True(button.IsTabStop);

        var plain = new Border();
        Assert.True(KeyboardNavigation.GetIsTabStop(plain));
        KeyboardNavigation.SetIsTabStop(plain, false);
        Assert.False(KeyboardNavigation.GetIsTabStop(plain));
    }

    [Fact]
    public void Tabnavigation_defaults_continue_with_ancestor_inherit()
    {
        var panel = new StackPanel();
        var child = new Button();
        panel.AddChild(child);
        Assert.Equal(KeyboardNavigationMode.Continue, KeyboardNavigation.GetTabNavigation(child));
        KeyboardNavigation.SetTabNavigation(panel, KeyboardNavigationMode.Cycle);
        Assert.Equal(KeyboardNavigationMode.Cycle, KeyboardNavigation.GetTabNavigation(panel));
        Assert.Equal(KeyboardNavigationMode.Cycle, KeyboardNavigation.GetTabNavigation(child));
        child.SetValue(KeyboardNavigation.TabNavigationProperty, KeyboardNavigationMode.Once);
        Assert.Equal(KeyboardNavigationMode.Once, KeyboardNavigation.GetTabNavigation(child));
    }

    [Fact]
    public void Keyboardnavigation_audit_shapes_resolve_by_arity()
    {
        Assert.NotNull(typeof(KeyboardNavigation).GetMethod(
            "SetTabNavigation", [typeof(DependencyObject), typeof(KeyboardNavigationMode)]));
        Assert.NotNull(typeof(KeyboardNavigation).GetMethod(
            "GetTabNavigation", [typeof(DependencyObject)]));
        Assert.NotNull(typeof(UIElement).GetField(
            "FocusableProperty", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy));
        Assert.NotNull(typeof(Control).GetField(
            "IsTabStopProperty", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy));
        Assert.NotNull(typeof(UIElement).GetMethod(
            "add_IsKeyboardFocusedChanged", BindingFlags.Public | BindingFlags.Instance));
        Assert.NotNull(typeof(UIElement).GetMethod(
            "add_PreviewGotKeyboardFocus", BindingFlags.Public | BindingFlags.Instance));
        Assert.NotNull(typeof(UIElement).GetMethod(
            "remove_PreviewGotKeyboardFocus", BindingFlags.Public | BindingFlags.Instance));
    }

    [Fact]
    public void Xaml_focus_knobs_land_in_clr_and_dp()
    {
        var window = Assert.IsType<Window>(XamlLoader.Load(
            $"<Window xmlns=\"{PresentationNs}\">" +
            "<Button Focusable=\"False\" KeyboardNavigation.IsTabStop=\"False\" " +
            "KeyboardNavigation.TabNavigation=\"Cycle\">go</Button>" +
            "</Window>").Root);
        var button = Assert.IsType<Button>(window.Content);
        Assert.False(button.Focusable);
        Assert.Equal(false, button.GetValue(UIElement.FocusableProperty));
        Assert.False(button.IsTabStop);
        Assert.False(KeyboardNavigation.GetIsTabStop(button));
        Assert.Equal(KeyboardNavigationMode.Cycle, KeyboardNavigation.GetTabNavigation(button));
    }

    [Fact]
    public void Converted_dockingmanager_advances_past_the_focus_cluster()
    {
        // Slice-65 declares the HwndSource trio plus the Window chrome/events
        // cluster, so the converted DockingManager .cctor and instance .ctor
        // both run: construction (not the next MissingMethod) is the advance.
        var assembly = StagedAvalonDockFocusLoader.LoadAssembly("AvalonDock");
        var manager = assembly.GetType("AvalonDock.DockingManager", throwOnError: true)!;
        var instance = Activator.CreateInstance(manager)!;
        Assert.IsAssignableFrom<FrameworkElement>(instance);
    }

    [Fact]
    public void Converted_focusable_users_clear_the_missing_field()
    {
        var assembly = StagedAvalonDockFocusLoader.LoadAssembly("AvalonDock");
        var users = new[]
        {
            "AvalonDock.DockingManager",
            "AvalonDock.Controls.AnchorablePaneTitle",
            "AvalonDock.Controls.DropDownControlArea",
            "AvalonDock.Controls.LayoutAnchorableControl",
            "AvalonDock.Controls.LayoutAnchorablePaneControl",
            "AvalonDock.Controls.LayoutAutoHideWindowControl",
            "AvalonDock.Controls.LayoutDocumentControl",
            "AvalonDock.Controls.LayoutDocumentPaneControl",
        };
        foreach (var name in users)
        {
            var type = assembly.GetType(name, throwOnError: true)!;
            try
            {
                System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(type.TypeHandle);
            }
            catch (Exception ex)
            {
                var chain = new List<string>();
                for (var e = ex; e is not null; e = e.InnerException)
                    chain.Add(e.GetType().Name + ": " + e.Message.Split('\n')[0]);
                Assert.DoesNotContain("FocusableProperty", string.Join(" ||| ", chain), StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void Converted_dropdownbutton_focusable_store_round_trip()
    {
        var assembly = StagedAvalonDockFocusLoader.LoadAssembly("AvalonDock");
        var dropType = assembly.GetType("AvalonDock.Controls.DropDownButton", throwOnError: true)!;
        var drop = (FrameworkElement)Activator.CreateInstance(dropType)!;
        Assert.True(drop.Focusable);
        drop.SetValue(UIElement.FocusableProperty, false);
        Assert.False(drop.Focusable);
        drop.Focusable = true;
        Assert.Equal(true, drop.GetValue(UIElement.FocusableProperty));
    }

    private static class StagedAvalonDockFocusLoader
    {
        private static readonly object Gate = new();
        private static readonly Dictionary<string, Assembly> Loaded = new(StringComparer.OrdinalIgnoreCase);

        public static List<string> VerifyHashes()
        {
            var failures = new List<string>();
            foreach (var (_, file, bytes, expected) in ReadStaged())
            {
                var actual = Convert.ToHexString(SHA256.HashData(bytes));
                if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                    failures.Add($"hash mismatch in staged '{file}'");
            }
            return failures;
        }

        public static Assembly LoadAssembly(string name)
        {
            lock (Gate)
            {
                if (Loaded.TryGetValue(name, out var hit)) return hit;
                var images = ReadStaged().ToDictionary(e => e.name, e => e.bytes, StringComparer.OrdinalIgnoreCase);
                Assert.True(images.ContainsKey(name), $"staged image missing: {name}");
                AppDomain.CurrentDomain.AssemblyResolve += (_, args) =>
                {
                    var requested = new AssemblyName(args.Name).Name;
                    var loaded = AppDomain.CurrentDomain.GetAssemblies()
                        .FirstOrDefault(a => string.Equals(a.GetName().Name, requested, StringComparison.OrdinalIgnoreCase));
                    if (loaded is not null) return loaded;
                    if (requested is not null && images.TryGetValue(requested, out var image))
                        return Assembly.Load(image);
                    return null;
                };
                foreach (var image in images.Values)
                {
                    var assembly = Assembly.Load(image);
                    Loaded[assembly.GetName().Name!] = assembly;
                }
                return Loaded[name];
            }
        }

        private static List<(string name, string file, byte[] bytes, string expected)> ReadStaged()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(System.IO.Path.Combine(directory.FullName, "src",
                "DesktopWasm.BrowserHost", "wwwroot", "apps", "avalondock", "app-manifest.json")))
                directory = directory.Parent;
            Assert.NotNull(directory);
            var apps = System.IO.Path.Combine(directory!.FullName, "src",
                "DesktopWasm.BrowserHost", "wwwroot", "apps", "avalondock");
            using var document = JsonDocument.Parse(File.ReadAllText(System.IO.Path.Combine(apps, "app-manifest.json")));
            var entries = new List<(string, string, byte[], string)>();
            foreach (var assembly in document.RootElement.GetProperty("assemblies").EnumerateArray())
            {
                var file = assembly.GetProperty("file").GetString()!;
                var expected = assembly.GetProperty("sha256").GetString()!;
                var bytes = File.ReadAllBytes(System.IO.Path.Combine(apps, file));
                entries.Add((assembly.GetProperty("name").GetString()!, file, bytes, expected));
            }
            return entries;
        }
    }
}
