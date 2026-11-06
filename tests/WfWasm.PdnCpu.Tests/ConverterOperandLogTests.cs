using System;
using System.IO;
using System.Runtime.CompilerServices;
using WfWasm.PdnCpu;

// FP-dw-vendor-undolog-0338 (WC-0338): bridge operand logging for the undo
// converter path. Runtime handed back with exact evidence: the undo Ctrl+Z
// death is a native zero-page write right after the
// `CreateFormatConverter<Bgra32>(IBitmapSource)` "ok call" line with zero
// managed frames, and the host cannot see vendor locals. This file pins the
// new `CpuFx-bridge-operands:` lines: per-arg runtime type + identity hash +
// safe-reflected facts (dims, format, disposed flag, lock buffer, nullness),
// a transfer->converter identity link (`seen=`), and a fail-safe guarantee
// (hostile members never break the call). PDN assemblies do not load under
// the test host, so local fakes carry the proof (ClosedGenericStaticBridge
// convention). Console capture shares the ConsoleCapture collection with
// ProbeAdapterLoadsTests: Console.Out is process-global, so the two
// swappers must never run in parallel.
[CollectionDefinition("ConsoleCapture")]
public sealed class ConsoleCaptureCollection { }

[Collection("ConsoleCapture")]
public sealed class ConverterOperandLogTests
{
    private sealed class FakeBgra32 { }

    private sealed class FakeColorContext
    {
        public string Name => "sRGB";
    }

    private sealed class FakeBitmapLock : PaintDotNet.ComponentModel.IFakeObjectRef
    {
        public int Width => 64;
        public int Height => 48;
        public string PixelFormat => "Bgra32";
        public bool IsDisposed => false;
    }

    private sealed class FakeThrowingSource
    {
        public int Width => throw new ObjectDisposedException("surface");
        public int Height => 48;
        public bool IsDisposed => true;
    }

    private static class FakeImaging
    {
        public static string CreateFormatConverter<TPixel>(
            FakeBitmapLock source, FakeColorContext ctx)
            => "converted:" + typeof(TPixel).Name + ":"
                + (source is null ? "nullsrc" : source.Width + "x" + source.Height);

        public static string CreateFormatConverter<TPixel>(
            FakeThrowingSource source, FakeColorContext ctx)
            => "converted-throwing:" + (source is null ? "nullsrc" : "sized");

        public static T Identity<T>(T a) => a;
    }

    private static string Aqn(Type t) => t.AssemblyQualifiedName!;

    private static string Capture(Action act)
    {
        var sw = new StringWriter();
        TextWriter old = Console.Out;
        Console.SetOut(sw);
        try
        {
            act();
        }
        finally
        {
            Console.SetOut(old);
        }
        return sw.ToString();
    }

    private static object? ConvertLock(object? source)
    {
        string pixel = Aqn(typeof(FakeBgra32));
        string lockT = Aqn(typeof(FakeBitmapLock));
        string throwT = Aqn(typeof(FakeThrowingSource));
        string ctxT = Aqn(typeof(FakeColorContext));
        string srcT = source is null ? lockT
            : source is FakeThrowingSource ? throwT : lockT;
        return CpuPaint.InvokeClosedGenericStatic(
            typeof(FakeImaging).FullName!,
            typeof(FakeImaging).Assembly.GetName().Name!,
            "CreateFormatConverter",
            new[] { pixel }, new[] { srcT, ctxT },
            new object?[] { source, new FakeColorContext() });
    }

    [Fact]
    public void ConverterCall_Logs_Operand_Facts()
    {
        var source = new FakeBitmapLock();
        string? result = null;
        string output = Capture(() => { result = (string?)ConvertLock(source); });
        Assert.Equal("converted:FakeBgra32:64x48", result);
        Assert.Contains("CpuFx-bridge-operands:", output);
        Assert.Contains(typeof(FakeBitmapLock).FullName!, output);
        Assert.Contains("W=64", output);
        Assert.Contains("H=48", output);
        Assert.Contains("Fmt=Bgra32", output);
        Assert.Contains("disposed=False", output);
        Assert.Contains(RuntimeHelpers.GetHashCode(source).ToString("X8"), output);
    }

    [Fact]
    public void ConverterCall_Null_Source_Logs_Nullness()
    {
        string? result = null;
        string output = Capture(() => { result = (string?)ConvertLock(null); });
        Assert.Equal("converted:FakeBgra32:nullsrc", result);
        Assert.Contains("CpuFx-bridge-operands:", output);
        Assert.Contains("arg0=null", output);
    }

    [Fact]
    public void ConverterCall_Throwing_Member_Does_Not_Break_Call()
    {
        // Fail-safe pin: a disposed surface whose Width getter throws must
        // still convert (and the log names the throwing member type).
        string? result = null;
        string output = Capture(
            () => { result = (string?)ConvertLock(new FakeThrowingSource()); });
        Assert.Equal("converted-throwing:sized", result);
        Assert.Contains("CpuFx-bridge-operands:", output);
        Assert.Contains("throw:ObjectDisposedException", output);
        Assert.Contains("disposed=True", output);
    }

    [Fact]
    public void NonConverterCall_Emits_No_Operand_Lines()
    {
        // Noise gate pin: ordinary bridge calls keep the old log shape only.
        string i32 = Aqn(typeof(int));
        string output = Capture(() =>
            CpuPaint.InvokeClosedGenericStatic(
                typeof(FakeImaging).FullName!,
                typeof(FakeImaging).Assembly.GetName().Name!,
                "Identity",
                new[] { i32 }, new[] { i32 },
                new object?[] { 7 }));
        Assert.DoesNotContain("bridge-operands", output);
    }

    [Fact]
    public void Transfer_Then_Converter_Links_Identity()
    {
        // Lifetime link: the converter log names the earlier transfer when
        // the same surface instance flows transfer -> converter call.
        var moved = new FakeBitmapLock();
        var carrier = new PaintDotNet.ComponentModel.FakeTransferSource
        {
            Moved = moved
        };
        string output = Capture(() =>
        {
            object got = CpuPaint.CastOrTransferRefRuntime(
                carrier, typeof(FakeBitmapLock));
            Assert.Same(moved, got);
            ConvertLock(moved);
        });
        string hash = RuntimeHelpers.GetHashCode(moved).ToString("X8");
        Assert.Contains("transfer", output);
        Assert.Contains(hash, output);
        Assert.Contains("seen=transfer", output);
    }
}
