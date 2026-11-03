using Mono.Cecil;

namespace DesktopWasm.Packaging.Tests;

/// <summary>
/// Builds tiny fixture assemblies with Cecil: real assembly references
/// plus types whose base types, signatures, and events use framework
/// references from one or both stacks. Fixtures are layout-real (files on
/// disk) so closure and hash tests observe authentic bytes.
/// </summary>
internal static class FixtureBuilder
{
    internal sealed record FixtureRef(string Assembly, string Namespace, string Name);

    internal static AssemblyNameReference Ref(string name, string version = "9.0.0.0") =>
        new(name, new Version(version));

    internal static string WriteAssembly(
        string directory,
        string fileName,
        string version,
        IEnumerable<AssemblyNameReference> references,
        Action<ModuleDefinition>? defineTypes = null)
    {
        Directory.CreateDirectory(directory);
        var name = new AssemblyNameDefinition(Path.GetFileNameWithoutExtension(fileName), new Version(version));
        var assembly = AssemblyDefinition.CreateAssembly(name, Path.GetFileNameWithoutExtension(fileName), ModuleKind.Dll);
        var module = assembly.MainModule;
        foreach (var reference in references)
            module.AssemblyReferences.Add(reference);
        defineTypes?.Invoke(module);
        var path = Path.Combine(directory, fileName);
        assembly.Write(path);
        assembly.Dispose();
        return path;
    }

    internal static TypeReference TypeRef(ModuleDefinition module, string assembly, string ns, string name)
    {
        var scope = module.AssemblyReferences.First(r => r.Name.Equals(assembly, StringComparison.OrdinalIgnoreCase));
        return new TypeReference(ns, name, module, scope);
    }

    /// <summary>WinForms control shape: Form base, DialogResult signature, FormClosed-style event.</summary>
    internal static void DefineWinFormsControl(ModuleDefinition module)
    {
        var form = TypeRef(module, "System.Windows.Forms", "System.Windows.Forms", "Form");
        var type = new TypeDefinition("Fixtures", "MixedForm", TypeAttributes.Public | TypeAttributes.Class, form);
        module.Types.Add(type);

        var dialogResult = TypeRef(module, "System.Windows.Forms", "System.Windows.Forms", "DialogResult");
        var show = new MethodDefinition("ShowDialog", MethodAttributes.Public | MethodAttributes.Virtual, dialogResult);
        type.Methods.Add(show);

        var handler = new TypeReference("System", "EventHandler", module, module.TypeSystem.CoreLibrary);
        var evt = new EventDefinition("FormClosed", EventAttributes.None, handler);
        type.Events.Add(evt);
    }

    /// <summary>WPF visual shape: Window base, RoutedEventHandler event, Screen (shim-overlapped) usage.</summary>
    internal static void DefineWpfVisual(ModuleDefinition module)
    {
        var window = TypeRef(module, "PresentationFramework", "System.Windows", "Window");
        var type = new TypeDefinition("Fixtures", "MixedWindow", TypeAttributes.Public | TypeAttributes.Class, window);
        module.Types.Add(type);

        var routed = TypeRef(module, "PresentationFramework", "System.Windows", "RoutedEventHandler");
        var evt = new EventDefinition("Loaded", EventAttributes.None, routed);
        type.Events.Add(evt);

        var screen = TypeRef(module, "System.Windows.Forms", "System.Windows.Forms", "Screen");
        var field = new FieldDefinition("_screen", FieldAttributes.Private, screen);
        type.Fields.Add(field);
    }

    /// <summary>Mixed assembly entirely in memory: one WinForms control plus
    /// one WPF visual, with a SystemEvents subscription both maps claim.</summary>
    internal static AssemblyDefinition CreateMixedModule()
    {
        var name = new AssemblyNameDefinition("MixedApp", new Version(1, 0, 0, 0));
        var assembly = AssemblyDefinition.CreateAssembly(name, "MixedApp", ModuleKind.Dll);
        var module = assembly.MainModule;
        module.AssemblyReferences.Add(Ref("System.Windows.Forms"));
        module.AssemblyReferences.Add(Ref("PresentationFramework"));
        module.AssemblyReferences.Add(Ref("Microsoft.Win32.SystemEvents"));
        DefineWinFormsControl(module);
        DefineWpfVisual(module);
        var handler = TypeRef(module, "Microsoft.Win32.SystemEvents", "Microsoft.Win32", "UserPreferenceChangedEventHandler");
        var control = module.Types.First(t => t.Name == "MixedForm");
        control.Events.Add(new EventDefinition("PrefChanged", EventAttributes.None, handler));
        return assembly;
    }

    internal static string TempDir(string name)
    {
        var dir = Path.Combine(Path.GetTempPath(), "dw-packaging", name + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
