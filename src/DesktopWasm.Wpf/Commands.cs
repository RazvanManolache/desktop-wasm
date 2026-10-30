namespace DesktopWasm.Wpf;

/// <summary>
/// Executable action with an availability gate, mirroring
/// System.Windows.Input.ICommand without the WindowsBase dependency.
/// Editors bind <c>Button.Command</c> to one; the button executes on click
/// only while <see cref="CanExecute"/> holds and greys out otherwise.
/// </summary>
public interface ICommand
{
    /// <summary>Whether <see cref="Execute"/> may run for this parameter.</summary>
    bool CanExecute(object? parameter);

    /// <summary>Runs the action (callers check <see cref="CanExecute"/> first).</summary>
    void Execute(object? parameter);

    /// <summary>Raised when availability may have changed; buttons re-query.</summary>
    event EventHandler? CanExecuteChanged;
}

/// <summary>Delegate-based <see cref="ICommand"/> for view-models and demos.</summary>
public sealed class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Func<object?, bool>? _canExecute;

    public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
    {
        ArgumentNullException.ThrowIfNull(execute);
        _execute = execute;
        _canExecute = canExecute;
    }

    /// <summary>Shorthand for parameterless actions.</summary>
    public RelayCommand(Action execute, Func<bool>? canExecute = null)
        : this(_ => execute(), canExecute is null ? null : _ => canExecute())
    {
        ArgumentNullException.ThrowIfNull(execute);
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

    public void Execute(object? parameter)
    {
        if (CanExecute(parameter)) _execute(parameter);
    }

    /// <summary>Re-query availability (buttons subscribed refresh themselves).</summary>
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>Executed handler (matches System.Windows.Input.ExecutedRoutedEventHandler).</summary>
public delegate void ExecutedRoutedEventHandler(object sender, ExecutedRoutedEventArgs e);

/// <summary>Can-execute handler (matches CanExecuteRoutedEventHandler).</summary>
public delegate void CanExecuteRoutedEventHandler(object sender, CanExecuteRoutedEventArgs e);

/// <summary>Executed payload (matches System.Windows.Input.ExecutedRoutedEventArgs).</summary>
public sealed class ExecutedRoutedEventArgs : RoutedEventArgs
{
    internal ExecutedRoutedEventArgs(ICommand command, object? parameter)
    {
        Command = command;
        Parameter = parameter;
    }

    public ICommand Command { get; }
    public object? Parameter { get; }
}

/// <summary>Can-execute payload (matches CanExecuteRoutedEventArgs).</summary>
public sealed class CanExecuteRoutedEventArgs : RoutedEventArgs
{
    internal CanExecuteRoutedEventArgs(ICommand command, object? parameter)
    {
        Command = command;
        Parameter = parameter;
    }

    public ICommand Command { get; }
    public object? Parameter { get; }
    public bool CanExecute { get; set; }
    public bool ContinueRouting { get; set; }
}

/// <summary>Command-to-handler binding (matches System.Windows.Input.CommandBinding).</summary>
public sealed class CommandBinding
{
    public CommandBinding() { }

    public CommandBinding(ICommand command) => Command = command;

    /// <summary>Command plus executed handler (matches the gallery Copy bindings; can-execute defaults to true, like WPF).</summary>
    public CommandBinding(ICommand command, ExecutedRoutedEventHandler executed)
    {
        Command = command;
        Executed += executed;
        CanExecute += (_, e) => e.CanExecute = true;
    }

    public CommandBinding(ICommand command, ExecutedRoutedEventHandler executed, CanExecuteRoutedEventHandler canExecute)
    {
        Command = command;
        Executed += executed;
        CanExecute += canExecute;
    }

    public ICommand? Command { get; set; }

    public event ExecutedRoutedEventHandler? Executed;
    public event ExecutedRoutedEventHandler? PreviewExecuted;
    public event CanExecuteRoutedEventHandler? CanExecute;
    public event CanExecuteRoutedEventHandler? PreviewCanExecute;

    internal void RaiseExecuted(object sender, ExecutedRoutedEventArgs args, bool preview)
    {
        if (preview) PreviewExecuted?.Invoke(sender, args);
        else Executed?.Invoke(sender, args);
    }

    internal void RaiseCanExecute(object sender, CanExecuteRoutedEventArgs args, bool preview)
    {
        if (preview) PreviewCanExecute?.Invoke(sender, args);
        else CanExecute?.Invoke(sender, args);
    }
}

/// <summary>Binding list (matches CommandBindingCollection).</summary>
public sealed class CommandBindingCollection : List<CommandBinding>
{
    /// <summary>
    /// Adds a binding and returns its index (matches real WPF, whose
    /// CommandBindingCollection.Add returns int32: converted OxyPlot.Wpf
    /// PlotView constructors call the int-returning shape, and a void-only
    /// engine raises MissingMethodException at instantiation).
    /// </summary>
    public new int Add(CommandBinding item)
    {
        base.Add(item);
        return Count - 1;
    }

    /// <summary>
    /// Appends bindings (matches CommandBindingCollection.AddRange(ICollection);
    /// converted AvalonEdit handlers bulk-load their bindings through it).
    /// </summary>
    public void AddRange(System.Collections.ICollection collection)
    {
        ArgumentNullException.ThrowIfNull(collection);
        foreach (CommandBinding binding in collection) Add(binding);
    }
}

/// <summary>Command routing hub (matches System.Windows.Input.CommandManager).</summary>
public static class CommandManager
{
    private static readonly Dictionary<(Type Owner, ICommand Command), List<CommandBinding>> ClassBindings = new();
    private static readonly object Gate = new();

    public static event EventHandler? RequerySuggested;

    public static void InvalidateRequerySuggested() => RequerySuggested?.Invoke(null, EventArgs.Empty);

    public static void RegisterClassCommandBinding(Type type, CommandBinding commandBinding)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(commandBinding);
        ArgumentNullException.ThrowIfNull(commandBinding.Command);
        lock (Gate)
        {
            var key = (type, commandBinding.Command);
            if (!ClassBindings.TryGetValue(key, out var list)) ClassBindings[key] = list = new List<CommandBinding>();
            list.Add(commandBinding);
        }
    }

    public static void UnregisterClassCommandBinding(Type type, CommandBinding commandBinding)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(commandBinding);
        lock (Gate)
        {
            if (commandBinding.Command is not null &&
                ClassBindings.TryGetValue((type, commandBinding.Command), out var list))
                list.Remove(commandBinding);
        }
    }

    internal static IEnumerable<CommandBinding> ClassBindingsFor(Type type, ICommand command)
    {
        lock (Gate)
        {
            for (var walk = type; walk is not null; walk = walk.BaseType)
                if (ClassBindings.TryGetValue((walk, command), out var list))
                    foreach (var binding in list.ToArray())
                        yield return binding;
        }
    }
}

