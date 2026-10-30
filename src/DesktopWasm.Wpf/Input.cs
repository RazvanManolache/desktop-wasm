namespace DesktopWasm.Wpf;

// Input enums (WindowsBase subset, shim, not port): member names and numeric
// values are generated from the Microsoft Learn API reference
// (System.Windows.Input.Key, ModifierKeys; see docs generation note in the
// Step 37 roadmap entry), so converted switch statements and casts behave.

/// <summary>Keyboard keys (matches System.Windows.Input.Key values).</summary>

public enum Key
{
    None = 0,
    Cancel = 1,
    Back = 2,
    Tab = 3,
    LineFeed = 4,
    Clear = 5,
    Enter = 6,
    Return = 6,
    Pause = 7,
    Capital = 8,
    CapsLock = 8,
    HangulMode = 9,
    KanaMode = 9,
    JunjaMode = 10,
    FinalMode = 11,
    HanjaMode = 12,
    KanjiMode = 12,
    Escape = 13,
    ImeConvert = 14,
    ImeNonConvert = 15,
    ImeAccept = 16,
    ImeModeChange = 17,
    Space = 18,
    PageUp = 19,
    Prior = 19,
    Next = 20,
    PageDown = 20,
    End = 21,
    Home = 22,
    Left = 23,
    Up = 24,
    Right = 25,
    Down = 26,
    Select = 27,
    Print = 28,
    Execute = 29,
    PrintScreen = 30,
    Snapshot = 30,
    Insert = 31,
    Delete = 32,
    Help = 33,
    D0 = 34,
    D1 = 35,
    D2 = 36,
    D3 = 37,
    D4 = 38,
    D5 = 39,
    D6 = 40,
    D7 = 41,
    D8 = 42,
    D9 = 43,
    A = 44,
    B = 45,
    C = 46,
    D = 47,
    E = 48,
    F = 49,
    G = 50,
    H = 51,
    I = 52,
    J = 53,
    K = 54,
    L = 55,
    M = 56,
    N = 57,
    O = 58,
    P = 59,
    Q = 60,
    R = 61,
    S = 62,
    T = 63,
    U = 64,
    V = 65,
    W = 66,
    X = 67,
    Y = 68,
    Z = 69,
    LWin = 70,
    RWin = 71,
    Apps = 72,
    Sleep = 73,
    NumPad0 = 74,
    NumPad1 = 75,
    NumPad2 = 76,
    NumPad3 = 77,
    NumPad4 = 78,
    NumPad5 = 79,
    NumPad6 = 80,
    NumPad7 = 81,
    NumPad8 = 82,
    NumPad9 = 83,
    Multiply = 84,
    Add = 85,
    Separator = 86,
    Subtract = 87,
    Decimal = 88,
    Divide = 89,
    F1 = 90,
    F2 = 91,
    F3 = 92,
    F4 = 93,
    F5 = 94,
    F6 = 95,
    F7 = 96,
    F8 = 97,
    F9 = 98,
    F10 = 99,
    F11 = 100,
    F12 = 101,
    F13 = 102,
    F14 = 103,
    F15 = 104,
    F16 = 105,
    F17 = 106,
    F18 = 107,
    F19 = 108,
    F20 = 109,
    F21 = 110,
    F22 = 111,
    F23 = 112,
    F24 = 113,
    NumLock = 114,
    Scroll = 115,
    LeftShift = 116,
    RightShift = 117,
    LeftCtrl = 118,
    RightCtrl = 119,
    LeftAlt = 120,
    RightAlt = 121,
    BrowserBack = 122,
    BrowserForward = 123,
    BrowserRefresh = 124,
    BrowserStop = 125,
    BrowserSearch = 126,
    BrowserFavorites = 127,
    BrowserHome = 128,
    VolumeMute = 129,
    VolumeDown = 130,
    VolumeUp = 131,
    MediaNextTrack = 132,
    MediaPreviousTrack = 133,
    MediaStop = 134,
    MediaPlayPause = 135,
    LaunchMail = 136,
    SelectMedia = 137,
    LaunchApplication1 = 138,
    LaunchApplication2 = 139,
    Oem1 = 140,
    OemSemicolon = 140,
    OemPlus = 141,
    OemComma = 142,
    OemMinus = 143,
    OemPeriod = 144,
    Oem2 = 145,
    OemQuestion = 145,
    Oem3 = 146,
    OemTilde = 146,
    AbntC1 = 147,
    AbntC2 = 148,
    Oem4 = 149,
    OemOpenBrackets = 149,
    Oem5 = 150,
    OemPipe = 150,
    Oem6 = 151,
    OemCloseBrackets = 151,
    Oem7 = 152,
    OemQuotes = 152,
    Oem8 = 153,
    Oem102 = 154,
    OemBackslash = 154,
    ImeProcessed = 155,
    System = 156,
    DbeAlphanumeric = 157,
    OemAttn = 157,
    DbeKatakana = 158,
    OemFinish = 158,
    DbeHiragana = 159,
    OemCopy = 159,
    DbeSbcsChar = 160,
    OemAuto = 160,
    DbeDbcsChar = 161,
    OemEnlw = 161,
    DbeRoman = 162,
    OemBackTab = 162,
    Attn = 163,
    DbeNoRoman = 163,
    CrSel = 164,
    DbeEnterWordRegisterMode = 164,
    DbeEnterImeConfigureMode = 165,
    ExSel = 165,
    DbeFlushString = 166,
    EraseEof = 166,
    DbeCodeInput = 167,
    Play = 167,
    DbeNoCodeInput = 168,
    Zoom = 168,
    DbeDetermineString = 169,
    NoName = 169,
    DbeEnterDialogConversionMode = 170,
    Pa1 = 170,
    OemClear = 171,
    DeadCharProcessed = 172,
}

