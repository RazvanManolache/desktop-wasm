using Mono.Cecil;

namespace DesktopWasm.Convert.Tests;

/// <summary>
/// Property/event/generic-constraint positions rewrite onto the engine like
/// every other position (converted OxyPlot property blobs and the
/// CanvasRenderContext FrameworkElement constraint shipped stale scopes and
/// only broke at first touch). Stale framework names under any other scope
/// fail the build instead.
/// </summary>
public sealed class MetadataRewriteTests
{
    private readonly string _workDir = Path.Combine(Path.GetTempPath(), "convert-metadata-" + Guid.NewGuid().ToString("N"));

    private static TypeReference FrameworkRef(ModuleDefinition module, string ns, string name) =>
        new(ns, name, module, new AssemblyNameReference("PresentationFramework", new Version(4, 0, 0, 0)));

    private string WriteMetadataFixture()
    {
        var asm = AssemblyDefinition.CreateAssembly(
            new AssemblyNameDefinition("MetadataApp", new Version(1, 0, 0, 0)), "MetadataApp", ModuleKind.Dll);
        var module = asm.MainModule;
        var fw = new AssemblyNameReference("PresentationFramework", new Version(4, 0, 0, 0));
        module.AssemblyReferences.Add(fw);

        var host = new TypeDefinition("MetadataApp", "Host",
            Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Class,
            new TypeReference("System.Windows.Controls", "Control", module, fw));
        var templateProp = new PropertyDefinition("Template", Mono.Cecil.PropertyAttributes.None,
            new TypeReference("System.Windows.Controls", "ControlTemplate", module, fw));
        host.Properties.Add(templateProp);
        var clickEvent = new EventDefinition("Click", Mono.Cecil.EventAttributes.None,
            new TypeReference("System.Windows", "RoutedEventHandler", module, fw));
        host.Events.Add(clickEvent);
        var create = new MethodDefinition("Create", Mono.Cecil.MethodAttributes.Public,
            module.TypeSystem.Object);
        host.Methods.Add(create);
        var gp = new Mono.Cecil.GenericParameter("T", create);
        gp.Attributes = Mono.Cecil.GenericParameterAttributes.ReferenceTypeConstraint | Mono.Cecil.GenericParameterAttributes.DefaultConstructorConstraint;
        gp.Constraints.Add(new Mono.Cecil.GenericParameterConstraint(
            new TypeReference("System.Windows", "FrameworkElement", module, fw)));
        create.GenericParameters.Add(gp);
        create.ReturnType = gp;
        create.Body.GetILProcessor().Emit(Mono.Cecil.Cil.OpCodes.Ldnull);
        create.Body.GetILProcessor().Emit(Mono.Cecil.Cil.OpCodes.Ret);
        module.Types.Add(host);

        var path = Path.Combine(_workDir, "MetadataApp.dll");
        Directory.CreateDirectory(_workDir);
        asm.Write(path);
        return path;
    }

    private string WriteStaleFixture()
    {
        var asm = AssemblyDefinition.CreateAssembly(
            new AssemblyNameDefinition("StaleApp", new Version(1, 0, 0, 0)), "StaleApp", ModuleKind.Dll);
        var module = asm.MainModule;
        var fake = new AssemblyNameReference("StaleAppLib", new Version(1, 0, 0, 0));
        module.AssemblyReferences.Add(fake);
        var host = new TypeDefinition("StaleApp", "Host",
            Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Class,
            module.TypeSystem.Object);
        host.Properties.Add(new PropertyDefinition("Template", Mono.Cecil.PropertyAttributes.None,
            new TypeReference("System.Windows.Controls", "ControlTemplate", module, fake)));
        module.Types.Add(host);
        var path = Path.Combine(_workDir, "StaleApp.dll");
        Directory.CreateDirectory(_workDir);
        asm.Write(path);
        return path;
    }

    private string NewSourceDir()
    {
        var dir = Path.Combine(_workDir, "src-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "Dummy.xaml"),
            "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"/>");
        return dir;
    }

    [Fact]
    public void PropertyEventAndConstraintRewriteOntoEngine()
    {
        var path = WriteMetadataFixture();
        var (ok, gaps, receipt) = AssemblyConverter.TryConvert(
            path, NewSourceDir(), Path.Combine(_workDir, "out"));
        Assert.True(ok, string.Join("; ", gaps.Select(g => $"{g.Kind}: {g.Detail}")));
        Assert.NotNull(receipt);
        using var converted = AssemblyDefinition.ReadAssembly(
            Path.Combine(_workDir, "out", "MetadataApp.dll"));
        var host = converted.MainModule.Types.Single(t => t.Name == "Host");
        Assert.Equal("DesktopWasm.Wpf",
            ((AssemblyNameReference)host.Properties.Single(p => p.Name == "Template").PropertyType.Scope).Name);
        Assert.Equal("DesktopWasm.Wpf",
            ((AssemblyNameReference)host.Events.Single(e => e.Name == "Click").EventType.Scope).Name);
        var create = host.Methods.Single(m => m.Name == "Create");
        Assert.Equal("DesktopWasm.Wpf",
            ((AssemblyNameReference)create.GenericParameters[0].Constraints[0].ConstraintType.Scope).Name);
    }

    [Fact]
    public void StaleFrameworkReferenceGaps()
    {
        var path = WriteStaleFixture();
        var (ok, gaps, _) = AssemblyConverter.TryConvert(
            path, NewSourceDir(), Path.Combine(_workDir, "out-stale"));
        Assert.False(ok);
        Assert.Contains(gaps, g => g.Kind == "stale-framework-reference");
    }
}
