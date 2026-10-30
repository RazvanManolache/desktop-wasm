using DesktopWasm.Wpf;

namespace DesktopWasm.Wpf.Tests;

/// <summary>Calculator-driven engine surface: routed Click, checkable menus,
/// retained hints, MessageBox presenting, multiline display, focus preview.</summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class CalcSurfaceTests : IDisposable
{
    private readonly Application? _previous = Application.Current;
    private readonly Func<Window, WpfSize, MessageBoxResult>? _previousPresenter = MessageBox.Presenter;

    public void Dispose()
    {
        Application.Current = _previous;
        MessageBox.Presenter = _previousPresenter;
        XamlLoader.CustomElementSource = null;
    }

    [Fact]
    public void ButtonIsButtonBaseIsContentControl()
    {
        var button = new Button();
        Assert.IsAssignableFrom<ButtonBase>(button);
        Assert.IsAssignableFrom<ContentControl>(button);
        Assert.IsAssignableFrom<FrameworkElement>(button);
    }

    [Fact]
    public void ClickCarriesSenderAndArgs()
    {
        var button = new Button();
        object? sender = null;
        RoutedEventArgs? args = null;
        button.Click += (s, e) => { sender = s; args = e; };
        button.RaiseClick();
        Assert.Same(button, sender);
        Assert.NotNull(args);
        Assert.Same(button, args.Source);
    }

    [Fact]
    public void CheckableMenuItemTogglesOnInvoke()
    {
        var item = new MenuItem { IsCheckable = true, IsChecked = true };
        var fired = 0;
        item.Click += (_, _) => fired++;
        item.RaiseClick();
        Assert.False(item.IsChecked);
        Assert.Equal(1, fired);
        item.RaiseClick();
        Assert.True(item.IsChecked);
    }

    [Fact]
    public void ToolTipRetained()
    {
        var button = new Button();
        var result = XamlLoader.Load(
            "<Button ToolTip=\"Clear All\" xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"/>");
        Assert.Equal("Clear All", ((Button)result.Root).ToolTip);
        Assert.Null(button.ToolTip);
    }

    [Fact]
    public void ShowGridLinesParsesBothWays()
    {
        // Slice-34: ShowGridLines=True now parses onto Grid.ShowGridLines
        // and paints the dotted boundary adornment (GridPage's 3x3 example);
        // it is no longer a loader rejection.
        const string ns = "xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"";
        var off = XamlLoader.Load($"<Grid ShowGridLines=\"False\" {ns}/>");
        Assert.False(Assert.IsType<Grid>(off.Root).ShowGridLines);
        var on = XamlLoader.Load($"<Grid ShowGridLines=\"True\" {ns}/>");
        Assert.True(Assert.IsType<Grid>(on.Root).ShowGridLines);
    }

    [Fact]
    public void ClickAttributeRecordsWire()
    {
        const string ns = "xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"";
        var result = XamlLoader.Load($"<Button Click=\"OnPress\" {ns}/>");
        var wire = Assert.Single(result.EventWires);
        Assert.Same(result.Root, wire.Element);
        Assert.Equal("Click", wire.EventName);
        Assert.Equal("OnPress", wire.HandlerName);
    }

    [Fact]
    public void IsCheckableParsesFromMarkup()
    {
        const string ns = "xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"";
        var result = XamlLoader.Load(
            $"<MenuItem IsCheckable=\"true\" IsChecked=\"True\" Header=\"Standard\" {ns}/>");
        var item = Assert.IsType<MenuItem>(result.Root);
        Assert.True(item.IsCheckable);
        Assert.True(item.IsChecked);
    }

    [Fact]
    public void MessageBoxShowsThroughPresenter()
    {
        Window? shown = null;
        MessageBox.Presenter = (window, size) =>
        {
            shown = window;
            Assert.Equal(360, size.Width);
            return MessageBoxResult.OK;
        };
        var result = MessageBox.Show(new Window(), "text", "caption",
            MessageBoxButton.OK, MessageBoxImage.Information);
        Assert.Equal(MessageBoxResult.OK, result);
        Assert.NotNull(shown);
        Assert.Equal("caption", shown.Title);
    }

    [Fact]
    public void MessageBoxWithoutPresenterThrows()
    {
        MessageBox.Presenter = null;
        Assert.Throws<InvalidOperationException>(() => MessageBox.Show("text"));
    }

    [Fact]
    public void MultilineTextBoxStacksLines()
    {
        var box = new TextBox { Text = "a\nb" };
        Assert.Equal(2, box.DisplayLines.Length);
        box.Measure(new WpfSize(800, 600));
        var single = new TextBox { Text = "a" };
        single.Measure(new WpfSize(800, 600));
        Assert.True(box.DesiredSize.Height > single.DesiredSize.Height);
    }

    [Fact]
    public void FocusPreviewVetoKeepsFocus()
    {
        var router = new DesktopWasm.Core.WasmInputRouter();
        var manager = new WpfInputManager(router);
        var plain = new TextBox();
        var guarded = new GuardedBox();
        var root = new Window();
        root.AddChild(plain);
        root.AddChild(guarded);
        manager.AddRoot(root);
        Assert.True(manager.TryFocus(plain));
        Assert.False(manager.TryFocus(guarded));
        Assert.Same(plain, manager.Focused);
    }

    private sealed class GuardedBox : TextBox
    {
        protected internal override void OnPreviewGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
        {
            e.Handled = true;
        }
    }

    [Fact]
    public void RoutedEventArgsDefaultUnandled()
    {
        var args = new RoutedEventArgs();
        Assert.False(args.Handled);
    }

    [Fact]
    public void TextCompositionCarriesText()
    {
        var args = new TextCompositionEventArgs("q");
        Assert.Equal("q", args.Text);
    }
}
