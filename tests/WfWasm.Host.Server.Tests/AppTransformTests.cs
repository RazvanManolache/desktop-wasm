using WfWasm.Host.Server;

namespace WfWasm.Host.Server.Tests;

public sealed class AppTransformTests
{
    [Fact]
    public void Pristine_Assemblies_Serve_Byte_Identical()
    {
        // Assemblies the rewriter must not touch (Mono rejects
        // Cecil-processed metadata-only reference assemblies with
        // BadImageFormatException at Assembly.Load) serve pristine.
        // Regression: telerik-photo form-create failed loading
        // UIAutomationProvider even though its bytes were fetched.
        const string app = "telerik-photo";
        const string name = "UIAutomationProvider.dll";
        Assert.True(AppTransform.TryGetSpec(app, out AppTransform.AppSpec? maybe));
        Assert.True(maybe is not null);
        AppTransform.AppSpec spec = maybe!;
        string pristinePath = spec.SourceOverride.TryGetValue(name, out string? alt)
            ? alt
            : Path.Combine(spec.Folder, name);
        Assert.True(File.Exists(pristinePath), $"pristine missing: {pristinePath}");
        // Drop any cached transform of this file (an older revision may
        // have rewritten it): the cache is regenerable, the server and
        // later test runs rebuild it on demand.
        try
        {
            string dir = Path.Combine(AppTransform.CacheRoot, app);
            if (Directory.Exists(dir))
            {
                foreach (string f in Directory.GetFiles(dir, "*" + Path.GetFileNameWithoutExtension(name) + "*"))
                    File.Delete(f);
            }
        }
        catch { }
        byte[] served = AppTransform.TransformOrCached(app, name);
        Assert.Equal(File.ReadAllBytes(pristinePath), served);
    }

    [Theory]
    [InlineData("todo", "Microsoft Sans Serif", 8.25f)] // netfx 4.6 entry
    [InlineData("tictactoe", "Microsoft Sans Serif", 8.25f)] // netfx 4.8 entry
    [InlineData("calculator", "Segoe UI", 9f)] // net10 entry
    [InlineData("calendar", "Segoe UI", 9f)] // net8 entry
    [InlineData("no-such-app", "Segoe UI", 9f)] // unreadable -> modern fallback
    public void DefaultFontFor_Matches_Entry_TFM(string app, string name, float size)
    {
        // Autoscale divides the designer dims by the runtime default font's
        // metrics, so the browser must start with the same default the
        // desktop runtime would pick for the app's framework.
        (string actualName, float actualSize) = AppTransform.DefaultFontFor(app);
        Assert.Equal(name, actualName);
        Assert.Equal(size, actualSize);
    }

    [Theory]
    [InlineData("PresentationCore.dll")] // SimpleFontObject virtuals over FontFamily (vtable setup)
    [InlineData("PresentationFramework.dll")] // FixedDocumentDefaults.DefaultCellPadding is Thickness (field token)
    public void Erp_Wpf_Prereq_Serves_Pristine_ILOnly(string name)
    {
        // Telerik fixed-doc model needs WPF types in the closure or the
        // ERP viewer LoadDocument path dies in cctors (SimpleFontObject
        // vtable TypeLoad, PageObject field-token BadImageFormat). Each
        // pristine copy must be pure IL: a mixed-mode (C++/CLI) pristine
        // aborts the WASM interp.
        const string app = "telerik-erp";
        Assert.True(AppTransform.TryGetSpec(app, out AppTransform.AppSpec? maybe));
        Assert.True(maybe is not null);
        AppTransform.AppSpec spec = maybe!;
        Assert.Contains(name, spec.Pristine);
        string pristinePath = spec.SourceOverride.TryGetValue(name, out string? alt)
            ? alt
            : Path.Combine(spec.Folder, name);
        Assert.True(File.Exists(pristinePath), $"pristine missing: {pristinePath}");
        using (var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(pristinePath))
            Assert.True(asm.MainModule.Attributes.HasFlag(Mono.Cecil.ModuleAttributes.ILOnly),
                "mixed-mode pristine aborts the WASM interp");
        try
        {
            string dir = Path.Combine(AppTransform.CacheRoot, app);
            if (Directory.Exists(dir))
            {
                foreach (string f in Directory.GetFiles(dir, "*" + Path.GetFileNameWithoutExtension(name) + "*"))
                    File.Delete(f);
            }
        }
        catch { }
        byte[] served = AppTransform.TransformOrCached(app, name);
        Assert.Equal(File.ReadAllBytes(pristinePath), served);
    }