/// <summary>Modifier keys (matches System.Windows.Input.ModifierKeys).</summary>
[Flags]
public enum ModifierKeys
{
    None = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,
    Windows = 8,
}

/// <summary>Mouse buttons (matches System.Windows.Input.MouseButton).</summary>
public enum MouseButton
{
    Left = 0,
    Middle = 1,
    Right = 2,
    XButton1 = 3,
    XButton2 = 4,
}

/// <summary>Button states (matches System.Windows.Input.MouseButtonState).</summary>
public enum MouseButtonState
{
    Released = 0,
    Pressed = 1,
}

/// <summary>Focus directions (matches System.Windows.Input.FocusNavigationDirection).</summary>
public enum FocusNavigationDirection
{
    Next = 0,
    Previous = 1,
    First = 2,
    Last = 3,
    Left = 4,
    Right = 5,
    Up = 6,
    Down = 7,
}

/// <summary>Focus traversal scope (matches System.Windows.Input.KeyboardNavigationMode).</summary>
public enum KeyboardNavigationMode
{
    Continue = 0,
    Once = 1,
    Cycle = 2,
    Contained = 3,
    None = 4,
    Local = 5,
}

/// <summary>Focus traversal contracts (matches System.Windows.Input.KeyboardNavigation).</summary>
public static class KeyboardNavigation
{
    /// <summary>Parses a traversal mode (gallery pages use Once/Cycle).</summary>
    public static KeyboardNavigationMode ParseMode(string raw) => raw?.Trim() switch
    {
        "Continue" => KeyboardNavigationMode.Continue,
        "Once" => KeyboardNavigationMode.Once,
        "Cycle" => KeyboardNavigationMode.Cycle,
        "Contained" => KeyboardNavigationMode.Contained,
        "None" => KeyboardNavigationMode.None,
        "Local" => KeyboardNavigationMode.Local,
        _ => throw new FormatException($"'{raw}' is not a KeyboardNavigationMode (Continue/Once/Cycle/Contained/None/Local)."),
    };

    /// <summary>Tab-stop flag (matches KeyboardNavigation.IsTabStopProperty; attached, default true; Control re-exports it via AddOwner like WPF).</summary>
    public static readonly DependencyProperty IsTabStopProperty =
        DependencyProperty.RegisterAttached("IsTabStop", typeof(bool), typeof(KeyboardNavigation),
            new FrameworkPropertyMetadata(true));

