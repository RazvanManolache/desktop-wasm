using DesktopWasm.Convert;
using DesktopWasm.Wpf;
using Mono.Cecil;

namespace DesktopWasm.Convert.Tests;

/// <summary>
/// Slice-55 regression: the slice-50 converter rewrite turned two
/// convertible ScreenToGif rows into failures. (1) Reading an attribute
/// blob carrying a framework enum (ScreenToGif.Controls.TextPath
/// Localizability rows, assembly-level ThemeInfo) threw
/// AssemblyResolutionException for PresentationCore/PresentationFramework
/// because the resolver never looked at the installed shared frameworks.
/// (2) ScreenToGif.Util.SimpleKeyGesture's
/// [System.Windows.Markup.ValueSerializerAttribute] (scoped to
/// System.ObjectModel on modern TFMs) tripped the new
/// stale-framework-reference gap with no engine redirect. Both are real
/// behavior: metadata resolves against the actual installed framework for
/// reading only (nothing ships), and the annotation maps onto the
/// WPF-identical engine marker.
/// </summary>
public sealed class ValueSerializerSliceTests : IDisposable
{
    private readonly string _workDir = Path.Combine(Path.GetTempPath(), "wpf-valueser-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_workDir, recursive: true); } catch { }
    }

    [Fact]
    public void MapCoversValueSerializerSlice()
    {
        // ScreenToGif.Util.SimpleKeyGesture carries this annotation naming
        // the framework KeyGestureValueSerializer; the attribute ships in
        // System.ObjectModel (forwarded from WindowsBase), so it needs the
        // explicit redirect (verified against the staged ScreenToGif.Util
        // binary plus live .NET reflection for the AttributeUsage shape).
        var mapped = WpfTypeMap.MapType("System.ObjectModel", "System.Windows.Markup", "ValueSerializerAttribute");
        Assert.NotNull(mapped);
        Assert.Equal(("DesktopWasm.Wpf", "DesktopWasm.Wpf", "ValueSerializerAttribute"), mapped.Value);
        var engineType = typeof(Wpf.Window).Assembly.GetType("DesktopWasm.Wpf.ValueSerializerAttribute");
        Assert.NotNull(engineType);
        Assert.True(engineType.IsSealed);
        Assert.NotNull(engineType.GetConstructor([typeof(Type)]));
        Assert.NotNull(engineType.GetConstructor([typeof(string)]));
        Assert.NotNull(engineType.GetProperty("ValueSerializerType"));
        Assert.NotNull(engineType.GetProperty("ValueSerializerTypeName"));
        Assert.Empty(WpfTypeMap.ValidateEngineTargets());
    }

    [Fact]
    public void ValueSerializer_attribute_rewrites_onto_engine()
    {
        // Fixture mimics SimpleKeyGesture: a type-level annotation scoped to
        // System.ObjectModel naming a serializer via typeof().
        var input = WriteValueSerializerFixture();
        var outputDir = Path.Combine(_workDir, "value-converted");
        var (ok, gaps, _) = AssemblyConverter.TryConvert(input, NewSourceDir(), outputDir);
        Assert.True(ok, string.Join("; ", gaps.Select(g => $"[{g.Kind}] {g.Detail}")));
        using var converted = AssemblyDefinition.ReadAssembly(Path.Combine(outputDir, "ValueSerApp.dll"));
        var type = converted.MainModule.Types.Single(t => t.Name == "Gesture");
        var attr = Assert.Single(type.CustomAttributes);
        Assert.Equal("ValueSerializerAttribute", attr.AttributeType.Name);
        Assert.Equal("DesktopWasm.Wpf", ((AssemblyNameReference)attr.AttributeType.Scope).Name);
    }

