using DesktopWasm.Convert;
using Mono.Cecil;

namespace DesktopWasm.Convert.Tests;

public sealed class ApplicationPackagerTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Packages_reference_closure_including_cycles_but_ignores_unreferenced_folder_dlls(bool missing)
    {
        var work = Path.Combine(Path.GetTempPath(), "wpf-package-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            File.WriteAllText(Path.Combine(work, "View.xaml"), "<Window/>");
            WriteAssembly(Path.Combine(work, "Entry.dll"), "Entry", "Dependency");
            if (!missing) WriteAssembly(Path.Combine(work, "Dependency.dll"), "Dependency", "Entry");
            WriteAssembly(Path.Combine(work, "Unrelated.dll"), "Unrelated", null);
            var output = Path.Combine(work, "package");
            var package = ApplicationPackager.Prepare(Path.Combine(work, "Entry.dll"), work, work, output, "fixture");
            package.Validate("fixture", "wpf-convert-v1");
            Assert.DoesNotContain(package.Assemblies, a => a.Name == "Unrelated");
            if (missing)
            {
                Assert.Equal("conversion-failed", package.Status);
                Assert.Contains(package.Gaps, g => g.Kind == "missing-dependency" && g.Detail.Contains("Dependency"));
            }
            else
            {
                Assert.Equal("converted", package.Status);
                Assert.Equal(new[] { "Dependency", "Entry" }, package.Assemblies.Select(a => a.Name).Order());
                Assert.All(package.Assemblies, a => Assert.True(File.Exists(Path.Combine(output, a.File))));
                Assert.Empty(package.Gaps);
                using var image = AssemblyDefinition.ReadAssembly(Path.Combine(output, package.Assemblies.Single(a => a.Name == "Entry").File));
                Assert.Contains(image.MainModule.Types, t => t.Name == "XamlOnlyView");
            }
        }
        finally { Directory.Delete(work, recursive: true); }
    }

    private static void WriteAssembly(string path, string name, string? dependency)
    {
        using var assembly = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition(name, new Version(1, 0)), name, ModuleKind.Dll);
        var module = assembly.MainModule;
        var type = new TypeDefinition("Fixture", "Data", TypeAttributes.Public, module.TypeSystem.Object);
        module.Types.Add(type);
        if (dependency is not null)
        {
            var reference = new AssemblyNameReference(dependency, new Version(1, 0));
            module.AssemblyReferences.Add(reference);
            type.Fields.Add(new FieldDefinition("Related", FieldAttributes.Public, new TypeReference("Fixture", "Data", module, reference)));
            if (name == "Entry") module.Types.Add(new TypeDefinition("Fixture", "XamlOnlyView", TypeAttributes.Public,
                new TypeReference("Fixture", "Data", module, reference)));
        }
        assembly.Write(path);
    }
}