    /// <summary>Reads the tab-stop flag (matches KeyboardNavigation.GetIsTabStop; attached bag plus DP store, per-type metadata default like WPF).</summary>
    public static bool GetIsTabStop(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (element is UIElement ui && ui.GetAttached("KeyboardNavigation.IsTabStop") is bool fromBag) return fromBag;
        return (bool?)element.GetValue(IsTabStopProperty) ?? true;
    }

    /// <summary>Writes the tab-stop flag (matches KeyboardNavigation.SetIsTabStop; DP store plus attached bag like the ScrollViewer knobs).</summary>
    public static void SetIsTabStop(DependencyObject element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(IsTabStopProperty, value);
        if (element is UIElement ui) ui.SetAttached("KeyboardNavigation.IsTabStop", value);
    }

    /// <summary>Traversal scope (matches KeyboardNavigation.TabNavigationProperty; attached, Inherits, default Continue like WPF).</summary>
    public static readonly DependencyProperty TabNavigationProperty =
        DependencyProperty.RegisterAttached("TabNavigation", typeof(KeyboardNavigationMode), typeof(KeyboardNavigation),
            new FrameworkPropertyMetadata(KeyboardNavigationMode.Continue, FrameworkPropertyMetadataOptions.Inherits));

    /// <summary>Reads the traversal scope (matches KeyboardNavigation.GetTabNavigation; walks ancestors like the ScrollViewer knobs).</summary>
    public static KeyboardNavigationMode GetTabNavigation(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        for (var node = element; node is UIElement ui; node = ui.Parent)
        {
            if (ui.GetAttached("KeyboardNavigation.TabNavigation") is KeyboardNavigationMode fromBag) return fromBag;
            if (ui.GetAttached("KeyboardNavigation.TabNavigation") is string bagText &&
                Enum.TryParse<KeyboardNavigationMode>(bagText, ignoreCase: true, out var bagParsed)) return bagParsed;
            if (ui.HasLocalValue(TabNavigationProperty) && ui.GetValue(TabNavigationProperty) is KeyboardNavigationMode local) return local;
        }
        return KeyboardNavigationMode.Continue;
    }

    /// <summary>Writes the traversal scope (matches KeyboardNavigation.SetTabNavigation; DP store plus attached bag like the ScrollViewer knobs).</summary>
    public static void SetTabNavigation(DependencyObject element, KeyboardNavigationMode value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(TabNavigationProperty, value);
        if (element is UIElement ui) ui.SetAttached("KeyboardNavigation.TabNavigation", value);
    }
}

/// <summary>Focus move request (matches System.Windows.Input.TraversalRequest).</summary>
public class TraversalRequest
{
    public TraversalRequest(FocusNavigationDirection focusNavigationDirection) =>
        FocusNavigationDirection = focusNavigationDirection;

    public FocusNavigationDirection FocusNavigationDirection { get; set; }

    public bool Wrapped { get; set; }
}

/// <summary>Gesture root (matches System.Windows.Input.InputGesture).</summary>
public abstract class InputGesture
{
    public abstract bool Matches(object targetElement, InputEventArgs inputEventArgs);
}

/// <summary>Gesture list (matches InputGestureCollection).</summary>
public sealed class InputGestureCollection : List<InputGesture>
{
    /// <summary>
    /// Appends a gesture and returns its index (matches real WPF, whose Add
    /// returns int32: converted AvalonEdit command wiring binds the
    /// int-returning shape, and a void-only Add raises MissingMethodException).
    /// </summary>
    public new int Add(InputGesture gesture)
    {
        ArgumentNullException.ThrowIfNull(gesture);
        base.Add(gesture);
        return Count - 1;
    }
}

/// <summary>Key plus modifiers gesture (matches System.Windows.Input.KeyGesture).</summary>
public class KeyGesture : InputGesture
{
    public KeyGesture(Key key) : this(key, ModifierKeys.None, string.Empty) { }

    public KeyGesture(Key key, ModifierKeys modifiers) : this(key, modifiers, string.Empty) { }

    public KeyGesture(Key key, ModifierKeys modifiers, string displayString)
    {
        Key = key;
        Modifiers = modifiers;
        DisplayString = displayString ?? string.Empty;
    }