    [Fact]
    public void Framework_enum_attribute_blobs_convert_against_real_entry()
    {
        // Exact slice-55 regression on the real upstream binary: 3 attributes
        // (TextPath.FontFamily/FontSize Localizability, assembly-level
        // ThemeInfo) threw AssemblyResolutionException for PresentationCore/
        // PresentationFramework before the shared-framework fallback, failing
        // the whole entry with a rewrite-error. Bypasses the status gate in
        // the probe only: TryConvert reads the upstream binary, never the
        // staged closure.
        var root = FindRepoRoot();
        var entry = Path.Combine(root, "targets", "studio-inputs", "screen-to-gif",
            "ScreenToGif", "bin", "Release", "net9.0-windows7.0", "ScreenToGif.dll");
        var source = Path.Combine(root, "targets", "studio-inputs", "screen-to-gif", "ScreenToGif");
        Assert.True(File.Exists(entry), $"upstream entry missing: {entry}");
        var outputDir = Path.Combine(_workDir, "entry-converted");
        var (ok, gaps, receipt) = AssemblyConverter.TryConvert(entry, source, outputDir);
        if (!WindowsDesktopPackPresent())
        {
            Assert.False(ok);
            Assert.Contains(gaps, g => g.Kind == "rewrite-error" && g.Detail.Contains("PresentationCore"));
            return;
        }
        Assert.True(ok, string.Join("; ", gaps.Select(g => $"[{g.Kind}] {g.Detail}")));
        Assert.NotNull(receipt);
        using var converted = AssemblyDefinition.ReadAssembly(Path.Combine(outputDir, "ScreenToGif.dll"));
        var textPath = converted.MainModule.Types.Single(t => t.Name == "TextPath");
        var family = textPath.Properties.Single(p => p.Name == "FontFamily");
        // The framework enum metadata survives the round trip (name, ctor
        // shape, enum value readable during conversion).
        // Slice-73: LocalizabilityAttribute now has a real engine
        // counterpart, so the blob converts onto DesktopWasm.Wpf (no
        // fabrication: the map row is member-evidenced) with the enum value
        // preserved bit-for-bit against the upstream binary.
        var attr = Assert.Single(family.CustomAttributes, a => a.AttributeType.Name == "LocalizabilityAttribute");
        Assert.Equal("DesktopWasm.Wpf", attr.AttributeType.Namespace);
        Assert.Equal("DesktopWasm.Wpf", ((AssemblyNameReference)attr.AttributeType.Scope).Name);
        Assert.Equal("DesktopWasm.Wpf.LocalizationCategory",
            ((MethodReference)attr.Constructor).Parameters[0].ParameterType.FullName);
        // The enum value survives bit-for-bit (the probe-only resolver
        // reads the framework attribute type out of the desktop pack).
        Assert.Equal(
            ReadLocalizabilityCategory(entry),
            ReadLocalizabilityCategory(Path.Combine(outputDir, "ScreenToGif.dll")));
        // Slice-145: the assembly-level ThemeInfoAttribute no longer ships.
        // It used to pass through with its scope silently re-pointed by the
        // Cecil writer onto a surviving row (the same mis-scoping the
        // System.Xaml Xmlns rows had); nothing in the browser can resolve
        // it (the engine never reads assembly attributes and converted
        // code cannot name the type without gapping), so the converter
        // drops it with an adaptation instead of shipping a dangling row.
        Assert.DoesNotContain(converted.CustomAttributes,
            a => a.AttributeType.Name == "ThemeInfoAttribute");
        Assert.Contains(receipt.Adaptations,
            a => a.Contains("ThemeInfoAttribute", StringComparison.Ordinal));
    }

    private static int ReadLocalizabilityCategory(string path)
    {
        var resolver = new DefaultAssemblyResolver();
        var shared = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(typeof(object).Assembly.Location)!));
        var pack = Path.Combine(shared!, "Microsoft.WindowsDesktop.App");
        resolver.AddSearchDirectory(Directory.GetDirectories(pack).Order().Last());
        using var image = AssemblyDefinition.ReadAssembly(path, new ReaderParameters { AssemblyResolver = resolver });
        var textPath = image.MainModule.Types.Single(t => t.Name == "TextPath");
        var family = textPath.Properties.Single(p => p.Name == "FontFamily");
        var attribute = Assert.Single(family.CustomAttributes, a => a.AttributeType.Name == "LocalizabilityAttribute");
        var argument = Assert.Single(attribute.ConstructorArguments);
        return System.Convert.ToInt32(argument.Value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "src", "DesktopWasm.BrowserHost", "DesktopWasm.BrowserHost.csproj")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return directory.FullName;
    }

    private static bool WindowsDesktopPackPresent()
    {
        var runtimeFile = typeof(object).Assembly.Location;
        var shared = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(runtimeFile)!));
        return shared is not null && Directory.Exists(Path.Combine(shared, "Microsoft.WindowsDesktop.App"));
    }

    private string WriteValueSerializerFixture()
    {
        var asm = AssemblyDefinition.CreateAssembly(
            new AssemblyNameDefinition("ValueSerApp", new Version(1, 0, 0, 0)), "ValueSerApp", ModuleKind.Dll);
        var module = asm.MainModule;
        var scope = new AssemblyNameReference("System.ObjectModel", new Version(9, 0, 0, 0));
        module.AssemblyReferences.Add(scope);
        var attrType = new TypeReference("System.Windows.Markup", "ValueSerializerAttribute", module, scope);
        var systemType = new TypeReference("System", "Type", module, module.TypeSystem.Object.Scope);
        var ctor = new MethodReference(".ctor", module.TypeSystem.Void, attrType) { HasThis = true };
        ctor.Parameters.Add(new ParameterDefinition(systemType));
        var attr = new CustomAttribute(ctor);
        attr.ConstructorArguments.Add(new CustomAttributeArgument(
            systemType, new TypeReference("System.Windows.Input", "KeyGestureValueSerializer", module, scope)));
        var type = new TypeDefinition("ValueSerApp", "Gesture",
            TypeAttributes.Public | TypeAttributes.Class, module.TypeSystem.Object);
        type.CustomAttributes.Add(attr);
        module.Types.Add(type);
        Directory.CreateDirectory(_workDir);
        var path = Path.Combine(_workDir, "ValueSerApp.dll");
        asm.Write(path);
        return path;
    }

    private string NewSourceDir()
    {
        var dir = Path.Combine(_workDir, "src");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "Dummy.xaml"),
            "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"/>");
        return dir;
    }
}
