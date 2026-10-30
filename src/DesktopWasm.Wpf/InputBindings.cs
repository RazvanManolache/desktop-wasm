namespace DesktopWasm.Wpf;

/// <summary>
/// Command plus gesture (matches System.Windows.Input.InputBinding):
/// retained on <see cref="UIElement.InputBindings"/> so converted apps keep
/// their accelerator tables. Key gestures dispatch through the routed key
/// path; the command runs with CommandParameter when CanExecute holds.
/// Live-verified against PresentationCore 9.0.21: protected parameterless
/// plus (ICommand, InputGesture) constructors, Command/CommandParameter/
/// CommandTarget dependency properties, virtual Gesture, CreateInstanceCore.
/// </summary>
public class InputBinding : Freezable
{
    public static readonly DependencyProperty CommandProperty =
        DependencyProperty.Register("Command", typeof(ICommand), typeof(InputBinding),
            new FrameworkPropertyMetadata(null));

    public static readonly DependencyProperty CommandParameterProperty =
        DependencyProperty.Register("CommandParameter", typeof(object), typeof(InputBinding),
            new FrameworkPropertyMetadata(null));

    public static readonly DependencyProperty CommandTargetProperty =
        DependencyProperty.Register("CommandTarget", typeof(IInputElement), typeof(InputBinding),
            new FrameworkPropertyMetadata(null));

    protected InputBinding()
    {
    }

    public InputBinding(ICommand command, InputGesture gesture)
    {
        Command = command;
        Gesture = gesture;
    }

    public ICommand? Command
    {
        get => (ICommand?)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    public object? CommandParameter
    {
        get => GetValue(CommandParameterProperty);
        set => SetValue(CommandParameterProperty, value);
    }

    public IInputElement? CommandTarget
    {
        get => (IInputElement?)GetValue(CommandTargetProperty);
        set => SetValue(CommandTargetProperty, value);
    }

    public virtual InputGesture? Gesture { get; set; }

    protected override Freezable CreateInstanceCore() => new InputBinding();
}

/// <summary>
/// Key accelerator (matches System.Windows.Input.KeyBinding): setting
/// Key/Modifiers rebuilds the <see cref="KeyGesture"/> and setting a
/// KeyGesture pushes the parts back, like WPF. A non-key gesture is stored
/// but never matches key input. Upstream AvalonDock registers these via
/// the (ICommand, KeyGesture) constructor.
/// </summary>
public class KeyBinding : InputBinding
{
    public static readonly DependencyProperty ModifiersProperty =
        DependencyProperty.Register("Modifiers", typeof(ModifierKeys), typeof(KeyBinding),
            new FrameworkPropertyMetadata(ModifierKeys.None));

    public static readonly DependencyProperty KeyProperty =
        DependencyProperty.Register("Key", typeof(Key), typeof(KeyBinding),
            new FrameworkPropertyMetadata(Key.None));

    private bool _settingGesture;

    public KeyBinding()
    {
    }

    public KeyBinding(ICommand command, KeyGesture gesture)
        : this()
    {
        Command = command;
        Gesture = gesture;
    }

    public KeyBinding(ICommand command, Key key, ModifierKeys modifiers)
        : this()
    {
        Command = command;
        Key = key;
        Modifiers = modifiers;
        SynchronizeGestureFromProperties();
    }

    public override InputGesture? Gesture
    {
        get => base.Gesture;
        set
        {
            if (value is KeyGesture keyGesture)
            {
                _settingGesture = true;
                try
                {
                    Key = keyGesture.Key;
                    Modifiers = keyGesture.Modifiers;
                }
                finally
                {
                    _settingGesture = false;
                }
            }
            base.Gesture = value;
        }
    }

    public ModifierKeys Modifiers
    {
        get => (ModifierKeys)(GetValue(ModifiersProperty) ?? ModifierKeys.None);
        set
        {
            SetValue(ModifiersProperty, value);
            SynchronizeGestureFromProperties();
        }
    }

    public Key Key
    {
        get => (Key)(GetValue(KeyProperty) ?? Key.None);
        set
        {
            SetValue(KeyProperty, value);
            SynchronizeGestureFromProperties();
        }
    }

    protected override Freezable CreateInstanceCore() => new KeyBinding();

