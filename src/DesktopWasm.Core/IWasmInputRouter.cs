namespace DesktopWasm.Core;

/// <summary>Normalized pointer button.</summary>
public enum WasmPointerButton { None, Left, Middle, Right }

/// <summary>Explicit pointer lifecycle phase (a move must never read as a release).</summary>
public enum WasmPointerKind { Down, Move, Up, Cancel }

/// <summary>
/// Normalized pointer event (DOM PointerEvent -> platform message).
/// The 5-argument shape is the legacy contract: <see cref="Kind"/> derives
/// from <see cref="IsDown"/> (down vs. up) and <see cref="PointerId"/> is 0.
/// New producers set <see cref="Kind"/> (and <see cref="PointerId"/>) explicitly.
/// </summary>
public readonly record struct WasmPointerArgs(
    WasmWindowHandle Window,
    double X,
    double Y,
    WasmPointerButton Button,
    bool IsDown)
{
    /// <summary>Explicit lifecycle phase; defaults from IsDown for legacy callers.</summary>
    public WasmPointerKind Kind { get; init; } = IsDown ? WasmPointerKind.Down : WasmPointerKind.Up;

    /// <summary>DOM pointerId distinguishing concurrent pointers (0 when unknown).</summary>
    public int PointerId { get; init; } = 0;
}

/// <summary>Normalized keyboard event (DOM KeyboardEvent -> platform message).</summary>
public readonly record struct WasmKeyArgs(
    WasmWindowHandle Window,
    string Key,
    bool IsDown,
    bool Ctrl,
    bool Shift,
    bool Alt);

/// <summary>Normalized wheel event (DOM WheelEvent -> platform message).</summary>
public readonly record struct WasmWheelArgs(
    WasmWindowHandle Window,
    double X,
    double Y,
    double DeltaY);

/// <summary>Normalized input event consumed by both frameworks.</summary>
public abstract record WasmInputEvent(WasmWindowHandle Window)
{
    public sealed record Pointer(WasmPointerArgs Args) : WasmInputEvent(Args.Window);
    public sealed record Key(WasmKeyArgs Args) : WasmInputEvent(Args.Window);
    public sealed record Wheel(WasmWheelArgs Args) : WasmInputEvent(Args.Window);
}

/// <summary>
/// Input and clipboard router. Translates DOM events into normalized
/// platform messages for System.Windows.Forms and WPF InputManager.
/// </summary>
public interface IWasmInputRouter
{
    void DispatchPointer(WasmPointerArgs args);
    void DispatchKeyboard(WasmKeyArgs args);
    void DispatchWheel(WasmWheelArgs args);
    event Action<WasmInputEvent>? Routed;
}

/// <summary>Async clipboard surface (navigator.clipboard under the hood).</summary>
public interface IWasmClipboard
{
    Task<string?> GetTextAsync(CancellationToken ct = default);
    Task SetTextAsync(string text, CancellationToken ct = default);
}
