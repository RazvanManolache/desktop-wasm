using WfWasm.Shim;

namespace WfWasm.Renderer;

/// <summary>
/// Optional native clip support. Scopes intersect the current clip and must be
/// disposed in reverse order. Coordinates remain in canvas space; nonpositive
/// dimensions clip everything. Bitmap uploads remain unaffected by clipping.
/// </summary>
public interface ICanvasClipping : ICanvas
{
    IDisposable PushClip(float x, float y, float width, float height);
}

internal sealed class CanvasClipScope(Action restore) : IDisposable
{
    private Action? _restore = restore;
    public void Dispose() => Interlocked.Exchange(ref _restore, null)?.Invoke();
}