/// <summary>Routed command (matches System.Windows.Input.RoutedCommand).</summary>
public class RoutedCommand : ICommand
{
    public RoutedCommand() : this(string.Empty, typeof(RoutedCommand)) { }

    public RoutedCommand(string name, Type ownerType)
    {
        Name = name;
        OwnerType = ownerType;
        // App-lifetime registration (matches the weak-event pattern loosely:
        // static commands live for the session; transient ones are collected
        // with their last subscriber).
        CommandManager.RequerySuggested += (_, _) => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }

    public RoutedCommand(string name, Type ownerType, InputGestureCollection inputGestures)
        : this(name, ownerType)
    {
        ArgumentNullException.ThrowIfNull(inputGestures);
        InputGestures.AddRange(inputGestures);
    }

    public string Name { get; }
    public Type OwnerType { get; }
    public InputGestureCollection InputGestures { get; } = new();

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => CanExecute(parameter, Keyboard.FocusedElement);

    public virtual bool CanExecute(object? parameter, IInputElement? target)
    {
        var chain = RouteChain(target as UIElement);
        if (chain.Count == 0) return false;
        var args = new CanExecuteRoutedEventArgs(this, parameter);
        foreach (var (element, binding) in Matches(chain).Reverse())
        {
            args.Source = element;
            binding.RaiseCanExecute(element, args, preview: true);
            if (args.Handled) return args.CanExecute;
        }
        foreach (var (element, binding) in Matches(chain))
        {
            args.Source = element;
            binding.RaiseCanExecute(element, args, preview: false);
            if (args.Handled) return args.CanExecute;
            if (args.CanExecute && !args.ContinueRouting)
            {
                args.Handled = true;
                return true;
            }
        }
        return args.CanExecute;
    }

