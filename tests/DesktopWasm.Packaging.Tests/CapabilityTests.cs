using Mono.Cecil;

namespace DesktopWasm.Packaging.Tests;

public sealed class CapabilityTests
{
    [Fact]
    public void WinFormsOnly_References_DetectWinForms()
    {
        var dir = FixtureBuilder.TempDir("wf");
        var path = FixtureBuilder.WriteAssembly(dir, "WfApp.dll", "1.0.0.0",
            [FixtureBuilder.Ref("System.Windows.Forms"), FixtureBuilder.Ref("System.Drawing")],
            FixtureBuilder.DefineWinFormsControl);

        using var assembly = AssemblyDefinition.ReadAssembly(path);
        var capabilities = JointCapabilities.Detect(assembly);

        Assert.Equal(FrameworkCapabilities.WinForms, capabilities);
    }

    [Fact]
    public void WpfOnly_References_DetectWpf()
    {
        var dir = FixtureBuilder.TempDir("wpf");
        var path = FixtureBuilder.WriteAssembly(dir, "WpfApp.dll", "1.0.0.0",
            [FixtureBuilder.Ref("PresentationFramework"), FixtureBuilder.Ref("WindowsBase"), FixtureBuilder.Ref("System.Xaml")]);

        using var assembly = AssemblyDefinition.ReadAssembly(path);
        var capabilities = JointCapabilities.Detect(assembly);

        Assert.Equal(FrameworkCapabilities.Wpf, capabilities);
    }

    [Fact]
    public void Mixed_References_DetectMixed_NotExclusiveChoice()
    {
        var dir = FixtureBuilder.TempDir("mixed");
        var path = FixtureBuilder.WriteAssembly(dir, "MixedApp.dll", "1.0.0.0",
            [FixtureBuilder.Ref("System.Windows.Forms"), FixtureBuilder.Ref("PresentationFramework"), FixtureBuilder.Ref("WindowsBase")],
            module =>
            {
                FixtureBuilder.DefineWinFormsControl(module);
                FixtureBuilder.DefineWpfVisual(module);
            });

        using var assembly = AssemblyDefinition.ReadAssembly(path);
        var capabilities = JointCapabilities.Detect(assembly);

        Assert.Equal(FrameworkCapabilities.Mixed, capabilities);
        Assert.True(capabilities.HasFlag(FrameworkCapabilities.WinForms));
        Assert.True(capabilities.HasFlag(FrameworkCapabilities.Wpf));
    }

    [Fact]
    public void Converted_Output_References_DetectNone()
    {
        var capabilities = JointCapabilities.Detect(["DesktopWasm.Wpf", "WfWasm.WinForms", "System.Runtime"]);

        Assert.Equal(FrameworkCapabilities.None, capabilities);
    }

    [Fact]
    public void Detection_IsCaseInsensitive()
    {
        Assert.Equal(FrameworkCapabilities.WinForms, JointCapabilities.Detect(["system.windows.forms"]));
        Assert.Equal(FrameworkCapabilities.Wpf, JointCapabilities.Detect(["presentationframework"]));
    }
}
