using DesktopWasm.Core;
using DesktopWasm.Graphics;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Toggle controls (ToggleButton/CheckBox/RadioButton), navigation hosts
/// (Page/Frame) and Visibility: the control subset that closes the
/// convertible-IL gaps for sample apps driven by option buttons and
/// page navigation (Geometry SampleViewer). Media3D, Storyboard and
/// templates stay precise boundaries.
/// </summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class ToggleNavigationTests : IDisposable
{
    private readonly Application? _previous = Application.Current;

    public void Dispose() => Application.Current = _previous;

    /// <summary>Stand-in for a converted Page codebehind (Geometry-style wiring).</summary>
    public sealed class NavProbePage : Page
    {
        internal RadioButton? First;
        internal RadioButton? Second;
        internal RadioButton? Other;
        internal CheckBox? Box;
        internal ToggleButton? Toggle;
        internal Frame? Host;

        public int LoadedCount;
        public int CheckedCount;
        public int RenderedCount;

        public NavProbePage() =>
            Application.LoadComponent(this, new Uri("Boot/NavPage.xaml", UriKind.Relative));

        private void OnPageLoaded(object sender, RoutedEventArgs e) => LoadedCount++;

        private void OnSampleChecked(object sender, RoutedEventArgs e) => CheckedCount++;

        private void OnFrameRendered(object sender, EventArgs e) => RenderedCount++;
    }

    private static NavProbePage BootPage()
    {
        Application.Current = new BootProbe.ProbeApp();
        return new NavProbePage();
    }

    private sealed class PressHarness
    {
        public WasmInputRouter Router { get; } = new();
        public WpfInputManager Manager { get; }
        public StackPanel Root { get; } = new();

        public PressHarness(params UIElement[] children)
        {
            Manager = new WpfInputManager(Router);
            foreach (var child in children) Root.AddChild(child);
            Manager.AddRoot(Root);
            Root.Measure(new WpfSize(800, 600));
            Root.Arrange(new WpfRect(0, 0, 800, 600));
        }

        public void Press(UIElement target)
        {
            var rect = target.ArrangedRect;
            var handle = new WasmWindowHandle(1);
            Router.DispatchPointer(new WasmPointerArgs(handle, (float)(rect.X + rect.Width / 2), (float)(rect.Y + rect.Height / 2), WasmPointerButton.Left, true)
                { Kind = WasmPointerKind.Down, PointerId = 1 });
            Router.DispatchPointer(new WasmPointerArgs(handle, (float)(rect.X + rect.Width / 2), (float)(rect.Y + rect.Height / 2), WasmPointerButton.Left, false)
                { Kind = WasmPointerKind.Up, PointerId = 1 });
        }
    }

    [Fact]
    public void Page_hydrates_title_names_attributes_and_loaded()
    {
        var page = BootPage();
        Assert.Equal("Geo", page.WindowTitle);
        Assert.NotNull(page.First);
        var first = page.First!;
        Assert.NotNull(page.Second);
        Assert.NotNull(page.Other);
        Assert.NotNull(page.Box);
        var box = page.Box!;
        Assert.NotNull(page.Toggle);
        var toggle = page.Toggle!;
        Assert.NotNull(page.Host);
        var host = page.Host!;
        Assert.Equal("Samples", first.GroupName);
        Assert.True(box.IsChecked);
        Assert.Equal(NavigationUIVisibility.Hidden, host.NavigationUIVisibility);
        Assert.Equal(Visibility.Hidden, toggle.Visibility);
        Assert.Equal(1, page.LoadedCount);
    }

    [Fact]
    public void Radio_group_checks_exclusively_per_group()
    {
        var page = BootPage();
        Assert.NotNull(page.First);
        var first = page.First!;
        Assert.NotNull(page.Second);
        var second = page.Second!;
        Assert.NotNull(page.Other);
        var other = page.Other!;
        var harness = new PressHarness(first, second, other);
        var clicks = 0;
        second.Click += (_, _) => clicks++;
        harness.Press(first);
        Assert.True(first.IsChecked);
        Assert.Equal(1, page.CheckedCount);
        harness.Press(second);
        Assert.True(second.IsChecked);
        Assert.False(first.IsChecked);
        Assert.Equal(1, clicks);
        harness.Press(other);
        Assert.True(other.IsChecked);
        Assert.True(second.IsChecked);
    }

    [Fact]
    public void Pressing_checked_radio_keeps_it_checked_but_clicks()
    {
        var page = BootPage();
        Assert.NotNull(page.First);
        var first = page.First!;
        var harness = new PressHarness(first);
        var clicks = 0;
        first.Click += (_, _) => clicks++;
        harness.Press(first);
        harness.Press(first);
        Assert.True(first.IsChecked);
        Assert.Equal(2, clicks);
        Assert.Equal(1, page.CheckedCount);
    }

    [Fact]
    public void Checkbox_starts_unchecked_and_press_toggles_then_commands()
    {
        // Gallery ButtonPage shape: an unset checkbox starts unchecked (not
        // indeterminate), and a real press flips it and runs its command
        // (the "Disable button" sample disables its button).
        Assert.False(new CheckBox().IsChecked);
        Assert.False(new ToggleButton().IsChecked);
        var commanded = 0;
        var box = new CheckBox
        {
            Content = "Disable",
            Command = new RelayCommand(_ => commanded++),
        };
        var harness = new PressHarness(box);
        Assert.False(box.IsChecked);
        harness.Press(box);
        Assert.True(box.IsChecked);
        Assert.Equal(1, commanded);
    }

    [Fact]
    public void Checkbox_three_state_cycles_and_two_state_skips_null()
    {
        var two = new CheckBox { IsChecked = true };
        var three = new CheckBox { IsChecked = true, IsThreeState = true };
        var harness = new PressHarness(two, three);
        var nulls = 0;
        three.Indeterminate += (_, _) => nulls++;
        harness.Press(two);
        Assert.False(two.IsChecked);
        harness.Press(two);
        Assert.True(two.IsChecked);
        harness.Press(three);
        Assert.Null(three.IsChecked);
        Assert.Equal(1, nulls);
        harness.Press(three);
        Assert.False(three.IsChecked);
        harness.Press(three);
        Assert.True(three.IsChecked);
    }

    [Fact]
    public void Hidden_elements_skip_hit_test_and_paint()
    {
        var page = BootPage();
        Assert.NotNull(page.Toggle);
        var toggle = page.Toggle!;
        var panel = Assert.IsType<StackPanel>(page.Content);
        panel.Measure(new WpfSize(800, 600));
        panel.Arrange(new WpfRect(0, 0, 800, 600));
        var rect = toggle.ArrangedRect;
        var center = new WpfPoint(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
        Assert.False(toggle.HitTest(center));
        var commands = WasmMilCore.RenderHeadless(panel, new WpfSize(800, 600));
        Assert.DoesNotContain(commands, c => c is DrawCommand.DrawText text && text.Text == "Unseen");
        toggle.Visibility = Visibility.Visible;
        Assert.True(toggle.HitTest(center));
        commands = WasmMilCore.RenderHeadless(panel, new WpfSize(800, 600));
        Assert.Contains(commands, c => c is DrawCommand.DrawText text && text.Text == "Unseen");
    }

    [Fact]
    public void Collapsed_behaves_like_hidden_in_v1()
    {
        var button = new Button { Content = "Gone" };
        var panel = new StackPanel();
        panel.AddChild(button);
        panel.Measure(new WpfSize(800, 600));
        panel.Arrange(new WpfRect(0, 0, 800, 600));
        button.Visibility = Visibility.Collapsed;
        Assert.False(button.HitTest(new WpfPoint(
            button.ArrangedRect.X + 1, button.ArrangedRect.Y + 1)));
        Assert.DoesNotContain(WasmMilCore.RenderHeadless(panel, new WpfSize(800, 600)),
            c => c is DrawCommand.DrawText text && text.Text == "Gone");
    }

    [Fact]
    public void Frame_navigate_hosts_page_and_reports_rendered()
    {
        var page = BootPage();
        Assert.NotNull(page.Host);
        var host = page.Host!;
        var inner = new Page { WindowTitle = "Inner" };
        inner.Content = new TextBlock { Text = "Inside" };
        host.Navigate(inner);
        Assert.Same(inner, host.Content);
        Assert.Equal(1, page.RenderedCount);
        var commands = WasmMilCore.RenderHeadless(host, new WpfSize(800, 600));
        Assert.Contains(commands, c => c is DrawCommand.DrawText text && text.Text == "Inside");
    }

    [Fact]
    public void Frame_navigate_rejects_non_elements_and_uris()
    {
        var frame = new Frame();
        Assert.Throws<InvalidOperationException>(() => frame.Navigate("nope"));
        Assert.Throws<InvalidOperationException>(() => frame.Navigate(new Uri("page.xaml", UriKind.Relative)));
    }

    [Fact]
    public void Loader_rejects_bad_visibility_and_navigation_values()
    {
        var ex = Assert.Throws<XamlLoadException>(() =>
            XamlLoader.Load("<Button Visibility=\"Gone\" xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" />"));
        Assert.Contains("Visibility", ex.Message);
        ex = Assert.Throws<XamlLoadException>(() =>
            XamlLoader.Load("<Frame NavigationUIVisibility=\"Sometimes\" xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" />"));
        Assert.Contains("NavigationUIVisibility", ex.Message);
    }

    [Fact]
    public void Loader_parses_toggle_navigation_attributes()
    {
        const string ns = "xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"";
        var result = XamlLoader.Load(
            $"<StackPanel {ns}><ToggleButton IsChecked=\"True\">T</ToggleButton>" +
            "<CheckBox IsThreeState=\"True\">C</CheckBox>" +
            "<RadioButton GroupName=\"G\">R</RadioButton>" +
            "<Page WindowTitle=\"P\"><TextBlock>Hi</TextBlock></Page>" +
            "<Frame NavigationUIVisibility=\"Hidden\" /></StackPanel>");
        var panel = Assert.IsType<StackPanel>(result.Root);
        var toggle = Assert.IsType<ToggleButton>(panel.LogicalChildren[0]);
        Assert.True(toggle.IsChecked);
        Assert.Equal("T", toggle.Content);
        var check = Assert.IsType<CheckBox>(panel.LogicalChildren[1]);
        Assert.True(check.IsThreeState);
        var radio = Assert.IsType<RadioButton>(panel.LogicalChildren[2]);
        Assert.Equal("G", radio.GroupName);
        var page = Assert.IsType<Page>(panel.LogicalChildren[3]);
        Assert.Equal("P", page.WindowTitle);
        var frame = Assert.IsType<Frame>(panel.LogicalChildren[4]);
        Assert.Equal(NavigationUIVisibility.Hidden, frame.NavigationUIVisibility);
    }
}