    public void Execute(object? parameter) => Execute(parameter, Keyboard.FocusedElement);

    public virtual void Execute(object? parameter, IInputElement? target)
    {
        var chain = RouteChain(target as UIElement);
        if (chain.Count == 0) return;
        var args = new ExecutedRoutedEventArgs(this, parameter);
        foreach (var (element, binding) in Matches(chain).Reverse())
        {
            args.Source = element;
            binding.RaiseExecuted(element, args, preview: true);
            if (args.Handled) return;
        }
        foreach (var (element, binding) in Matches(chain))
        {
            args.Source = element;
            binding.RaiseExecuted(element, args, preview: false);
            if (args.Handled) return;
        }
    }

    private static List<UIElement> RouteChain(UIElement? target)
    {
        var chain = new List<UIElement>();
        for (var node = target; node is not null; node = node.Parent) chain.Add(node);
        return chain;
    }

    private IEnumerable<(UIElement Element, CommandBinding Binding)> Matches(List<UIElement> chain)
    {
        foreach (var element in chain)
        {
            foreach (var binding in element.CommandBindings)
                if (ReferenceEquals(binding.Command, this))
                    yield return (element, binding);
            foreach (var binding in CommandManager.ClassBindingsFor(element.GetType(), this))
                yield return (element, binding);
        }
    }
}

/// <summary>Labeled routed command (matches System.Windows.Input.RoutedUICommand).</summary>
public class RoutedUICommand : RoutedCommand
{
    public RoutedUICommand() : this(string.Empty, string.Empty, typeof(RoutedUICommand)) { }

    public RoutedUICommand(string text, string name, Type ownerType)
        : base(name, ownerType) => Text = text;

    public RoutedUICommand(string text, string name, Type ownerType, InputGestureCollection inputGestures)
        : base(name, ownerType, inputGestures) => Text = text;

    public string Text { get; set; } = string.Empty;
}

