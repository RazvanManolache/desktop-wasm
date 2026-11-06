using System.Reflection;
using WfWasm.CanvasHost;
using WfWasm.Renderer;
using Xunit;

// TRANSIENT trigger probe (delete after use): is the GC-test abort driven by
// forced collections (variant A) or by repeated strokes (variant B)?
// Progress goes to canonical runs/ probe files because console capture is lost on FailFast.
namespace WfWasm.CanvasHost.Tests;

[Collection("Paint")]
public sealed class PaintTriggerProbeTests
{
    private readonly WinFormsAppHost _host;

    // TMPMOVE canonical-only: probe progress goes to the repo runs tree.
    private static string RunsDir =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".artifacts", "tmp", "runs", "canvashost-probes"));
    private static void ProbeAppend(string file, string text)
    {
        Directory.CreateDirectory(RunsDir);
        File.AppendAllText(Path.Combine(RunsDir, file), text);
    }

    public PaintTriggerProbeTests(PaintHostFixture fx)
    {
        _host = fx.Host;
    }

    private static T InvokePrivate<T>(object target, string name, object?[]? args = null)
    {
        var m = target.GetType().GetMethod(name,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
        Assert.NotNull(m);
        return (T)m.Invoke(target, args)!;
    }

    private void CycleToBrush()
    {
        var host = _host;
        host.Paint(new RecordingRenderer());
        for (int i = 0; i < 20; i++)
        {
            string tool = InvokePrivate<string>(host, "GetActiveDocToolName");
            if (tool.StartsWith("PaintBrushTool", StringComparison.Ordinal))
                return;
            InvokePrivate<object?>(host, "CycleNextTool");
            host.Paint(new RecordingRenderer());
            Thread.Sleep(25);
        }
        Assert.Fail("brush tool not reached in probe");
    }

    [Fact]
    public void ProbeA_GcWithoutStrokes()
    {
        CycleToBrush();
        for (int i = 0; i < 10; i++)
        {
            ProbeAppend("probe-a.txt", $"iter {i} collect{System.Environment.NewLine}");
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            ProbeAppend("probe-a.txt", $"iter {i} paint{System.Environment.NewLine}");
            _host.Paint(new RecordingRenderer());
        }
        ProbeAppend("probe-a.txt", $"DONE{System.Environment.NewLine}");
    }

    [Fact]
    public void ProbeB_StrokesWithoutForcedGc()
    {
        CycleToBrush();
        for (int stroke = 0; stroke < 10; stroke++)
        {
            ProbeAppend("probe-b.txt", $"stroke {stroke} start{System.Environment.NewLine}");
            _host.Paint(new RecordingRenderer());
            dynamic bounds = _host.GetType()
                .GetField("_documentPageBounds", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(_host)!;
            int x0 = bounds.X + bounds.Width / 3;
            int y0 = bounds.Y + bounds.Height / 3;
            _host.OnMouseDown(x0, y0);
            for (int i = 1; i <= 12; i++)
                _host.OnMouseMove(x0 + (i * bounds.Width) / 36, y0 + (i * bounds.Height) / 72);
            _host.OnMouseUp(x0 + bounds.Width / 3, y0 + bounds.Height / 6);
            ProbeAppend("probe-b.txt", $"stroke {stroke} done{System.Environment.NewLine}");
        }
        ProbeAppend("probe-b.txt", $"DONE{System.Environment.NewLine}");
    }
}
