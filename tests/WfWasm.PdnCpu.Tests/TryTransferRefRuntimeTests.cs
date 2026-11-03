using WfWasm.PdnCpu;

// TRIO-TRYTRANSFER-R34 (WC-20261103T1115-vendor-trytransfer): the gen40
// toolstrip-click "CpuPaint: TryTransferRef open def missing" fired when
// CastOrTransferRefRuntime could not resolve the open generic definition:
// the R6 scan was Public-only while the served def is non-public (R16
// widened the flags + added carrier preference), and a first call before
// the carrier assembly loads missed entirely (R34 pre-loads
// PaintDotNet.ComponentModel, mirroring R12/R17). PDN assemblies do not
// load under the test host, so local fakes carry the proof (same
// convention as FakeRenderScans.cs): a same-named ObjectRefExtensions with
// a 1-generic/1-param TryTransferRef exercises the runtime path (scan ->
// carries-preference -> MakeGenericMethod -> invoke -> cache). The type
// and method names must stay EXACT: the runtime binds by fixed name.
namespace PaintDotNet.ComponentModel
{
    public interface IFakeObjectRef { }

    public sealed class FakeTransferTarget : IFakeObjectRef { }

    public sealed class FakeTransferSource : IFakeObjectRef
    {
        public object? Moved { get; set; }
    }

    public static class ObjectRefExtensions
    {
        public static T TryTransferRef<T>(IFakeObjectRef objectRef) where T : class
        {
            if (objectRef is FakeTransferSource s && s.Moved is T t)
                return t;
            return null!;
        }
    }
}

public sealed class TryTransferRefRuntimeTests
{
    [Fact]
    public void TransferRef_FastPath_Returns_Instance_Unchanged()
    {
        var target = new PaintDotNet.ComponentModel.FakeTransferTarget();
        object got = CpuPaint.CastOrTransferRefRuntime(target, typeof(PaintDotNet.ComponentModel.FakeTransferTarget));
        Assert.Same(target, got);
    }

    [Fact]
    public void TransferRef_Hit_Returns_Moved_Ref_Through_Open_Def()
    {
        // Full runtime path with zero vendor execution: the scan finds the
        // same-named def, closes it over FakeTransferTarget, invokes it,
        // and returns the moved ref (then caches it for the next call).
        var moved = new PaintDotNet.ComponentModel.FakeTransferTarget();
        var source = new PaintDotNet.ComponentModel.FakeTransferSource { Moved = moved };
        object got = CpuPaint.CastOrTransferRefRuntime(source, typeof(PaintDotNet.ComponentModel.FakeTransferTarget));
        Assert.Same(moved, got);
        object again = CpuPaint.CastOrTransferRefRuntime(source, typeof(PaintDotNet.ComponentModel.FakeTransferTarget));
        Assert.Same(moved, again);
    }

    [Fact]
    public void TransferRef_Miss_Reaches_Downstream_Error_Not_Resolution_Error()
    {
        // Resolution must succeed (def found, closed, invoked); the miss
        // surfaces downstream as the missing InterfaceNotSupportedException
        // type (no PDN in testhost), never as open-def-missing/not-loaded.
        // This is the exact gen40 failure split: resolution vs invocation.
        var source = new PaintDotNet.ComponentModel.FakeTransferSource { Moved = null };
        var ex = Assert.Throws<System.InvalidOperationException>(() =>
            CpuPaint.CastOrTransferRefRuntime(source, typeof(PaintDotNet.ComponentModel.FakeTransferTarget)));
        Assert.DoesNotContain("open def missing", ex.Message);
        Assert.DoesNotContain("ObjectRefExtensions not loaded", ex.Message);
        Assert.Contains("InterfaceNotSupported", ex.Message);
    }
}