    [Fact]
    public void Erp_Content_Map_Covers_Data_Dir()
    {
        // ERP.Repository.InitializeData reads Data/*.json via a
        // BaseDirectory-relative walk that breaks in the browser; the VFS
        // content bridge must ship every file the sources hold.
        Assert.True(AppTransform.TryGetSpec("telerik-erp", out AppTransform.AppSpec? maybe));
        Assert.True(maybe is not null);
        var files = maybe!.ContentFiles;
        const string data = @"E:\AI\Code\WindowsForms\telerik\Telerik UI for WinForms demos\ExamplesProjects\ERP\ERP.Client\Data";
        string[] onDisk = Directory.GetFiles(data, "*.json");
        foreach (string disk in onDisk)
        {
            string key = "Data/" + Path.GetFileName(disk);
            Assert.True(files.TryGetValue(key, out string? mapped), key);
            Assert.True(File.Exists(mapped), mapped);
        }
        // rev176 staged the viewer Documents alongside the Data caches;
        // the map must cover both families exactly.
        const string docs = @"E:\AI\Code\WindowsForms\telerik\Telerik UI for WinForms demos\ExamplesProjects\ERP\ERP.Client\Documents";
        foreach (string pdf in new[] { "Documentation.pdf", "Instructions.pdf" })
        {
            string key = "Documents/" + pdf;
            Assert.True(files.TryGetValue(key, out string? mapped), key);
            Assert.True(File.Exists(mapped), mapped);
            Assert.True(File.Exists(Path.Combine(docs, pdf)), pdf);
        }
        Assert.Equal(onDisk.Length + 2, files.Count);
    }

