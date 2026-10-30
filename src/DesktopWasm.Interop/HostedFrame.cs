using DesktopWasm.Graphics;

namespace DesktopWasm.Interop;

/// <summary>
/// Explicit shared-surface exchange: one presented frame is its device-px
/// size plus the DPI it was rendered at plus the draw commands that paint
/// it. Both adapters produce this; neither command format is assumed to
/// match the other's — <see cref="Commands"/> is the canonical list.
/// </summary>
public sealed record HostedFrame
{
    public HostedFrame(int width, int height, InteropDpi dpi, IReadOnlyList<DrawCommand> commands)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentNullException.ThrowIfNull(commands);
        Width = width;
        Height = height;
        Dpi = dpi;
        Commands = commands;
    }

    public int Width { get; init; }

    public int Height { get; init; }

    public InteropDpi Dpi { get; init; }

    public IReadOnlyList<DrawCommand> Commands { get; init; }

    /// <summary>Empty 1x1 identity frame (placeholder, never presented).</summary>
    public static HostedFrame Empty => new(1, 1, InteropDpi.Identity, Array.Empty<DrawCommand>());
}

/// <summary>
/// Lifetime contract every hosted guest implements: deterministic dispose,
/// an invalidation signal for layout-affecting changes, and frame rendering.
/// </summary>
public interface IHostedGuest : IDisposable
{
    /// <summary>True after <see cref="IDisposable.Dispose"/> runs once.</summary>
    bool IsDisposed { get; }

    /// <summary>Raised after resize/DPI changes that require a re-render.</summary>
    event Action? Invalidated;

    /// <summary>
    /// Renders the current guest state to a frame (throws
    /// <see cref="ObjectDisposedException"/> after disposal).
    /// </summary>
    HostedFrame RenderFrame();
}
