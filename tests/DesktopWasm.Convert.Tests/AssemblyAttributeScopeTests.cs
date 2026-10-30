using Mono.Cecil;

namespace DesktopWasm.Convert.Tests;

/// <summary>
/// Slice-145 converter attribute-scope fix: assembly and module custom
/// attributes are never rewritten by the live-graph pass, so an attribute
/// type scoped to a pruned convertible assembly (the System.Xaml
/// XmlnsDefinition/XmlnsPrefix rows every XAML library carries) shipped with
/// its scope silently re-pointed by the writer onto an unrelated live
/// reference (observed: System.Collections), breaking at first touch
/// (converted HandyControl's Lang resource lookup enumerates assembly
/// attributes and throws on the dangling row, stopping staged theme boot at
/// BAML+0x19DCE). Mapped attribute types remap onto the engine like every
/// other position; the documented inert rows drop with an adaptation;
/// anything else under a convertible source scope is a precise gap so
/// conversion fails loudly instead of shipping a mis-scoped row.
/// </summary>
public sealed class AssemblyAttributeScopeTests
{
    private readonly string _workDir = Path.Combine(Path.GetTempPath(), "convert-attrscope-" + Guid.NewGuid().ToString("N"));

    private static CustomAttribute InertXamlMarker(ModuleDefinition module, string name)
    {
        var xaml = new AssemblyNameReference("System.Xaml", new Version(4, 0, 0, 0));
        module.AssemblyReferences.Add(xaml);
        var attributeType = new TypeReference("System.Windows.Markup", name, module, xaml);
        var ctor = new MethodReference(".ctor", module.TypeSystem.Void, attributeType) { HasThis = true };
        ctor.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));
        ctor.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));
        var attr = new CustomAttribute(ctor);
        attr.ConstructorArguments.Add(new CustomAttributeArgument(module.TypeSystem.String, "http://schemas.handycontrol.org/winfx"));
        attr.ConstructorArguments.Add(new CustomAttributeArgument(module.TypeSystem.String, "HandyControl.Controls"));
        return attr;
    }

    private string WriteFixture(Action<ModuleDefinition> shape, string name = "AttrScopeApp")
    {
        var asm = AssemblyDefinition.CreateAssembly(
            new AssemblyNameDefinition(name, new Version(1, 0, 0, 0)), name, ModuleKind.Dll);
        var module = asm.MainModule;
        var host = new TypeDefinition(name, "Host",
            Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Class,
            module.TypeSystem.Object);
        module.Types.Add(host);
        shape(module);
        var path = Path.Combine(_workDir, name + ".dll");
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

    private static string ScopeOf(CustomAttribute attr)
    {
        var type = attr.AttributeType;
        while (type is TypeSpecification spec)
            type = spec.ElementType;
        return type?.Scope is AssemblyNameReference scope ? scope.Name : "?";
    }

    [Fact]
    public void XamlNamespaceMarkersDropInsteadOfMisscoping()
    {
        var path = WriteFixture(module =>
        {
            module.Assembly.CustomAttributes.Add(InertXamlMarker(module, "XmlnsDefinitionAttribute"));
            module.Assembly.CustomAttributes.Add(InertXamlMarker(module, "XmlnsPrefixAttribute"));
        });
        var (ok, gaps, receipt) = AssemblyConverter.TryConvert(
            path, NewSourceDir(), Path.Combine(_workDir, "out"));
        Assert.True(ok, string.Join("; ", gaps.Select(g => $"{g.Kind}: {g.Detail}")));
        Assert.NotNull(receipt);
        Assert.Contains(receipt.Adaptations, a => a.Contains("XmlnsDefinitionAttribute", StringComparison.Ordinal));
        Assert.Contains(receipt.Adaptations, a => a.Contains("XmlnsPrefixAttribute", StringComparison.Ordinal));
        using var converted = AssemblyDefinition.ReadAssembly(Path.Combine(_workDir, "out", "AttrScopeApp.dll"));
        Assert.DoesNotContain(converted.MainModule.AssemblyReferences, r => r.Name == "System.Xaml");
        var leftovers = converted.CustomAttributes
            .Concat(converted.MainModule.CustomAttributes)
            .Where(a => a.AttributeType.FullName.Contains("Xmlns", StringComparison.Ordinal))
            .ToList();
        Assert.Empty(leftovers);
    }

    [Fact]
    public void SelfScopedAssemblyAttributesAreKept()
    {
        var path = WriteFixture(module =>
        {
            var marker = new TypeDefinition("AttrScopeApp", "ScopeMarker",
                Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Class,
                module.TypeSystem.Object);
            module.Types.Add(marker);
            var ctor = new MethodReference(".ctor", module.TypeSystem.Void, marker) { HasThis = true };
            var attr = new CustomAttribute(ctor);
            module.Assembly.CustomAttributes.Add(attr);
            module.Assembly.CustomAttributes.Add(InertXamlMarker(module, "XmlnsDefinitionAttribute"));
        }, "AttrScopeKeep");
        var (ok, gaps, receipt) = AssemblyConverter.TryConvert(
            path, NewSourceDir(), Path.Combine(_workDir, "out-keep"));
        Assert.True(ok, string.Join("; ", gaps.Select(g => $"{g.Kind}: {g.Detail}")));
        Assert.NotNull(receipt);
        using var converted = AssemblyDefinition.ReadAssembly(Path.Combine(_workDir, "out-keep", "AttrScopeKeep.dll"));
        Assert.Contains(converted.CustomAttributes, a => a.AttributeType.Name == "ScopeMarker");
        Assert.DoesNotContain(converted.CustomAttributes,
            a => a.AttributeType.FullName.Contains("Xmlns", StringComparison.Ordinal));
    }

    [Fact]
    public void UnknownSourceScopedAssemblyAttributeGaps()
    {
        var path = WriteFixture(module =>
        {
            var fw = new AssemblyNameReference("PresentationFramework", new Version(4, 0, 0, 0));
            module.AssemblyReferences.Add(fw);
            var attributeType = new TypeReference("System.Windows", "NoSuchBehaviorAttribute", module, fw);
            var ctor = new MethodReference(".ctor", module.TypeSystem.Void, attributeType) { HasThis = true };
            module.Assembly.CustomAttributes.Add(new CustomAttribute(ctor));
        }, "AttrScopeGap");
        Assert.Null(WpfTypeMap.MapType("PresentationFramework", "System.Windows", "NoSuchBehaviorAttribute"));
        var (ok, gaps, _) = AssemblyConverter.TryConvert(
            path, NewSourceDir(), Path.Combine(_workDir, "out-gap"));
        Assert.False(ok);
        Assert.Contains(gaps, g => g.Kind == "unmapped-assembly-attribute" &&
            g.Detail.Contains("NoSuchBehaviorAttribute", StringComparison.Ordinal));
    }

    [Fact]
    public void MappedAssemblyAttributeRemapsOntoEngine()
    {
        // Synthetic shape pinning the remap branch: DependencyPropertyDescriptor
        // rides an explicit WindowsBase override onto the engine.
        Assert.NotNull(WpfTypeMap.MapType("WindowsBase", "System.ComponentModel", "DependencyPropertyDescriptor"));
        var path = WriteFixture(module =>
        {
            var fw = new AssemblyNameReference("WindowsBase", new Version(4, 0, 0, 0));
            module.AssemblyReferences.Add(fw);
            var attributeType = new TypeReference("System.ComponentModel", "DependencyPropertyDescriptor", module, fw);
            var ctor = new MethodReference(".ctor", module.TypeSystem.Void, attributeType) { HasThis = true };
            module.Assembly.CustomAttributes.Add(new CustomAttribute(ctor));
        }, "AttrScopeRemap");
        var (ok, gaps, receipt) = AssemblyConverter.TryConvert(
            path, NewSourceDir(), Path.Combine(_workDir, "out-remap"));
        Assert.True(ok, string.Join("; ", gaps.Select(g => $"{g.Kind}: {g.Detail}")));
        Assert.NotNull(receipt);
        Assert.Contains(receipt.Adaptations, a => a.Contains("Remapped assembly attribute", StringComparison.Ordinal));
        using var converted = AssemblyDefinition.ReadAssembly(Path.Combine(_workDir, "out-remap", "AttrScopeRemap.dll"));
        var remapped = Assert.Single(converted.CustomAttributes,
            a => a.AttributeType.Name == "DependencyPropertyDescriptor");
        Assert.Equal("DesktopWasm.Wpf", ScopeOf(remapped));
    }
}