    public Key Key { get; }
    public ModifierKeys Modifiers { get; }

    /// <summary>Explicit label, else the conventional "Ctrl+N" form.</summary>
    public string DisplayString { get; }

    public string GetDisplayStringForCulture(System.Globalization.CultureInfo culture) =>
        DisplayString.Length > 0 ? DisplayString : BuildDisplayString();

    public override bool Matches(object targetElement, InputEventArgs inputEventArgs)
    {
        if (inputEventArgs is not KeyEventArgs key) return false;
        if (key.Key != Key) return false;
        var modifiers = key.KeyboardDevice?.Modifiers ?? ModifierKeys.None;
        return modifiers == Modifiers;
    }

    internal static string BuildDisplayString(Key key, ModifierKeys modifiers)
    {
        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(key.ToString());
        return string.Join("+", parts);
    }

    private string BuildDisplayString() => BuildDisplayString(Key, Modifiers);
}

/// <summary>Parses "Ctrl+Shift+N" gestures (matches KeyGestureConverter).</summary>
public class KeyGestureConverter : System.ComponentModel.TypeConverter
{
    public override bool CanConvertFrom(System.ComponentModel.ITypeDescriptorContext? context, Type sourceType) =>
        sourceType == typeof(string);

    public override object? ConvertFrom(System.ComponentModel.ITypeDescriptorContext? context, System.Globalization.CultureInfo? culture, object value)
    {
        if (value is not string text) throw new NotSupportedException($"Cannot convert {value?.GetType().FullName ?? "null"} to KeyGesture (strings only).");
        return Parse(text);
    }

    internal static KeyGesture Parse(string text)
    {
        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) throw new FormatException($"'{text}' is not a key gesture (Ctrl+Shift+N).");
        if (!Enum.TryParse<Key>(parts[^1], ignoreCase: true, out var key))
            throw new FormatException($"'{parts[^1]}' is not a Key.");
        var modifiers = ModifierKeys.None;
        foreach (var part in parts[..^1])
        {
            modifiers |= part.ToLowerInvariant() switch
            {
                "ctrl" or "control" => ModifierKeys.Control,
                "shift" => ModifierKeys.Shift,
                "alt" => ModifierKeys.Alt,
                "win" or "windows" => ModifierKeys.Windows,
                _ => throw new FormatException($"'{part}' is not a modifier (Ctrl/Shift/Alt/Win)."),
            };
        }
        return new KeyGesture(key, modifiers);
    }
}

/// <summary>Parses key names (matches KeyConverter).</summary>
public class KeyConverter : System.ComponentModel.TypeConverter
{
    public override bool CanConvertFrom(System.ComponentModel.ITypeDescriptorContext? context, Type sourceType) =>
        sourceType == typeof(string);

    public override bool CanConvertTo(System.ComponentModel.ITypeDescriptorContext? context, Type? destinationType) =>
        destinationType == typeof(string);

    public override object? ConvertFrom(System.ComponentModel.ITypeDescriptorContext? context, System.Globalization.CultureInfo? culture, object value)
    {
        if (value is not string text) throw new NotSupportedException("KeyConverter converts strings only.");
        if (!Enum.TryParse<Key>(text, ignoreCase: true, out var key))
            throw new FormatException($"'{text}' is not a Key.");
        return key;
    }

    public override object? ConvertTo(System.ComponentModel.ITypeDescriptorContext? context, System.Globalization.CultureInfo? culture, object? value, Type destinationType)
    {
        if (destinationType == typeof(string) && value is Key key) return key.ToString();
        throw new NotSupportedException("KeyConverter converts Key to string only.");
    }
}

/// <summary>Parses modifier lists (matches ModifierKeysConverter).</summary>
public class ModifierKeysConverter : System.ComponentModel.TypeConverter
{
    public override bool CanConvertFrom(System.ComponentModel.ITypeDescriptorContext? context, Type sourceType) =>
        sourceType == typeof(string);