    [Fact]
    public void Adgv_Content_Map_Resolves_App_Base_Assets_By_Tail()
    {
        // RVW-1104-2100 JobB: app-relative resources (flag-green_24.png,
        // lang*.json) must package from the application base folder, keyed
        // by tail name for the VFS walk — never from the browser FS root.
        // The enumerator map must include the loose assets and keep the
        // exclusion list (preparation/deps/runtimeconfig/dll.config) out.
        const string app = "benchmark-advanceddatagridview";
        Assert.True(AppTransform.TryGetSpec(app, out AppTransform.AppSpec? maybe));
        Assert.True(maybe is not null);
        var files = maybe!.ContentFiles;
        foreach (string tail in new[] { "flag-green_24.png", "flag-red_24.png", "lang.json" })
        {
            Assert.True(files.TryGetValue(tail, out string? mapped), tail);
            Assert.True(File.Exists(mapped), mapped);
            Assert.Equal(
                Path.Combine(AppTransform.AppFolders[app], tail),
                mapped);
        }
        foreach (string lang in Directory.GetFiles(AppTransform.AppFolders[app], "lang*.json"))
            Assert.True(files.ContainsKey(Path.GetFileName(lang)), lang);
        byte[] pngMagic = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        foreach (string tail in new[] { "flag-green_24.png", "flag-red_24.png" })
            Assert.Equal(pngMagic, File.ReadAllBytes(files[tail])[..8]);
        Assert.DoesNotContain(files.Keys, k =>
            k.Equals("preparation.json", StringComparison.OrdinalIgnoreCase) ||
            k.EndsWith(".deps.json", StringComparison.OrdinalIgnoreCase) ||
            k.EndsWith(".runtimeconfig.json", StringComparison.OrdinalIgnoreCase) ||
            k.EndsWith(".dll.config", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("benchmark-openpdn", "PaintDotNet.exe", "PaintDotNet")]
    [InlineData("benchmark-pkhex", "PKHeX.dll", "PKHeX")]
    [InlineData("benchmark-keepass", "KeePass.exe", "KeePass")]
    [InlineData("benchmark-linqpad5", "LINQPad.exe", "LINQPad")]
    [InlineData("benchmark-advanceddatagridview", "AdvancedDataGridViewSample.dll", "AdvancedDataGridViewSample")]
    [InlineData("benchmark-external-calculator", "Calculator.exe", "Calculator")]
    [InlineData("benchmark-openpdn", "PaintDotNet.SystemLayer.dll", "PaintDotNet.SystemLayer")]
    public void Benchmark_Entry_Assembly_Transforms_To_Loadable_Output(
        string app, string entryFile, string assemblyName)
    {
        // RVW-1104-2100 JobA: transform proof per staged benchmark app. The
        // entry assembly must resolve from the staged folder and rewrite to
        // completion; the output must parse as the same assembly. This is
        // conversion proof only — browser boot stays verifier-owned, and the
        // catalog keeps a runtime-unverified note after the receipt flip.
        Assert.True(AppTransform.TryGetSpec(app, out AppTransform.AppSpec? maybe));
        Assert.True(maybe is not null);
        Assert.True(AppTransform.TryResolveAssembly(app, entryFile, out string? resolved));
        Assert.Equal(entryFile, resolved);
        byte[] served = AppTransform.TransformOrCached(app, entryFile);
        Assert.NotEmpty(served);
        using var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(new MemoryStream(served));
        Assert.Equal(assemblyName, asm.Name.Name);
    }

    [Fact]
    public void Calculator_Entry_Resolves_From_Prepared_Staging()
    {
        // PUSH1106-shell-w23: prepared calculator input (retained/calculator).
        // The entry must resolve from the staged folder and rewrite to a
        // loadable assembly; a missing input fails closed at resolve time.
        const string app = "calculator";
        const string entryFile = "Calculator.dll";
        Assert.True(AppTransform.TryGetSpec(app, out AppTransform.AppSpec? maybe));
        Assert.True(maybe is not null);
        Assert.True(AppTransform.TryResolveAssembly(app, entryFile, out string? resolved));
        Assert.Equal(entryFile, resolved);
        byte[] served = AppTransform.TransformOrCached(app, entryFile);
        Assert.NotEmpty(served);
        using var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(new MemoryStream(served));
        Assert.Equal("Calculator", asm.Name.Name);
    }

    [Fact]
    public void Linqpad_Dependency_Closure_Transforms_To_Loadable_Output()
    {
        // DIRECT-1105 Priority1: LINQPad5 asset 500. Every dependency in the
        // staged dll/ closure is served through the rewriter via appresolve;
        // a single throwing file is a user-visible 500 at boot. Each must
        // resolve and rewrite to completion, and the output must parse as the
        // same assembly. Real retained originals only, no synthetic inputs.
        const string app = "benchmark-linqpad5";
        Assert.True(AppTransform.TryGetSpec(app, out AppTransform.AppSpec? maybe));
        Assert.True(maybe is not null);
        string closure = Path.Combine(AppTransform.AppFolders[app], "dll");
        Assert.True(Directory.Exists(closure), $"closure missing: {closure}");
        string[] deps = Directory.GetFiles(closure, "*.dll", SearchOption.TopDirectoryOnly);
        Assert.NotEmpty(deps);
        var failures = new System.Collections.Generic.List<string>();
        foreach (string dep in deps)
        {
            string tail = Path.GetFileName(dep);
            try
            {
                Assert.True(AppTransform.TryResolveAssembly(app, tail, out string? resolved), tail);
                byte[] served = AppTransform.TransformOrCached(app, resolved!);
                Assert.NotEmpty(served);
                using var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(new MemoryStream(served));
                Assert.Equal(Path.GetFileNameWithoutExtension(tail), asm.Name.Name);
            }
            catch (Exception ex) when (ex is not Xunit.Sdk.XunitException)
            {
                failures.Add(tail + ": " + ex.GetType().Name + ": " + ex.Message.Split('\n')[0]);
            }
        }
        Assert.True(failures.Count == 0, "transform failures:\n" + string.Join("\n", failures));
    }

    [Fact]
    public void Linqpad_Startup_Adapter_Adds_Parameterless_MainForm_Ctor()
    {
        // DIRECT-1105 Priority1: served boot fails at DesktopBenchmarkForms.Create
        // because LINQPad.UI.MainForm has no public parameterless ctor
        // (NotSupportedException: startup adapter needed). The experimental
        // adapter must add exactly that (delegating to the original
        // (string, bool) ctor); disabled must leave bytes untouched. Real
        // retained original input, no synthetic assemblies.
        const string app = "benchmark-linqpad5";
        Assert.True(AppTransform.TryGetSpec(app, out AppTransform.AppSpec? maybe));
        Assert.True(maybe is not null);
        string entry = Path.Combine(AppTransform.AppFolders[app], "LINQPad.exe");
        Assert.True(File.Exists(entry), $"entry missing: {entry}");
        byte[] pristine = File.ReadAllBytes(entry);
        byte[] adapted = BenchmarkStartupCompatibility.Rewrite(pristine, app,
            [Path.GetDirectoryName(entry)!], enabled: true);
        Assert.NotEqual(pristine, adapted);
        using (var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(new MemoryStream(adapted)))
        {
            Assert.Equal("LINQPad", asm.Name.Name);
            var mainForm = asm.MainModule.GetType("LINQPad.UI.MainForm");
            Assert.True(mainForm is not null);
            Assert.Contains(mainForm.Methods, m => m.IsConstructor && m.IsPublic && !m.IsStatic && m.Parameters.Count == 0);
        }
        byte[] untouched = BenchmarkStartupCompatibility.Rewrite(pristine, app,
            [Path.GetDirectoryName(entry)!], enabled: false);
        Assert.Equal(pristine, untouched);
    }

    [Fact]
    public void Keepass_App_Config_Aliases_Staged_Entry_Config()
    {
        // FP-1935: KeePass boot requests VFS tail "app.config" (non-fatal
        // notice on miss). The map must serve the staged KeePass.exe.config
        // bytes under that name: alias to the real file, never a copy or
        // placeholder. Other benchmark maps must not gain the key (scope).
        const string app = "benchmark-keepass";
        Assert.True(AppTransform.TryGetSpec(app, out AppTransform.AppSpec? maybe));
        Assert.True(maybe is not null);
        var files = maybe!.ContentFiles;
        Assert.True(files.TryGetValue("app.config", out string? mapped), "app.config");
        Assert.True(File.Exists(mapped), mapped);
        string staged = Path.Combine(AppTransform.AppFolders[app], "KeePass.exe.config");
        Assert.Equal(staged, mapped);
        Assert.Equal(File.ReadAllBytes(staged), File.ReadAllBytes(mapped));
        // DIRECT-1105: the rooted "/vfs/app.config" read bypasses the
        // tail-matching registry (unrewritten framework reader), so the same
        // staged bytes are also served under the "vfs/app.config" key whose
        // MEMFS mirror lands exactly on the requested path.
        Assert.True(files.TryGetValue("vfs/app.config", out string? rooted), "vfs/app.config");
        Assert.Equal(staged, rooted);
        Assert.Equal(File.ReadAllBytes(staged), File.ReadAllBytes(rooted));
        Assert.True(AppTransform.TryGetSpec("benchmark-advanceddatagridview", out AppTransform.AppSpec? adgv));
        Assert.True(adgv is not null);
        Assert.DoesNotContain("app.config", adgv!.ContentFiles.Keys);
        Assert.DoesNotContain("vfs/app.config", adgv!.ContentFiles.Keys);
    }

    [Fact]
    public void Minesweeper_Config_Alias_Serves_Staged_Dll_Config()
    {
        // apprev-g5-shell-config (RT-G5A-1927 item 10): Minesweeper settings
        // fault on ClientConfigurationHost::OpenStreamForWrite '' — the
        // staged entry config was never served (*.dll.config excluded from
        // loose content). The map must serve the staged
        // Minesweeper.dll.config bytes under its desktop exe-config name:
        // alias to the real file, never a copy or placeholder. Other
        // benchmark maps must not gain the key (scope).
        const string app = "benchmark-minesweeper";
        Assert.True(AppTransform.TryGetSpec(app, out AppTransform.AppSpec? maybe));
        Assert.True(maybe is not null);
        var files = maybe!.ContentFiles;
        Assert.True(files.TryGetValue("Minesweeper.dll.config", out string? mapped), "Minesweeper.dll.config");
        Assert.True(File.Exists(mapped), mapped);
        string staged = Path.Combine(AppTransform.AppFolders[app], "Minesweeper.dll.config");
        Assert.Equal(staged, mapped);
        Assert.Equal(File.ReadAllBytes(staged), File.ReadAllBytes(mapped));
        Assert.True(AppTransform.TryGetSpec("benchmark-advanceddatagridview", out AppTransform.AppSpec? adgv));
        Assert.True(adgv is not null);
        Assert.DoesNotContain("Minesweeper.dll.config", adgv!.ContentFiles.Keys);
    }

    [Fact]
    public void Word_Spec_Carries_PdnCpu()
    {
        // MEMBERREF-RESTAGE: the IsWindow forwarder only fires when the
        // spec carries PdnCpu (sole paintnet occurrence left word with
        // cpuPaintPath null -> fail-closed zero IsWindow -> Assign no-op,
        // no TypeLoad but unhooked UI). The word spec must ship the helper.
        Assert.True(AppTransform.TryGetSpec("syncfusion-word", out AppTransform.AppSpec? maybe));
        Assert.True(maybe is not null);
        AppTransform.AppSpec spec = maybe!;
        Assert.False(string.IsNullOrEmpty(spec.PdnCpu), "word spec omits PdnCpu");
        Assert.True(File.Exists(spec.PdnCpu), $"PdnCpu missing: {spec.PdnCpu}");
    }

    [Fact]
    public void Paintnet_DirectWriteForwarder_Keeps_Self_Scope()
    {
        // The DWF fake is hand-built managed (the mixed-mode pristine
        // aborts WASM interp) with self-named typeref scopes; it needs
        // pinvoke-stubbing at most. Full rewrite dropped its
        // self-named assemblyref and Cecil re-emitted the orphaned
        // typerefs as System.Private.CoreLib (TypeLoad at bind time).
        const string app = "paintnet";
        const string name = "DirectWriteForwarder.dll";
        Assert.True(AppTransform.TryGetSpec(app, out AppTransform.AppSpec? maybe));
        Assert.True(maybe is not null);
        try
        {
            string dir = Path.Combine(AppTransform.CacheRoot, app);
            if (Directory.Exists(dir))
            {
                foreach (string f in Directory.GetFiles(dir, "*" + Path.GetFileNameWithoutExtension(name) + "*"))
                    File.Delete(f);
            }
        }
        catch { }
        byte[] served = AppTransform.TransformOrCached(app, name);
        using var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(new MemoryStream(served));
        Assert.Contains(asm.MainModule.AssemblyReferences, r => r.Name == "DirectWriteForwarder");
        var face = asm.MainModule.GetTypeReferences()
            .First(t => t.FullName == "MS.Internal.Text.TextInterface.FontFace");
        Assert.Equal("DirectWriteForwarder", face.Scope.Name);
    }

    [Fact]
    public void Paintnet_MagicScaler_Gets_Full_Rewrite()
    {
        // PhotoSauce.MagicScaler must NOT ship verbatim: rewritten PDN
        // imaging adapters implement its IPixelSource with remapped drawing
        // types (Rectangle -> WfWasm.Drawing), while a verbatim MagicScaler
        // keeps System.Drawing.Primitives tokens. The VTable slot then
        // mismatches and every adapter fails to load headless
        // (TypeLoadException: VTable setup), blanking all thumbnails.
        Assert.True(AppTransform.TryGetSpec("paintnet", out AppTransform.AppSpec? maybe));
        Assert.True(maybe is not null);
        Assert.DoesNotContain(maybe!.Verbatim, n =>
            n.Equals("PhotoSauce.MagicScaler.dll", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(maybe.Assemblies, n =>
            n.Equals("PhotoSauce.MagicScaler.dll", StringComparison.OrdinalIgnoreCase));
    }
}
