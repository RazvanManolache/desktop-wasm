namespace DesktopWasm.Wpf.Tests;

/// <summary>MainWindow input/command bindings (MaterialDesign boot shape):
/// the Window.InputBindings KeyBinding lands in the live collection with a
/// parsed gesture, and its sourceless Command captures the host data context
/// when it lands; Window.CommandBindings resolves the ApplicationCommands
/// static and rides the shared event-wire list to codebehind.</summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class InputBindingXamlTests : IDisposable
{
    private const string PresentationNs = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private const string XamlNs = "http://schemas.microsoft.com/winfx/2006/xaml";

    private readonly Application? _previous = Application.Current;

    public void Dispose()
    {
        XamlLoader.CustomElementSource = null;
        Application.Current = _previous;
    }

    private sealed class RecordingCommand : ICommand
    {
        public readonly List<object?> Calls = new();
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => Calls.Add(parameter);
        public event EventHandler? CanExecuteChanged;
    }

    private sealed class ProbeHost
    {
        public ProbeHost(RecordingCommand command) => ProbeCommand = command;
        public ICommand ProbeCommand { get; }
    }

    [Fact]
    public void Window_input_bindings_parse_and_capture_data_context_command()
    {
        var window = (Window)XamlLoader.Load(
            "<Window xmlns=\"" + PresentationNs + "\" xmlns:x=\"" + XamlNs + "\">" +
            "<Window.InputBindings>" +
            "<KeyBinding Key=\"K\" Modifiers=\"Control\" Command=\"{Binding ProbeCommand}\" />" +
            "</Window.InputBindings>" +
            "<Window.CommandBindings>" +
            "<CommandBinding Command=\"Copy\" Executed=\"OnCopyProbe\" />" +
            "</Window.CommandBindings>" +
            "</Window>").Root;
        Assert.Equal(1, window.InputBindings.Count);
        var binding = Assert.IsType<KeyBinding>(window.InputBindings[0]);
        Assert.Equal(Key.K, binding.Key);
        Assert.Equal(ModifierKeys.Control, binding.Modifiers);
        Assert.Null(binding.Command);
        var command = new RecordingCommand();
        window.DataContext = new ProbeHost(command);
        Assert.Same(command, binding.Command);
        var commandBinding = Assert.Single(window.CommandBindings);
        Assert.Same(ApplicationCommands.Copy, commandBinding.Command);
    }
}