/// <summary>Stock application commands (matches System.Windows.Input.ApplicationCommands).</summary>
public static class ApplicationCommands
{
    public static RoutedUICommand New { get; } = new("New", nameof(New), typeof(ApplicationCommands));
    public static RoutedUICommand Open { get; } = new("Open", nameof(Open), typeof(ApplicationCommands));
    public static RoutedUICommand Save { get; } = new("Save", nameof(Save), typeof(ApplicationCommands));
    public static RoutedUICommand SaveAs { get; } = new("Save As", nameof(SaveAs), typeof(ApplicationCommands));
    public static RoutedUICommand Close { get; } = new("Close", nameof(Close), typeof(ApplicationCommands));
    public static RoutedUICommand Print { get; } = new("Print", nameof(Print), typeof(ApplicationCommands));
    public static RoutedUICommand PrintPreview { get; } = new("Print Preview", nameof(PrintPreview), typeof(ApplicationCommands));
    public static RoutedUICommand CancelPrint { get; } = new("Cancel Print", nameof(CancelPrint), typeof(ApplicationCommands));
    public static RoutedUICommand Cut { get; } = new("Cut", nameof(Cut), typeof(ApplicationCommands));
    public static RoutedUICommand Copy { get; } = new("Copy", nameof(Copy), typeof(ApplicationCommands));
    public static RoutedUICommand Paste { get; } = new("Paste", nameof(Paste), typeof(ApplicationCommands));
    public static RoutedUICommand Delete { get; } = new("Delete", nameof(Delete), typeof(ApplicationCommands));
    public static RoutedUICommand Undo { get; } = new("Undo", nameof(Undo), typeof(ApplicationCommands));
    public static RoutedUICommand Redo { get; } = new("Redo", nameof(Redo), typeof(ApplicationCommands));
    public static RoutedUICommand Find { get; } = new("Find", nameof(Find), typeof(ApplicationCommands));
    public static RoutedUICommand Replace { get; } = new("Replace", nameof(Replace), typeof(ApplicationCommands));
    public static RoutedUICommand SelectAll { get; } = new("Select All", nameof(SelectAll), typeof(ApplicationCommands));
    public static RoutedUICommand Help { get; } = new("Help", nameof(Help), typeof(ApplicationCommands));
    public static RoutedUICommand Properties { get; } = new("Properties", nameof(Properties), typeof(ApplicationCommands));
    public static RoutedUICommand IncreaseZoom { get; } = new("Increase Zoom", nameof(IncreaseZoom), typeof(ApplicationCommands));
    public static RoutedUICommand DecreaseZoom { get; } = new("Decrease Zoom", nameof(DecreaseZoom), typeof(ApplicationCommands));
    public static RoutedUICommand Zoom { get; } = new("Zoom", nameof(Zoom), typeof(ApplicationCommands));
    public static RoutedUICommand GoToPage { get; } = new("Go To Page", nameof(GoToPage), typeof(ApplicationCommands));
    public static RoutedUICommand NextPage { get; } = new("Next Page", nameof(NextPage), typeof(ApplicationCommands));
    public static RoutedUICommand PreviousPage { get; } = new("Previous Page", nameof(PreviousPage), typeof(ApplicationCommands));
    public static RoutedUICommand FirstPage { get; } = new("First Page", nameof(FirstPage), typeof(ApplicationCommands));
    public static RoutedUICommand LastPage { get; } = new("Last Page", nameof(LastPage), typeof(ApplicationCommands));
    public static RoutedUICommand CorrectionList { get; } = new("Correction List", nameof(CorrectionList), typeof(ApplicationCommands));
    public static RoutedUICommand ContextMenu { get; } = new("Context Menu", nameof(ContextMenu), typeof(ApplicationCommands));
    public static RoutedUICommand Stop { get; } = new("Stop", nameof(Stop), typeof(ApplicationCommands));
    public static RoutedUICommand NotACommand { get; } = new("Not A Command", nameof(NotACommand), typeof(ApplicationCommands));
}

/// <summary>Component-level commands (matches System.Windows.Input.ComponentCommands).</summary>
public static class ComponentCommands
{
    public static RoutedUICommand MoveFocusBack { get; } = new("Move Focus Back", nameof(MoveFocusBack), typeof(ComponentCommands));
    public static RoutedUICommand MoveFocusDown { get; } = new("Move Focus Down", nameof(MoveFocusDown), typeof(ComponentCommands));
    public static RoutedUICommand MoveFocusForward { get; } = new("Move Focus Forward", nameof(MoveFocusForward), typeof(ComponentCommands));
    public static RoutedUICommand MoveFocusPageDown { get; } = new("Move Focus Page Down", nameof(MoveFocusPageDown), typeof(ComponentCommands));
    public static RoutedUICommand MoveFocusPageUp { get; } = new("Move Focus Page Up", nameof(MoveFocusPageUp), typeof(ComponentCommands));
    public static RoutedUICommand MoveFocusUp { get; } = new("Move Focus Up", nameof(MoveFocusUp), typeof(ComponentCommands));
    public static RoutedUICommand MoveToEnd { get; } = new("Move To End", nameof(MoveToEnd), typeof(ComponentCommands));
    public static RoutedUICommand MoveToHome { get; } = new("Move To Home", nameof(MoveToHome), typeof(ComponentCommands));
    public static RoutedUICommand MoveToPageDown { get; } = new("Move To Page Down", nameof(MoveToPageDown), typeof(ComponentCommands));
    public static RoutedUICommand MoveToPageUp { get; } = new("Move To Page Up", nameof(MoveToPageUp), typeof(ComponentCommands));
    public static RoutedUICommand ScrollByLine { get; } = new("Scroll By Line", nameof(ScrollByLine), typeof(ComponentCommands));
    public static RoutedUICommand ScrollPageDown { get; } = new("Scroll Page Down", nameof(ScrollPageDown), typeof(ComponentCommands));
    public static RoutedUICommand ScrollPageLeft { get; } = new("Scroll Page Left", nameof(ScrollPageLeft), typeof(ComponentCommands));
    public static RoutedUICommand ScrollPageRight { get; } = new("Scroll Page Right", nameof(ScrollPageRight), typeof(ComponentCommands));
    public static RoutedUICommand ScrollPageUp { get; } = new("Scroll Page Up", nameof(ScrollPageUp), typeof(ComponentCommands));
    public static RoutedUICommand SelectToEnd { get; } = new("Select To End", nameof(SelectToEnd), typeof(ComponentCommands));
    public static RoutedUICommand SelectToHome { get; } = new("Select To Home", nameof(SelectToHome), typeof(ComponentCommands));
    public static RoutedUICommand SelectToPageDown { get; } = new("Select To Page Down", nameof(SelectToPageDown), typeof(ComponentCommands));
    public static RoutedUICommand SelectToPageUp { get; } = new("Select To Page Up", nameof(SelectToPageUp), typeof(ComponentCommands));
}

