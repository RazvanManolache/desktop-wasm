using System.Text.Json;
using Mono.Cecil;
using WpfAppBoot = DesktopWasm.Wpf.WpfAppBoot;
using WpfPackageLoader = DesktopWasm.Wpf.ApplicationPackageLoader;

namespace DesktopWasm.Packaging.Tests;

/// <summary>
/// Real TestApp joint packaging over the pristine staged20 inputs at
/// inputs/mixed/avalondock: full closure lowering, loader-compatible
/// manifest, staged payload integrity, bridge/drawing/framework provider
/// identities and precise gaps. A dummy manifest around two binaries
/// cannot satisfy these tests: every packaged node is lowered from real
/// bytes, scope- and namespace-routed, existence-verified against the
/// real provider binaries, hashed and staged.
/// </summary>
public sealed class TestAppJointPackageTests
{
    private static string StagedDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DesktopWasm.slnx")))
            dir = dir.Parent;
        Assert.True(dir is not null, "Repository root (DesktopWasm.slnx) not found above the test binaries.");
        var staged = Path.Combine(dir.FullName, "inputs", "mixed", "avalondock");
        Assert.True(Directory.Exists(staged),
            $"Staged AvalonDock closure absent at '{staged}': stage the pristine TestApp closure there first (see provenance.json).");
        return staged;
    }

    private static IReadOnlyDictionary<string, string> ProviderPaths()
    {
        var bin = AppContext.BaseDirectory;
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [JointTestAppPackager.ForkAssembly] = Path.Combine(bin, "WfWasm.WinForms.dll"),
            [JointTestAppPackager.EngineAssembly] = Path.Combine(bin, "DesktopWasm.Wpf.dll"),
            [JointTestAppPackager.DrawingAssembly] = Path.Combine(bin, "WfWasm.Drawing.dll"),
        };
    }

    [Fact]
    public void MapVersion_MatchesLoaderContract()
    {
        Assert.Equal(WpfAppBoot.MapVersion, JointTestAppPackager.MapVersion);
    }

    [Fact]
    public void JointPackage_BuildsValidLoaderManifest_RealStaged20()
    {
        var work = FixtureBuilder.TempDir("testapp-joint-package");
        var result = JointTestAppPackager.Build(StagedDirectory(), Path.Combine(work, "pkg"), ProviderPaths());

        Assert.True(result.Manifest.Status == "converted",
            "Joint package must be converted, not synthesized: " +
            string.Join(" | ", result.Gaps.Select(g => $"{g.Assembly} [{g.Kind}]: {g.Detail}")));
        Assert.Empty(result.Gaps);
        result.Manifest.Validate(JointTestAppPackager.Slug, WpfAppBoot.MapVersion);

        // Full closure disposition: 11 staged nodes (TestApp + 10
        // AvalonDock libs) packaged; framework references observed in the
        // converted graph resolve from the bundle/bridge, never staged.
        // Microsoft.Bcl.AsyncInterfaces is staged but unreferenced by the
        // packaged graph (only the bundle-provided System.Text.Json names
        // it), so it is correctly absent everywhere.
        Assert.Equal(11, result.Converted.Count);
        Assert.Equal("TestApp", result.Manifest.EntryAssembly);
        Assert.Contains(result.Manifest.Assemblies, a => a.Name == "TestApp");
        Assert.DoesNotContain(result.Manifest.Assemblies, a => a.Name == "Microsoft.Bcl.AsyncInterfaces");
        Assert.DoesNotContain(result.Manifest.Assemblies, a => JointTestAppPackager.TargetRuntimeProvided.Contains(a.Name));
        Assert.Contains(result.FrameworkProvided, p => p.Name == "System.Text.Json");
        Assert.Contains(result.FrameworkProvided, p => p.Name == "WindowsFormsIntegration");

        // Manifest round-trips from disk and still validates: this is the
        // file shell serves and the loader fetches.
        var manifestPath = Path.Combine(result.OutputDirectory, "app-manifest.json");
        Assert.True(File.Exists(manifestPath), "app-manifest.json missing from package output.");
        var reloaded = JsonSerializer.Deserialize<DesktopWasm.Core.ApplicationPackage>(
            File.ReadAllText(manifestPath), JointTestAppPackager.JsonOptions);
        Assert.NotNull(reloaded);
        reloaded.Validate(JointTestAppPackager.Slug, WpfAppBoot.MapVersion);
        Assert.Equal("converted", reloaded.Status);
        Assert.Empty(reloaded.Gaps);

        // Every staged payload matches its manifest hash; no duplicates,
        // no extras beyond manifest+receipt+provenance.
        foreach (var assembly in reloaded.Assemblies)
        {
            var path = Path.Combine(result.OutputDirectory, assembly.File);
            Assert.True(File.Exists(path), $"Staged payload missing for '{assembly.Name}'.");
            Assert.Equal(assembly.Sha256, JointClosure.HashOf(path), StringComparer.OrdinalIgnoreCase);
        }
        var payloadFiles = Directory.GetFiles(result.OutputDirectory, "*.bin").Select(Path.GetFileName).OfType<string>().ToHashSet<string>();
        Assert.Equal(reloaded.Assemblies.Length, payloadFiles.Count);

        // Payload identity through the real verifier: exact-name copies of
        // the staged hashed payloads verify against the manifest closure
        // (same name, same bytes), one payload per node.
        Assert.Empty(VerifyStagedPayload(result, reloaded));
    }

    [Fact]
    public void ConvertedBytes_RouteBothFrameworks_WindowsFormsHostUntouched()
    {
        var work = FixtureBuilder.TempDir("testapp-joint-routing");
        var result = JointTestAppPackager.Build(StagedDirectory(), Path.Combine(work, "pkg"), ProviderPaths());
        Assert.Equal("converted", result.Manifest.Status);

        foreach (var node in result.Converted)
        {
            var path = Path.Combine(result.OutputDirectory, node.File);
            using var assembly = AssemblyDefinition.ReadAssembly(path);
            var module = assembly.MainModule;
            // No convertible source scope survives anywhere in the graph
            // (positions, bodies, attributes, generic constraints).
            Assert.Empty(JointTestAppPackager.AuditConvertedScopes(module));
            // Identity preserved: same name and version, never re-keyed.
            Assert.Equal(node.Name, assembly.Name.Name);
            if (node.ForkRetargets == 0 && node.WpfRetargets == 0 && node.DrawingRetargets == 0)
                Assert.Equal(node.InputSha256, node.OutputSha256, StringComparer.OrdinalIgnoreCase);
        }

        var entry = result.Converted.First(c => c.Name == "TestApp");
        Assert.True(entry.ForkRetargets > 0, "WinForms fork pass retargeted nothing on real mixed TestApp bytes.");
        Assert.True(entry.WpfRetargets > 0, "WPF pass retargeted nothing on real mixed TestApp bytes.");
        Assert.True(entry.DrawingRetargets > 0, "Drawing provider pass retargeted nothing on real mixed TestApp bytes.");
        var entryPath = Path.Combine(result.OutputDirectory, entry.File);
        using (var assembly = AssemblyDefinition.ReadAssembly(entryPath))
        {
            var module = assembly.MainModule;
            Assert.Contains(module.AssemblyReferences, r => r.Name == "WfWasm.WinForms");
            Assert.Contains(module.AssemblyReferences, r => r.Name == "DesktopWasm.Wpf");
            Assert.Contains(module.AssemblyReferences, r => r.Name == "WfWasm.Drawing");
            var mainWindow = module.Types.First(t => t.FullName == "TestApp.MainWindow");
            // WPF visuals ride the real engine contract (renamed namespace).
            Assert.Equal("DesktopWasm.Wpf", ((AssemblyNameReference)mainWindow.BaseType.Scope).Name);
            Assert.Equal("DesktopWasm.Wpf.Window", mainWindow.BaseType.FullName);
            var userControl = module.Types.First(t => t.FullName == "TestApp.UserControl1");
            Assert.Equal("WfWasm.WinForms", ((AssemblyNameReference)userControl.BaseType.Scope).Name);
            // The joint interface is never rebound by either pass: it still
            // names the GAC-only WindowsFormsIntegration scope, which the
            // runtime bridge resolves to the real host provider.
            var hostScope = WalkScopes(module)
                .Where(s => s.FullName == "System.Windows.Forms.Integration.WindowsFormsHost")
                .Select(s => s.Scope)
                .FirstOrDefault();
            Assert.Equal("WindowsFormsIntegration", hostScope);
            // Drawing members ride the concrete drawing provider (the
            // fork's own contract), not the net48 facade.
            var colorScope = WalkScopes(module)
                .Where(s => s.FullName == "System.Drawing.Color")
                .Select(s => s.Scope)
                .FirstOrDefault();
            Assert.Equal("WfWasm.Drawing", colorScope);
            // Entry point and resources survive lowering (BAML boot path).
            Assert.NotNull(assembly.EntryPoint);
            Assert.Contains(module.Resources, r => r.Name == "TestApp.g.resources");
        }

        // Idempotency on real converted bytes: re-running the composed
        // lowering retargets nothing and leaves routing stable.
        using (var assembly = AssemblyDefinition.ReadAssembly(entryPath))
        {
            var module = assembly.MainModule;
            var capabilities = JointCapabilities.Detect(module);
            var scopes = ProviderScopes(module);
            var typeSets = JointTestAppPackager.LoadProviderTypeSets(ProviderPaths());
            var (fork, wpf, drawing, gaps) = JointTestAppPackager.RetargetModule(
                module, capabilities, scopes.Fork, scopes.Engine, scopes.Drawing, typeSets);
            Assert.Equal(0, fork);
            Assert.Equal(0, wpf);
            Assert.Equal(0, drawing);
            Assert.Empty(gaps);
        }
    }

    [Fact]
    public async Task Loader_FetchAndHashVerify_RealPackage()
    {
        var work = FixtureBuilder.TempDir("testapp-joint-loader");
        var result = JointTestAppPackager.Build(StagedDirectory(), Path.Combine(work, "pkg"), ProviderPaths());
        Assert.Equal("converted", result.Manifest.Status);

        // CreateAsync fetches every manifest file and hash-verifies it:
        // success here is the loader contract (fetch + hash-verify).
        Task<byte[]> Fetch(string file) =>
            File.ReadAllBytesAsync(Path.Combine(result.OutputDirectory, file));
        using var loader = await WpfPackageLoader.CreateAsync(
            result.Manifest, JointTestAppPackager.Slug, Fetch);

        // The staged entry image loads with its preserved identity and a
        // resolvable entry point: the first loadable boundary. No guest
        // code executes here; guest boot stays with the browser harness
        // (verifier lane owns it).
        var entryFile = result.Manifest.Assemblies.First(a => a.Name == "TestApp").File;
        var entryImage = await Fetch(entryFile);
        var entryAssembly = System.Reflection.Assembly.Load(entryImage);
        Assert.Equal("TestApp", entryAssembly.GetName().Name);
        Assert.NotNull(entryAssembly.EntryPoint);
    }

    [Fact]
    public void Entry_EmbedsDerivedLooseAppXaml()
    {
        var work = FixtureBuilder.TempDir("testapp-joint-appxaml");
        var result = JointTestAppPackager.Build(StagedDirectory(), Path.Combine(work, "pkg"), ProviderPaths());
        Assert.Equal("converted", result.Manifest.Status);

        // The embedded name mirrors the loader's ResolveSource transform exactly.
        Assert.Equal("testapp;v5.0.2.0;component.app.xaml",
            JointTestAppPackager.LooseAppXamlName("TestApp", "5.0.2.0"));
        var entry = result.Converted.First(c => c.Name == "TestApp");
        Assert.Equal("testapp;v5.0.2.0;component.app.xaml", entry.LooseAppXaml);

        // The ShutdownMode policy is read from real app.baml bytes, never invented.
        var stagedBaml = ReadStagedAppBaml();
        Assert.Equal("OnMainWindowClose", JointTestAppPackager.ExtractShutdownMode(stagedBaml));
        Assert.Null(JointTestAppPackager.ExtractShutdownMode("no markers here"u8.ToArray()));
        Assert.Null(JointTestAppPackager.ExtractShutdownMode(
            System.Text.Encoding.ASCII.GetBytes("ShutdownMode OnMainWindowClose OnLastWindowClose")));

        // The converted entry image carries the derived loose form with identity + policy.
        var entryPath = Path.Combine(result.OutputDirectory, entry.File);
        using var assembly = AssemblyDefinition.ReadAssembly(entryPath);
        var loose = assembly.MainModule.Resources
            .OfType<Mono.Cecil.EmbeddedResource>()
            .FirstOrDefault(r => r.Name == "testapp;v5.0.2.0;component.app.xaml");
        Assert.NotNull(loose);
        var text = System.Text.Encoding.UTF8.GetString(loose.GetResourceData());
        Assert.Contains("x:Class=\"TestApp.App\"", text, StringComparison.Ordinal);
        Assert.Contains("ShutdownMode=\"OnMainWindowClose\"", text, StringComparison.Ordinal);
    }

    private static byte[] ReadStagedAppBaml()
    {
        using var assembly = AssemblyDefinition.ReadAssembly(Path.Combine(StagedDirectory(), "TestApp.exe"));
        var resources = assembly.MainModule.Resources
            .OfType<Mono.Cecil.EmbeddedResource>()
            .First(r => r.Name.EndsWith(".g.resources", StringComparison.OrdinalIgnoreCase));
        using var reader = new System.Resources.ResourceReader(new MemoryStream(resources.GetResourceData()));
        foreach (System.Collections.DictionaryEntry entry in reader)
        {
            if ((entry.Key as string)?.EndsWith("app.baml", StringComparison.OrdinalIgnoreCase) == true)
            {
                if (entry.Value is byte[] bytes) return bytes;
                if (entry.Value is Stream stream)
                {
                    using var copy = new MemoryStream();
                    stream.CopyTo(copy);
                    return copy.ToArray();
                }
            }
        }
        throw new Xunit.Sdk.XunitException("staged TestApp.g.resources has no app.baml");
    }

    [Fact]
    public void Receipt_RecordsProvidersAndPreciseGaps()
    {
        var work = FixtureBuilder.TempDir("testapp-joint-receipt");
        var result = JointTestAppPackager.Build(StagedDirectory(), Path.Combine(work, "pkg"), ProviderPaths());

        var receiptPath = Path.Combine(result.OutputDirectory, "testapp-joint.receipt.json");
        var provenancePath = Path.Combine(result.OutputDirectory, "PROVENANCE.json");
        Assert.True(File.Exists(receiptPath), "Joint receipt missing from package output.");
        Assert.True(File.Exists(provenancePath), "PROVENANCE.json missing from package output.");
        using var receipt = JsonDocument.Parse(File.ReadAllText(receiptPath));
        Assert.Equal("converted", receipt.RootElement.GetProperty("status").GetString());
        Assert.Empty(receipt.RootElement.GetProperty("gaps").EnumerateArray());
        var bridge = receipt.RootElement.GetProperty("bridgeProvider");
        Assert.Equal("WindowsFormsIntegration", bridge.GetProperty("assembly").GetString());
        Assert.Equal("DesktopWasm.Interop.WindowsFormsHostResolver", bridge.GetProperty("resolver").GetString());
        Assert.Equal("WfWasm.Drawing", receipt.RootElement.GetProperty("drawingProvider").GetProperty("assembly").GetString());
        Assert.Equal(11, receipt.RootElement.GetProperty("converted").EnumerateArray().Count());
        var testapp = receipt.RootElement.GetProperty("converted").EnumerateArray()
            .First(e => e.GetProperty("name").GetString() == "TestApp");
        Assert.Equal("testapp;v5.0.2.0;component.app.xaml", testapp.GetProperty("looseAppXaml").GetString());
        var frameworkNames = receipt.RootElement.GetProperty("frameworkProvided")
            .EnumerateArray().Select(e => e.GetProperty("name").GetString()).OfType<string>().ToHashSet<string>();
        foreach (var name in JointTestAppPackager.TargetRuntimeProvided)
            Assert.Contains(name, frameworkNames);
    }

    private static (AssemblyNameReference Fork, AssemblyNameReference Engine, AssemblyNameReference Drawing) ProviderScopes(ModuleDefinition module)
    {
        AssemblyNameReference Find(string name) =>
            module.AssemblyReferences.First(r => r.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        return (Find("WfWasm.WinForms"), Find("DesktopWasm.Wpf"), Find("WfWasm.Drawing"));
    }

    private static IReadOnlyList<JointGap> VerifyStagedPayload(
        JointTestAppPackager.JointPackageResult result, DesktopWasm.Core.ApplicationPackage manifest)
    {
        // The hashed loader layout (<Name>.<sha>.bin) is exact-name staged
        // aside so the real VerifyPayload sees one payload per node with
        // matching bytes; version is carried by the closure, not the file.
        var stage = FixtureBuilder.TempDir("testapp-joint-verify");
        var nodes = new List<JointNode>();
        foreach (var assembly in manifest.Assemblies)
        {
            File.Copy(
                Path.Combine(result.OutputDirectory, assembly.File),
                Path.Combine(stage, assembly.Name + ".dll"));
            nodes.Add(new JointNode(assembly.Name, "0.0.0.0", assembly.Sha256, "", FrameworkCapabilities.None));
        }
        return JointClosure.VerifyPayload(new BuiltJointClosure(nodes, []), stage);
    }

    private static IEnumerable<(string FullName, string? Scope)> WalkScopes(ModuleDefinition module)
    {
        var stack = new Stack<TypeDefinition>(module.Types);
        while (stack.Count > 0)
        {
            var type = stack.Pop();
            foreach (var nested in type.NestedTypes) stack.Push(nested);
            foreach (var reference in Positions(type))
            {
                var unwrapped = Unwrap(reference);
                if (unwrapped is not null)
                    yield return (unwrapped.FullName, (unwrapped.Scope as AssemblyNameReference)?.Name);
            }
            // Drawing members live in method bodies, not type positions.
            foreach (var method in type.Methods)
            {
                if (method.Body is null) continue;
                foreach (var instruction in method.Body.Instructions)
                {
                    if (instruction.Operand is MemberReference member)
                    {
                        var unwrapped = Unwrap(member.DeclaringType);
                        if (unwrapped is not null)
                            yield return (unwrapped.FullName, (unwrapped.Scope as AssemblyNameReference)?.Name);
                    }
                }
            }
        }
    }

    private static IEnumerable<TypeReference?> Positions(TypeDefinition type)
    {
        yield return type.BaseType;
        foreach (var iface in type.Interfaces) yield return iface.InterfaceType;
        foreach (var field in type.Fields) yield return field.FieldType;
        foreach (var method in type.Methods)
        {
            yield return method.ReturnType;
            foreach (var parameter in method.Parameters) yield return parameter.ParameterType;
        }
        foreach (var property in type.Properties) yield return property.PropertyType;
        foreach (var evt in type.Events) yield return evt.EventType;
    }

    private static TypeReference? Unwrap(TypeReference? reference)
    {
        while (reference is TypeSpecification specification)
            reference = specification.ElementType;
        return reference;
    }
}
