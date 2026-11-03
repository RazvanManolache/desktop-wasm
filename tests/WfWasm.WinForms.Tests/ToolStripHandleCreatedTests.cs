using System.Windows.Forms;

namespace WfWasm.WinForms.Tests;

// TOOLSTRIP-TRIAGE2 probe: Word boot hits "Invalid window handle" from
// Office12ToolStripRenderer.toolStrip_HandleCreated -> Control.get_Handle (IL_0026)
// -> NativeWindowBase.AssignHandle(IntPtr) -> IsWindow false.
// Mimics the vendor handler: read .Handle re-entrantly inside HandleCreated.
public sealed class ToolStripHandleCreatedTests : IDisposable
{
    private readonly List<nint> _observed = new();
    private readonly List<bool> _observedCreated = new();

    public void Dispose() { }

    [Fact]
    public void HandleCreated_HandlerSeesNonZeroHandle()
    {
        using var ts = new ToolStrip();
        ts.HandleCreated += (_, _) =>
        {
            _observed.Add(ts.Handle);
            _observedCreated.Add(ts.IsHandleCreated);
        };

        nint handle = ts.Handle;

        Assert.NotEqual(nint.Zero, handle);
        Assert.True(ts.IsHandleCreated);
        Assert.NotEmpty(_observed);
        Assert.All(_observed, h => Assert.NotEqual(nint.Zero, h));
        Assert.All(_observedCreated, c => Assert.True(c));
    }
}