/// <summary>Navigation commands (matches System.Windows.Input.NavigationCommands).</summary>
public static class NavigationCommands
{
    public static RoutedUICommand BrowseBack { get; } = new("Browse Back", nameof(BrowseBack), typeof(NavigationCommands));
    public static RoutedUICommand BrowseForward { get; } = new("Browse Forward", nameof(BrowseForward), typeof(NavigationCommands));
    public static RoutedUICommand BrowseHome { get; } = new("Browse Home", nameof(BrowseHome), typeof(NavigationCommands));
    public static RoutedUICommand BrowseStop { get; } = new("Browse Stop", nameof(BrowseStop), typeof(NavigationCommands));
    public static RoutedUICommand DecreaseZoom { get; } = new("Decrease Zoom", nameof(DecreaseZoom), typeof(NavigationCommands));
    public static RoutedUICommand FirstPage { get; } = new("First Page", nameof(FirstPage), typeof(NavigationCommands));
    public static RoutedUICommand GoToPage { get; } = new("Go To Page", nameof(GoToPage), typeof(NavigationCommands));
    public static RoutedUICommand IncreaseZoom { get; } = new("Increase Zoom", nameof(IncreaseZoom), typeof(NavigationCommands));
    public static RoutedUICommand LastPage { get; } = new("Last Page", nameof(LastPage), typeof(NavigationCommands));
    public static RoutedUICommand NavigateJournal { get; } = new("Navigate Journal", nameof(NavigateJournal), typeof(NavigationCommands));
    public static RoutedUICommand NextPage { get; } = new("Next Page", nameof(NextPage), typeof(NavigationCommands));
    public static RoutedUICommand PreviousPage { get; } = new("Previous Page", nameof(PreviousPage), typeof(NavigationCommands));
    public static RoutedUICommand Refresh { get; } = new("Refresh", nameof(Refresh), typeof(NavigationCommands));
    public static RoutedUICommand Search { get; } = new("Search", nameof(Search), typeof(NavigationCommands));
    public static RoutedUICommand Zoom { get; } = new("Zoom", nameof(Zoom), typeof(NavigationCommands));
}