    public override object? ConvertFrom(System.ComponentModel.ITypeDescriptorContext? context, System.Globalization.CultureInfo? culture, object value)
    {
        if (value is not string text) throw new NotSupportedException("ModifierKeysConverter converts strings only.");
        if (text.Trim().Equals("None", StringComparison.OrdinalIgnoreCase)) return ModifierKeys.None;
        var modifiers = ModifierKeys.None;
        foreach (var part in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            modifiers |= part.ToLowerInvariant() switch
            {
                "ctrl" or "control" => ModifierKeys.Control,
                "shift" => ModifierKeys.Shift,
                "alt" => ModifierKeys.Alt,
                "win" or "windows" => ModifierKeys.Windows,
                _ => throw new FormatException($"'{part}' is not a modifier (Ctrl/Shift/Alt/Win)."),
            };
        }
        return modifiers;
    }
}

/// <summary>
/// Virtual-key mapping (matches System.Windows.Input.KeyInterop; US layout,
/// best-effort: unmapped keys fall back to 0 / Key.None like real WPF).
/// </summary>
public static class KeyInterop
{
    private static readonly Dictionary<Key, int> KeyToVirtual = BuildKeyMap();
    private static readonly Dictionary<int, Key> VirtualToKey = BuildReverseMap();

    public static int VirtualKeyFromKey(Key key) =>
        KeyToVirtual.TryGetValue(key, out var virtualKey) ? virtualKey : 0;

    public static Key KeyFromVirtualKey(int virtualKey) =>
        VirtualToKey.TryGetValue(virtualKey, out var key) ? key : Key.None;

    private static Dictionary<Key, int> BuildKeyMap()
    {
        var map = new Dictionary<Key, int>();
        for (var digit = 0; digit <= 9; digit++)
            map[(Key)((int)Key.D0 + digit)] = 48 + digit;
        for (var letter = 0; letter < 26; letter++)
            map[(Key)((int)Key.A + letter)] = 65 + letter;
        for (var function = 0; function < 24; function++)
            map[(Key)((int)Key.F1 + function)] = 112 + function;
        for (var pad = 0; pad <= 9; pad++)
            map[(Key)((int)Key.NumPad0 + pad)] = 96 + pad;
        foreach (var (key, virtualKey) in new (Key, int)[]
        {
            (Key.Cancel, 3), (Key.Back, 8), (Key.Tab, 9), (Key.Clear, 12),
            (Key.Enter, 13), (Key.Pause, 19), (Key.Capital, 20), (Key.Escape, 27),
            (Key.Space, 32), (Key.PageUp, 33), (Key.PageDown, 34), (Key.End, 35),
            (Key.Home, 36), (Key.Left, 37), (Key.Up, 38), (Key.Right, 39),
            (Key.Down, 40), (Key.Select, 41), (Key.Print, 42), (Key.Execute, 43),
            (Key.PrintScreen, 44), (Key.Insert, 45), (Key.Delete, 46), (Key.Help, 47),
            (Key.LWin, 91), (Key.RWin, 92), (Key.Apps, 93), (Key.Sleep, 95),
            (Key.Multiply, 106), (Key.Add, 107), (Key.Separator, 108), (Key.Subtract, 109),
            (Key.Decimal, 110), (Key.Divide, 111), (Key.NumLock, 144), (Key.Scroll, 145),
            (Key.LeftShift, 160), (Key.RightShift, 161), (Key.LeftCtrl, 162), (Key.RightCtrl, 163),
            (Key.LeftAlt, 164), (Key.RightAlt, 165),
            (Key.Oem1, 186), (Key.OemPlus, 187), (Key.OemComma, 188), (Key.OemMinus, 189),
            (Key.OemPeriod, 190), (Key.OemQuestion, 191), (Key.OemTilde, 192),
            (Key.OemOpenBrackets, 219), (Key.OemPipe, 220), (Key.OemCloseBrackets, 221),
            (Key.OemQuotes, 222), (Key.Oem8, 223), (Key.OemBackslash, 226),
        }) map[key] = virtualKey;
        return map;
    }

    private static Dictionary<int, Key> BuildReverseMap()
    {
        var reverse = new Dictionary<int, Key>();
        foreach (var (key, virtualKey) in KeyToVirtual)
            reverse.TryAdd(virtualKey, key);
        return reverse;
    }
}
