namespace DesktopWasm.Wpf;

/// <summary>Dialog buttons (PresentationFramework subset).</summary>
public enum MessageBoxButton
{
    OK = 0,
    OKCancel = 1,
    YesNo = 4,
    YesNoCancel = 3,
}

/// <summary>Dialog icon (PresentationFramework subset; aliases match WPF).</summary>
public enum MessageBoxImage
{
    None = 0,
    Hand = 16,
    Question = 32,
    Exclamation = 48,
    Asterisk = 64,
    Error = Hand,
    Stop = Hand,
    Warning = Exclamation,
    Information = Asterisk,
}

/// <summary>Dialog outcome (PresentationFramework subset).</summary>
public enum MessageBoxResult
{
    None = 0,
    OK = 1,
    Cancel = 2,
    Yes = 6,
    No = 7,
}

/// <summary>
/// Modal message dialogs for converted apps (calculator About/error paths).
/// The browser cannot block on dismissal, so Show presents the dialog
/// non-blockingly and returns OK; the caller continues exactly as if the
/// user had acknowledged. The app host supplies the presenter
/// (Blazor wires it to WasmWpfHost.ShowDialog); without one Show is a
/// precise error instead of a silent drop.
/// </summary>
public static class MessageBox
{
    /// <summary>Presents a built dialog and returns the dismissal result.</summary>
    public static Func<Window, WpfSize, MessageBoxResult>? Presenter { get; set; }

    public static MessageBoxResult Show(string text) =>
        ShowCore(null, text, string.Empty, MessageBoxButton.OK, MessageBoxImage.None);

    public static MessageBoxResult Show(Window owner, string text) =>
        ShowCore(owner, text, string.Empty, MessageBoxButton.OK, MessageBoxImage.None);

    public static MessageBoxResult Show(string text, string caption) =>
        ShowCore(null, text, caption, MessageBoxButton.OK, MessageBoxImage.None);

    public static MessageBoxResult Show(Window owner, string text, string caption) =>
        ShowCore(owner, text, caption, MessageBoxButton.OK, MessageBoxImage.None);

    public static MessageBoxResult Show(string text, string caption, MessageBoxButton button) =>
        ShowCore(null, text, caption, button, MessageBoxImage.None);

    public static MessageBoxResult Show(Window owner, string text, string caption, MessageBoxButton button) =>
        ShowCore(owner, text, caption, button, MessageBoxImage.None);

    public static MessageBoxResult Show(string text, string caption, MessageBoxButton button, MessageBoxImage image) =>
        ShowCore(null, text, caption, button, image);

    public static MessageBoxResult Show(Window owner, string text, string caption, MessageBoxButton button, MessageBoxImage image) =>
        ShowCore(owner, text, caption, button, image);

    private static MessageBoxResult ShowCore(Window? owner, string text, string caption, MessageBoxButton button, MessageBoxImage image)
    {
        _ = owner;
        _ = image;
        if (button != MessageBoxButton.OK)
            throw new NotSupportedException($"MessageBox.{button} requires a synchronous decision bridge; the browser host must not invent a result.");
        if (Presenter is null)
            throw new InvalidOperationException(
                "MessageBox has no presenter: the app host must set MessageBox.Presenter (Blazor wires WasmWpfHost.ShowDialog).");
        // Non-blocking (the browser loop cannot wait): the dialog shows and
        // the caller continues as if acknowledged, matching the calc's
        // fire-and-continue error/About flows.
        return Presenter(CreateDialog(text, caption), new WpfSize(360, 160));
    }

    /// <summary>
    /// Builds the dialog window (caption + text + OK). The presenter shows
    /// it; OK closes it (modal-aware via <see cref="Window.Close"/>).
    /// </summary>
    public static Window CreateDialog(string text, string caption)
    {
        var window = new Window { Title = caption };
        var panel = new StackPanel { Margin = new Thickness(12) };
        panel.AddChild(new TextBlock { Text = text, Margin = new Thickness(0, 0, 0, 12) });
        var ok = new Button { Content = "OK", Width = 100, HorizontalAlignment = HorizontalAlignment.Center };
        ok.Click += (_, _) => window.Close();
        panel.AddChild(ok);
        window.Content = panel;
        return window;
    }
}