/// <summary>Media commands (matches System.Windows.Input.MediaCommands).</summary>
public static class MediaCommands
{
    public static RoutedUICommand BoostBass { get; } = new("Boost Bass", nameof(BoostBass), typeof(MediaCommands));
    public static RoutedUICommand ChannelDown { get; } = new("Channel Down", nameof(ChannelDown), typeof(MediaCommands));
    public static RoutedUICommand ChannelUp { get; } = new("Channel Up", nameof(ChannelUp), typeof(MediaCommands));
    public static RoutedUICommand DecreaseBass { get; } = new("Decrease Bass", nameof(DecreaseBass), typeof(MediaCommands));
    public static RoutedUICommand DecreaseMicrophoneVolume { get; } = new("Decrease Microphone Volume", nameof(DecreaseMicrophoneVolume), typeof(MediaCommands));
    public static RoutedUICommand DecreaseTreble { get; } = new("Decrease Treble", nameof(DecreaseTreble), typeof(MediaCommands));
    public static RoutedUICommand DecreaseVolume { get; } = new("Decrease Volume", nameof(DecreaseVolume), typeof(MediaCommands));
    public static RoutedUICommand FastForward { get; } = new("Fast Forward", nameof(FastForward), typeof(MediaCommands));
    public static RoutedUICommand IncreaseBass { get; } = new("Increase Bass", nameof(IncreaseBass), typeof(MediaCommands));
    public static RoutedUICommand IncreaseMicrophoneVolume { get; } = new("Increase Microphone Volume", nameof(IncreaseMicrophoneVolume), typeof(MediaCommands));
    public static RoutedUICommand IncreaseTreble { get; } = new("Increase Treble", nameof(IncreaseTreble), typeof(MediaCommands));
    public static RoutedUICommand IncreaseVolume { get; } = new("Increase Volume", nameof(IncreaseVolume), typeof(MediaCommands));
    public static RoutedUICommand MuteMicrophoneVolume { get; } = new("Mute Microphone Volume", nameof(MuteMicrophoneVolume), typeof(MediaCommands));
    public static RoutedUICommand MuteVolume { get; } = new("Mute Volume", nameof(MuteVolume), typeof(MediaCommands));
    public static RoutedUICommand NextTrack { get; } = new("Next Track", nameof(NextTrack), typeof(MediaCommands));
    public static RoutedUICommand Pause { get; } = new("Pause", nameof(Pause), typeof(MediaCommands));
    public static RoutedUICommand Play { get; } = new("Play", nameof(Play), typeof(MediaCommands));
    public static RoutedUICommand PreviousTrack { get; } = new("Previous Track", nameof(PreviousTrack), typeof(MediaCommands));
    public static RoutedUICommand Record { get; } = new("Record", nameof(Record), typeof(MediaCommands));
    public static RoutedUICommand Rewind { get; } = new("Rewind", nameof(Rewind), typeof(MediaCommands));
    public static RoutedUICommand Select { get; } = new("Select", nameof(Select), typeof(MediaCommands));
    public static RoutedUICommand Stop { get; } = new("Stop", nameof(Stop), typeof(MediaCommands));
    public static RoutedUICommand ToggleMicrophoneOnOff { get; } = new("Toggle Microphone On Off", nameof(ToggleMicrophoneOnOff), typeof(MediaCommands));
    public static RoutedUICommand TogglePlayPause { get; } = new("Toggle Play Pause", nameof(TogglePlayPause), typeof(MediaCommands));
}

