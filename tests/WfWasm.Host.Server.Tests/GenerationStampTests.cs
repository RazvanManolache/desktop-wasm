namespace WfWasm.Host.Server.Tests;

// FP-dw-shell-20261104T2335-265 stamp adoption (b): pure-helper coverage for
// the serve-time generation stamp. The middleware itself is a thin caller;
// these tests pin the anchor contract against runtime-context.js.
public sealed class GenerationStampTests
{
    private const string Anchor = "Object.freeze({app, session, label,";

    private static string SampleJs()
        => "window.wfwasmRuntime = " + Anchor + " send, fail(message) {}});";

    [Fact]
    public void StampRuntimeContext_Injects_Generation_Into_Freeze_Literal()
    {
        string stamped = GenerationStamp.StampRuntimeContext(SampleJs(), "20261104T2144Zw6");

        Assert.Contains("generation: \"20261104T2144Zw6\"", stamped);
        Assert.Contains(Anchor, SampleJs());
    }

    [Fact]
    public void StampRuntimeContext_Without_Anchor_Returns_Input_Unchanged()
    {
        const string other = "window.wfwasmRuntime = Object.freeze({app});";

        Assert.Same(other, GenerationStamp.StampRuntimeContext(other, "20261104T2144Zw6"));
    }

    [Fact]
    public void StampRuntimeContext_Escapes_Generation_Literal()
    {
        string stamped = GenerationStamp.StampRuntimeContext(SampleJs(), "gen\"x");

        Assert.Contains("generation: \"gen\\u0022x\"", stamped);
    }

    [Fact]
    public void ResolveGeneration_Prefers_Generation_Txt()
    {
        string genRoot = NewDir();
        string webRoot = Directory.CreateDirectory(Path.Combine(genRoot, "winforms")).FullName;
        File.WriteAllText(Path.Combine(genRoot, "generation.txt"), "20261104T2144Zw6\n");
        File.WriteAllText(Path.Combine(genRoot, "deployment.json"), """{"generation":"other"}""");

        Assert.Equal("20261104T2144Zw6", GenerationStamp.ResolveGeneration(webRoot));
    }

    [Fact]
    public void ResolveGeneration_Falls_Back_To_Deployment_Json()
    {
        string genRoot = NewDir();
        string webRoot = Directory.CreateDirectory(Path.Combine(genRoot, "winforms")).FullName;
        File.WriteAllText(Path.Combine(genRoot, "deployment.json"), """{"generation":"20261104T2144Zw6"}""");

        Assert.Equal("20261104T2144Zw6", GenerationStamp.ResolveGeneration(webRoot));
    }

    [Fact]
    public void ResolveGeneration_Returns_Unknown_When_Nothing_Present()
    {
        string webRoot = Directory.CreateDirectory(Path.Combine(NewDir(), "winforms")).FullName;

        Assert.Equal("unknown", GenerationStamp.ResolveGeneration(webRoot));
    }

    private static string NewDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "wfgenstamp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
