using Mono.Cecil;

namespace DesktopWasm.Packaging.Tests;

public sealed class PipelineTests
{
    private static ModuleDefinition ModuleWith(params string[] references)
    {
        var name = new AssemblyNameDefinition("Probe", new Version(1, 0, 0, 0));
        var assembly = AssemblyDefinition.CreateAssembly(name, "Probe", ModuleKind.Dll);
        foreach (var reference in references)
            assembly.MainModule.AssemblyReferences.Add(new AssemblyNameReference(reference, new Version(1, 0, 0, 0)));
        return assembly.MainModule;
    }

    [Fact]
    public void Stages_HaveFixedOrder_WinFormsBeforeWpf_ValidateLast()
    {
        Assert.Equal(
            [JointStage.ScanClosure, JointStage.RewriteWinForms, JointStage.RewriteWpf,
             JointStage.ValidateLiveGraph, JointStage.HashAndManifest],
            JointPipeline.OrderedStages);
    }

    [Fact]
    public void FullOrder_Passes()
    {
        Assert.Null(JointPipeline.CheckOrder(JointPipeline.OrderedStages));
    }

    [Fact]
    public void ValidateBeforeRewrites_IsOrderViolation()
    {
        var gap = JointPipeline.CheckOrder([JointStage.ScanClosure, JointStage.ValidateLiveGraph]);

        Assert.NotNull(gap);
        Assert.Equal(JointGap.OrderViolation, gap.Kind);
    }

    [Fact]
    public void OutOfOrder_IsOrderViolation()
    {
        var gap = JointPipeline.CheckOrder(
            [JointStage.ScanClosure, JointStage.RewriteWpf, JointStage.RewriteWinForms]);

        Assert.NotNull(gap);
        Assert.Equal(JointGap.OrderViolation, gap.Kind);
    }

    [Fact]
    public void ConvertedModule_IsIdempotent_NoDoubleRewrite()
    {
        using var module = ModuleWith("DesktopWasm.Wpf", "System.Runtime");

        Assert.True(JointPipeline.IsAlreadyConverted(module));
    }

    [Fact]
    public void ModuleWithRemainingSourceRefs_IsNotConverted()
    {
        using var module = ModuleWith("DesktopWasm.Wpf", "PresentationFramework");

        Assert.False(JointPipeline.IsAlreadyConverted(module));
    }

    [Fact]
    public void UnconvertedModule_IsNotConverted()
    {
        using var module = ModuleWith("System.Windows.Forms", "PresentationFramework");

        Assert.False(JointPipeline.IsAlreadyConverted(module));
    }
}