/// <summary>Editing commands (matches System.Windows.Input.EditingCommands).</summary>
public static class EditingCommands
{
    public static RoutedUICommand AlignCenter { get; } = new("Align Center", nameof(AlignCenter), typeof(EditingCommands));
    public static RoutedUICommand AlignJustify { get; } = new("Align Justify", nameof(AlignJustify), typeof(EditingCommands));
    public static RoutedUICommand AlignLeft { get; } = new("Align Left", nameof(AlignLeft), typeof(EditingCommands));
    public static RoutedUICommand AlignRight { get; } = new("Align Right", nameof(AlignRight), typeof(EditingCommands));
    public static RoutedUICommand Backspace { get; } = new("Backspace", nameof(Backspace), typeof(EditingCommands));
    public static RoutedUICommand CorrectSpellingError { get; } = new("Correct Spelling Error", nameof(CorrectSpellingError), typeof(EditingCommands));
    public static RoutedUICommand CorrectWord { get; } = new("Correct Word", nameof(CorrectWord), typeof(EditingCommands));
    public static RoutedUICommand DecreaseFontSize { get; } = new("Decrease Font Size", nameof(DecreaseFontSize), typeof(EditingCommands));
    public static RoutedUICommand DecreaseIndentation { get; } = new("Decrease Indentation", nameof(DecreaseIndentation), typeof(EditingCommands));
    public static RoutedUICommand Delete { get; } = new("Delete", nameof(Delete), typeof(EditingCommands));
    public static RoutedUICommand DeleteNextWord { get; } = new("Delete Next Word", nameof(DeleteNextWord), typeof(EditingCommands));
    public static RoutedUICommand DeletePreviousWord { get; } = new("Delete Previous Word", nameof(DeletePreviousWord), typeof(EditingCommands));
    public static RoutedUICommand EnterLineBreak { get; } = new("Enter Line Break", nameof(EnterLineBreak), typeof(EditingCommands));
    public static RoutedUICommand EnterParagraphBreak { get; } = new("Enter Paragraph Break", nameof(EnterParagraphBreak), typeof(EditingCommands));
    public static RoutedUICommand IgnoreSpellingError { get; } = new("Ignore Spelling Error", nameof(IgnoreSpellingError), typeof(EditingCommands));
    public static RoutedUICommand IncreaseFontSize { get; } = new("Increase Font Size", nameof(IncreaseFontSize), typeof(EditingCommands));
    public static RoutedUICommand IncreaseIndentation { get; } = new("Increase Indentation", nameof(IncreaseIndentation), typeof(EditingCommands));
    public static RoutedUICommand MoveDownByLine { get; } = new("Move Down By Line", nameof(MoveDownByLine), typeof(EditingCommands));
    public static RoutedUICommand MoveDownByPage { get; } = new("Move Down By Page", nameof(MoveDownByPage), typeof(EditingCommands));
    public static RoutedUICommand MoveDownByParagraph { get; } = new("Move Down By Paragraph", nameof(MoveDownByParagraph), typeof(EditingCommands));
    public static RoutedUICommand MoveLeftByCharacter { get; } = new("Move Left By Character", nameof(MoveLeftByCharacter), typeof(EditingCommands));
    public static RoutedUICommand MoveLeftByWord { get; } = new("Move Left By Word", nameof(MoveLeftByWord), typeof(EditingCommands));
    public static RoutedUICommand MoveRightByCharacter { get; } = new("Move Right By Character", nameof(MoveRightByCharacter), typeof(EditingCommands));
    public static RoutedUICommand MoveRightByWord { get; } = new("Move Right By Word", nameof(MoveRightByWord), typeof(EditingCommands));
    public static RoutedUICommand MoveToDocumentEnd { get; } = new("Move To Document End", nameof(MoveToDocumentEnd), typeof(EditingCommands));
    public static RoutedUICommand MoveToDocumentStart { get; } = new("Move To Document Start", nameof(MoveToDocumentStart), typeof(EditingCommands));
    public static RoutedUICommand MoveToLineEnd { get; } = new("Move To Line End", nameof(MoveToLineEnd), typeof(EditingCommands));
    public static RoutedUICommand MoveToLineStart { get; } = new("Move To Line Start", nameof(MoveToLineStart), typeof(EditingCommands));
    public static RoutedUICommand MoveUpByLine { get; } = new("Move Up By Line", nameof(MoveUpByLine), typeof(EditingCommands));
    public static RoutedUICommand MoveUpByPage { get; } = new("Move Up By Page", nameof(MoveUpByPage), typeof(EditingCommands));
    public static RoutedUICommand MoveUpByParagraph { get; } = new("Move Up By Paragraph", nameof(MoveUpByParagraph), typeof(EditingCommands));
    public static RoutedUICommand SelectDownByLine { get; } = new("Select Down By Line", nameof(SelectDownByLine), typeof(EditingCommands));
    public static RoutedUICommand SelectDownByPage { get; } = new("Select Down By Page", nameof(SelectDownByPage), typeof(EditingCommands));
    public static RoutedUICommand SelectDownByParagraph { get; } = new("Select Down By Paragraph", nameof(SelectDownByParagraph), typeof(EditingCommands));
    public static RoutedUICommand SelectLeftByCharacter { get; } = new("Select Left By Character", nameof(SelectLeftByCharacter), typeof(EditingCommands));
    public static RoutedUICommand SelectLeftByWord { get; } = new("Select Left By Word", nameof(SelectLeftByWord), typeof(EditingCommands));
    public static RoutedUICommand SelectRightByCharacter { get; } = new("Select Right By Character", nameof(SelectRightByCharacter), typeof(EditingCommands));
    public static RoutedUICommand SelectRightByWord { get; } = new("Select Right By Word", nameof(SelectRightByWord), typeof(EditingCommands));
    public static RoutedUICommand SelectToDocumentEnd { get; } = new("Select To Document End", nameof(SelectToDocumentEnd), typeof(EditingCommands));
    public static RoutedUICommand SelectToDocumentStart { get; } = new("Select To Document Start", nameof(SelectToDocumentStart), typeof(EditingCommands));
    public static RoutedUICommand SelectToLineEnd { get; } = new("Select To Line End", nameof(SelectToLineEnd), typeof(EditingCommands));
    public static RoutedUICommand SelectToLineStart { get; } = new("Select To Line Start", nameof(SelectToLineStart), typeof(EditingCommands));
    public static RoutedUICommand SelectUpByLine { get; } = new("Select Up By Line", nameof(SelectUpByLine), typeof(EditingCommands));
    public static RoutedUICommand SelectUpByPage { get; } = new("Select Up By Page", nameof(SelectUpByPage), typeof(EditingCommands));
    public static RoutedUICommand SelectUpByParagraph { get; } = new("Select Up By Paragraph", nameof(SelectUpByParagraph), typeof(EditingCommands));
    public static RoutedUICommand TabBackward { get; } = new("Tab Backward", nameof(TabBackward), typeof(EditingCommands));
    public static RoutedUICommand TabForward { get; } = new("Tab Forward", nameof(TabForward), typeof(EditingCommands));
    public static RoutedUICommand ToggleBold { get; } = new("Toggle Bold", nameof(ToggleBold), typeof(EditingCommands));
    public static RoutedUICommand ToggleBullets { get; } = new("Toggle Bullets", nameof(ToggleBullets), typeof(EditingCommands));
    public static RoutedUICommand ToggleItalic { get; } = new("Toggle Italic", nameof(ToggleItalic), typeof(EditingCommands));
    public static RoutedUICommand ToggleNumbering { get; } = new("Toggle Numbering", nameof(ToggleNumbering), typeof(EditingCommands));
    public static RoutedUICommand ToggleSubscript { get; } = new("Toggle Subscript", nameof(ToggleSubscript), typeof(EditingCommands));
    public static RoutedUICommand ToggleSuperscript { get; } = new("Toggle Superscript", nameof(ToggleSuperscript), typeof(EditingCommands));
    public static RoutedUICommand ToggleUnderline { get; } = new("Toggle Underline", nameof(ToggleUnderline), typeof(EditingCommands));
}

/// <summary>Window chrome commands (matches System.Windows.SystemCommands).</summary>
public static class SystemCommands
{
    /// <summary>Closes the window (real behavior).</summary>
    public static void CloseWindow(Window? window) => window?.Close();

    public static void MaximizeWindow(Window? window) =>
        throw new InvalidOperationException("MaximizeWindow has no browser window chrome to act on.");

    public static void MinimizeWindow(Window? window) =>
        throw new InvalidOperationException("MinimizeWindow has no browser window chrome to act on.");

    public static void RestoreWindow(Window? window) =>
        throw new InvalidOperationException("RestoreWindow has no browser window chrome to act on.");

    public static void ShowSystemMenu(Window? window, Point screenLocation) =>
        throw new InvalidOperationException("ShowSystemMenu has no browser window chrome to act on.");
}