    private void SynchronizeGestureFromProperties()
    {
        if (_settingGesture) return;
        base.Gesture = new KeyGesture(Key, Modifiers);
    }
}

/// <summary>Mouse buttons-as-actions (matches System.Windows.Input.MouseAction).</summary>
public enum MouseAction
{
    None = 0,
    LeftClick = 1,
    RightClick = 2,
    MiddleClick = 3,
    WheelClick = 4,
    LeftDoubleClick = 5,
    RightDoubleClick = 6,
    MiddleDoubleClick = 7,
}

/// <summary>
/// Button plus modifiers gesture (matches System.Windows.Input.MouseGesture):
/// plain CLR parts (no dependency properties, like WPF), Matches mapping a
/// pressed MouseButtonEventArgs (double press by click count) or a
/// MouseWheelEventArgs onto the action with the ambient modifiers.
/// Live-verified against PresentationCore 9.0.21: (), (MouseAction),
/// (MouseAction, ModifierKeys) constructors, get/set Modifiers/MouseAction,
/// Matches(object, InputEventArgs) override.
/// </summary>
public class MouseGesture : InputGesture
{
    public MouseGesture()
        : this(MouseAction.None, ModifierKeys.None)
    {
    }

    public MouseGesture(MouseAction mouseAction)
        : this(mouseAction, ModifierKeys.None)
    {
    }

    public MouseGesture(MouseAction mouseAction, ModifierKeys modifiers)
    {
        MouseAction = mouseAction;
        Modifiers = modifiers;
    }

    public ModifierKeys Modifiers { get; set; }

    public MouseAction MouseAction { get; set; }

    public override bool Matches(object targetElement, InputEventArgs inputEventArgs)
    {
        // The routed path stamps the dispatching manager's modifier state
        // onto the event (ActiveModifiers), so matching never depends on
        // ambient cross-talk; explicitly constructed args fall back to the
        // ambient device like WPF's global keyboard state.
        var modifiers = inputEventArgs is MouseEventArgs mouse && mouse.ActiveModifiers.HasValue
            ? mouse.ActiveModifiers.Value
            : Keyboard.PrimaryDevice?.Modifiers ?? ModifierKeys.None;
        if (modifiers != Modifiers) return false;
        if (inputEventArgs is MouseButtonEventArgs button)
        {
            if (button.ButtonState != MouseButtonState.Pressed) return false;
            return MapButtonAction(button.ChangedButton, button.ClickCount) == MouseAction;
        }
        if (inputEventArgs is MouseWheelEventArgs)
            return MouseAction == MouseAction.WheelClick;
        return false;
    }

    internal static MouseAction MapButtonAction(MouseButton button, int clickCount) => button switch
    {
        MouseButton.Left => clickCount >= 2 ? MouseAction.LeftDoubleClick : MouseAction.LeftClick,
        MouseButton.Right => clickCount >= 2 ? MouseAction.RightDoubleClick : MouseAction.RightClick,
        MouseButton.Middle => clickCount >= 2 ? MouseAction.MiddleDoubleClick : MouseAction.MiddleClick,
        _ => MouseAction.None,
    };
}

/// <summary>
/// Mouse accelerator (matches System.Windows.Input.MouseBinding): setting
/// MouseAction rebuilds the <see cref="MouseGesture"/> and setting a
/// MouseGesture pushes the action back, like KeyBinding and WPF. A non-mouse
/// gesture is stored but never matches mouse input. Upstream HandyControl
/// registers these via the (ICommand, MouseGesture) constructor
/// (ImageViewer move binding) and reads MouseAction back off the gesture
/// (ImageViewer/ToggleBlock), while SimpleMouseBinding mirrors the KeyBinding
/// two-way sync between its own MouseAction DP and the gesture.
/// Live-verified against PresentationCore 9.0.21: public () and
/// (ICommand, MouseGesture) constructors, MouseActionProperty dependency
/// property, Gesture/MouseAction get/set, non-sealed InputBinding subclass.
/// </summary>
public class MouseBinding : InputBinding
{
    public static readonly DependencyProperty MouseActionProperty =
        DependencyProperty.Register("MouseAction", typeof(MouseAction), typeof(MouseBinding),
            new FrameworkPropertyMetadata(MouseAction.None));

    private bool _settingGesture;

    public MouseBinding()
    {
    }

    public MouseBinding(ICommand command, MouseGesture gesture)
        : this()
    {
        Command = command;
        Gesture = gesture;
    }

    public override InputGesture? Gesture
    {
        get => base.Gesture;
        set
        {
            if (value is MouseGesture mouseGesture)
            {
                _settingGesture = true;
                try
                {
                    MouseAction = mouseGesture.MouseAction;
                }
                finally
                {
                    _settingGesture = false;
                }
            }
            base.Gesture = value;
        }
    }

    public MouseAction MouseAction
    {
        get => (MouseAction)(GetValue(MouseActionProperty) ?? MouseAction.None);
        set
        {
            SetValue(MouseActionProperty, value);
            SynchronizeGestureFromProperties();
        }
    }

    protected override Freezable CreateInstanceCore() => new MouseBinding();

    private void SynchronizeGestureFromProperties()
    {
        if (_settingGesture) return;
        var current = base.Gesture as MouseGesture;
        base.Gesture = new MouseGesture(MouseAction, current?.Modifiers ?? ModifierKeys.None);
    }
}

/// <summary>
/// Binding list (matches System.Windows.Input.InputBindingCollection):
/// the per-element table converted apps fill (AvalonDock adds and removes
/// KeyBindings on UIElement.InputBindings). Sealed like WPF.
/// </summary>
public sealed class InputBindingCollection : System.Collections.IList
{
    private readonly List<InputBinding> _bindings = new();

    public InputBindingCollection()
    {
    }

    public InputBindingCollection(System.Collections.IList inputBindings)
    {
        ArgumentNullException.ThrowIfNull(inputBindings);
        foreach (var item in inputBindings)
        {
            if (item is InputBinding binding) _bindings.Add(binding);
            else throw new ArgumentException("All items must be InputBinding.", nameof(inputBindings));
        }
    }

    public InputBinding? this[int index]
    {
        get => _bindings[index];
        set => _bindings[index] = value ?? throw new ArgumentNullException(nameof(value));
    }

    public int Count => _bindings.Count;

    public bool IsSynchronized => false;

    public object SyncRoot => this;

    public bool IsReadOnly => false;

    public bool IsFixedSize => false;

    public int Add(InputBinding inputBinding)
    {
        ArgumentNullException.ThrowIfNull(inputBinding);
        _bindings.Add(inputBinding);
        return _bindings.Count - 1;
    }

    public bool Contains(InputBinding key) => _bindings.Contains(key);

    public int IndexOf(InputBinding value) => _bindings.IndexOf(value);

    public void Insert(int index, InputBinding inputBinding)
    {
        ArgumentNullException.ThrowIfNull(inputBinding);
        _bindings.Insert(index, inputBinding);
    }

    public void Remove(InputBinding inputBinding) => _bindings.Remove(inputBinding);

    public void RemoveAt(int index) => _bindings.RemoveAt(index);

    public void Clear() => _bindings.Clear();

    public void AddRange(System.Collections.ICollection collection)
    {
        ArgumentNullException.ThrowIfNull(collection);
        foreach (var item in collection)
        {
            if (item is InputBinding binding) _bindings.Add(binding);
            else throw new ArgumentException("All items must be InputBinding.", nameof(collection));
        }
    }

    public void CopyTo(InputBinding[] inputBindings, int index) => _bindings.CopyTo(inputBindings, index);

    /// <summary>First binding whose gesture matches (null when none).</summary>
    public InputBinding? FindMatch(object targetElement, InputEventArgs inputEventArgs)
    {
        foreach (var binding in _bindings)
        {
            if (binding.Gesture is not null && binding.Gesture.Matches(targetElement, inputEventArgs))
                return binding;
        }
        return null;
    }

    public System.Collections.IEnumerator GetEnumerator() => _bindings.GetEnumerator();

    object? System.Collections.IList.this[int index]
    {
        get => this[index];
        set => this[index] = (InputBinding)value!;
    }

    bool System.Collections.ICollection.IsSynchronized => IsSynchronized;

    object System.Collections.ICollection.SyncRoot => SyncRoot;

    int System.Collections.ICollection.Count => Count;

    void System.Collections.ICollection.CopyTo(Array array, int index) =>
        ((System.Collections.ICollection)_bindings).CopyTo(array, index);

    bool System.Collections.IList.IsReadOnly => IsReadOnly;

    bool System.Collections.IList.IsFixedSize => IsFixedSize;

    int System.Collections.IList.Add(object? value) => Add((InputBinding)value!);

    bool System.Collections.IList.Contains(object? value) => value is InputBinding binding && Contains(binding);

    int System.Collections.IList.IndexOf(object? value) => value is InputBinding binding ? IndexOf(binding) : -1;

    void System.Collections.IList.Insert(int index, object? value) => Insert(index, (InputBinding)value!);

    void System.Collections.IList.Remove(object? value)
    {
        if (value is InputBinding binding) Remove(binding);
    }
}
