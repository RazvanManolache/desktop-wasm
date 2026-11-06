using System.Reflection;
using System.Reflection.Emit;
using WfWasm.CanvasHost;
using WfWasm.Shim;

namespace WfWasm.CanvasHost.Tests;

// RT-ADAPTER-2327: gitext startup adapter. The harness builds FormBrowse
// directly, skipping Program.Main, so the designer-only parameterless ctor
// throws InvalidOperationException (RT-CTOR-2314: first frame
// GitUI.GitModuleForm..ctor). The adapter builds the minimal real service
// graph reflectively (no static GitUI references in host code) and invokes
// FormBrowse(IGitUICommands, BrowseArguments).
//
// Test strategy (desktop net9 gate vs net10-only vendor closure): the staged
// pristine GitUI targets net10 WinForms and cannot load under the net9 test
// host, and no fork-based GitUI exists on disk for tests. So these tests run
// the adapter against an emitted fake closure that mirrors the REAL vendor
// shapes name-for-name and ctor-for-ctor (proven by Cecil recon over the
// staged bytes in .artifacts/tmp/runs/dw-runtime/rot2327-adapter/; any
// vendor rename fails the adapter closed, never green-on-wrong-shapes).
// Served-convert acceptance rides the w21 boot probe per the assignment.
public sealed class GitExtensionsStartupAdapterTests
{
    private const string GuardText =
        "This constructor is only to be called by the Visual Studio designer, and the translation unit tests.";

    // PUSH1106-gitext-0315: served w25 proved the adapter SILENTLY declines
    // when a peripheral service is poisoned by a missing platform API
    // (RepositoryDescriptionProvider..ctor touches AppSettings, whose cctor
    // builds FileSettingsCache -> new FileSystemWatcher() -> PNSE on wasm;
    // Cecil proof in the slice report). The harness then falls through to
    // the designer-guarded parameterless ctor. A poisoned leaf must not veto
    // the real window: the adapter skips it with a logged line and builds
    // the rest. The fake mirrors the poisoning with a static switch.
    public static bool PoisonSettingsCache;

    [Fact]
    public void TryCreateForm_Builds_FormBrowse_When_Settings_Cache_Poisoned()
    {
        PoisonSettingsCache = true;
        try
        {
            Assert.True(GitExtensionsStartupAdapter.TryCreateForm(
                FakeClosure.FormBrowse, null, out object? form));
            Assert.NotNull(form);
            Assert.Equal("GitUI.CommandsDialogs.FormBrowse", form.GetType().FullName);
        }
        finally
        {
            PoisonSettingsCache = false;
        }
    }

    [Fact]
    public void TryCreateForm_Logs_Skip_For_Poisoned_Leaf_Service()
    {
        PoisonSettingsCache = true;
        TextWriter prior = Console.Out;
        StringWriter capture = new();
        try
        {
            Console.SetOut(capture);
            Assert.True(GitExtensionsStartupAdapter.TryCreateForm(
                FakeClosure.FormBrowse, null, out object? _));
        }
        finally
        {
            Console.SetOut(prior);
            PoisonSettingsCache = false;
        }
        Assert.Contains("[Host][adapter-skip]", capture.ToString());
        Assert.Contains("RepositoryDescriptionProvider", capture.ToString());
    }

    [Fact]
    public void TryCreateForm_Returns_False_For_Unrelated_Type()
    {
        Assert.False(GitExtensionsStartupAdapter.TryCreateForm(typeof(string), null, out object? form));
        Assert.Null(form);
    }

    [Fact]
    public void TryCreateForm_Returns_False_For_Null_Type()
    {
        Assert.False(GitExtensionsStartupAdapter.TryCreateForm(null, null, out object? form));
        Assert.Null(form);
    }

    [Fact]
    public void TryCreateForm_Builds_FormBrowse_On_Mirrored_Closure()
    {
        Type formType = FakeClosure.FormBrowse;
        Assert.True(GitExtensionsStartupAdapter.TryCreateForm(formType, null, out object? form));
        Assert.NotNull(form);
        Assert.Equal("GitUI.CommandsDialogs.FormBrowse", form.GetType().FullName);
        Assert.True(FakeClosure.IsHostForm(form));
    }

    [Fact]
    public void Create_GitextBenchmark_Uses_Startup_Adapter_Path()
    {
        // Routing proof for the adapter branch. Production converts return a
        // real form (ex null). The fake closure builds a host Form too, so
        // this goes fully green here. Pre-fix (stub adapter declining) this
        // threw TargetInvocationException over the designer guard, which
        // fails the type assertion below.
        _ = FakeClosure.FormBrowse;
        var benchmark = DesktopBenchmarks.Find(AppScene.BenchmarkGitExtensions);
        Assert.NotNull(benchmark);
        Exception? ex = Record.Exception(() => DesktopBenchmarkForms.Create(benchmark));
        if (ex is null)
            return;
        var io = Assert.IsType<InvalidOperationException>(ex);
        Assert.Contains("startup adapter built GitUI.CommandsDialogs.FormBrowse", io.Message);
        var chain = new List<string>();
        for (Exception? e = ex; e is not null; e = e.InnerException)
            chain.Add(e.GetType().Name + ": " + e.Message);
        Assert.DoesNotContain(chain, s => s.Contains("only to be called by the Visual Studio designer"));
    }

    [Fact]
    public void Catalog_Gitext_Entry_Marks_GitExtensions_Adapter()
    {
        var gitext = DesktopBenchmarks.Find(AppScene.BenchmarkGitExtensions);
        Assert.NotNull(gitext);
        Assert.Equal("GitExtensions", gitext.StartupAdapter);
        foreach (var entry in DesktopBenchmarks.Entries)
        {
            if (entry.Scene == AppScene.BenchmarkGitExtensions)
                continue;
            if (entry.Scene == AppScene.BenchmarkMRemoteNg)
            {
                Assert.Equal("mRemoteNG", entry.StartupAdapter);
                continue;
            }
            Assert.Null(entry.StartupAdapter);
        }
    }

    // Emitted fake closure mirroring the real vendor shapes (names and ctor
    // shapes proven by Cecil recon; see class comment). Assembly is named
    // "GitUI" so Type.GetType("GitUI.CommandsDialogs.FormBrowse, GitUI")
    // resolves exactly like production.
    private static class FakeClosure
    {
        private static readonly AssemblyBuilder ClosureAssembly;
        private static readonly ModuleBuilder ClosureModule;
        private static readonly Dictionary<string, TypeBuilder> Builders = new();
        private static Type? _formBrowse;

        static FakeClosure()
        {
            ClosureAssembly = AssemblyBuilder.DefineDynamicAssembly(
                new AssemblyName("GitUI"), AssemblyBuilderAccess.Run);
            ClosureModule = ClosureAssembly.DefineDynamicModule("GitUI");
            string[] ifaces =
            [
                "GitExtensions.Extensibility.Git.IGitUICommands",
                "GitExtensions.Extensibility.Git.IGitModule",
                "GitExtensions.Extensibility.Git.IGitExecutorProvider",
                "GitCommands.Git.IGitDirectoryResolver",
                "GitCommands.UserRepositoryHistory.IRepositoryDescriptionProvider",
                "GitUI.ScriptsEngine.IScriptsManager",
                "GitUI.ScriptsEngine.IScriptsRunner",
                "GitUI.Hotkey.IHotkeySettingsManager",
                "ResourceManager.IHotkeySettingsLoader",
                "GitUI.ConsoleEmulation.IConsoleEmulatorsRegistry",
                "GitUI.Shells.IShellProvider",
                "GitCommands.IAppTitleGenerator",
                "GitUI.IWindowsJumpListManager",
                "GitCommands.Submodules.ISubmoduleStatusProvider",
                "GitUI.IRepositoryHistoryUIService",
                "GitUI.IRepositoryCurrentBranchNameProvider",
                "GitUI.IRepositoryCurrentBranchNameCache",
                "GitUI.CommandsDialogs.IInvalidRepositoryRemover",
            ];
            foreach (string name in ifaces)
                Builders[name] = ClosureModule.DefineType(name,
                    TypeAttributes.Interface | TypeAttributes.Abstract | TypeAttributes.Public);
            // Concrete services with the audited ctor shapes. Every impl
            // registers its service interface(s): the adapter resolves ctor
            // parameters and GetRequiredService targets by assignability.
            DefineImpl("GitCommands.Git.GitDirectoryResolver", [],
                ["GitCommands.Git.IGitDirectoryResolver"]);
            DefineImpl("GitCommands.GitExecutorProvider",
                ["GitCommands.Git.IGitDirectoryResolver"],
                ["GitExtensions.Extensibility.Git.IGitExecutorProvider"]);
            DefineImpl("GitCommands.GitModule",
                ["GitExtensions.Extensibility.Git.IGitExecutorProvider", "System.String"],
                ["GitExtensions.Extensibility.Git.IGitModule"]);
            DefineImpl("GitCommands.UserRepositoryHistory.RepositoryDescriptionProvider",
                ["GitCommands.Git.IGitDirectoryResolver"],
                ["GitCommands.UserRepositoryHistory.IRepositoryDescriptionProvider"]);
            DefineImpl("GitUI.ScriptsEngine.ScriptsManager", [],
                ["GitUI.ScriptsEngine.IScriptsManager", "GitUI.ScriptsEngine.IScriptsRunner"]);
            DefineImpl("GitUI.Hotkey.HotkeySettingsManager",
                ["GitUI.ScriptsEngine.IScriptsManager"],
                ["GitUI.Hotkey.IHotkeySettingsManager", "ResourceManager.IHotkeySettingsLoader"]);
            DefineImpl("GitUI.ConsoleEmulation.PlainText.PlainTextConsoleEmulatorsRegistry",
                [], ["GitUI.ConsoleEmulation.IConsoleEmulatorsRegistry"]);
            DefineImpl("GitUI.Shells.ShellProvider", [], ["GitUI.Shells.IShellProvider"]);
            DefineImpl("GitCommands.AppTitleGenerator",
                ["GitCommands.UserRepositoryHistory.IRepositoryDescriptionProvider"],
                ["GitCommands.IAppTitleGenerator"]);
            DefineImpl("GitUI.WindowsJumpListManager",
                ["GitCommands.UserRepositoryHistory.IRepositoryDescriptionProvider"],
                ["GitUI.IWindowsJumpListManager"]);
            DefineImpl("GitCommands.Submodules.SubmoduleStatusProvider",
                ["GitExtensions.Extensibility.Git.IGitExecutorProvider"],
                ["GitCommands.Submodules.ISubmoduleStatusProvider"]);
            DefineImpl("GitUI.RepositoryCurrentBranchNameProvider", [],
                ["GitUI.IRepositoryCurrentBranchNameProvider"]);
            DefineImpl("GitUI.RepositoryCurrentBranchNameCache",
                ["GitUI.IRepositoryCurrentBranchNameProvider"],
                ["GitUI.IRepositoryCurrentBranchNameCache"]);
            DefineImpl("GitUI.CommandsDialogs.InvalidRepositoryRemover", [],
                ["GitUI.CommandsDialogs.IInvalidRepositoryRemover"]);
            DefineImpl("GitUI.RepositoryHistoryUIService",
                ["GitExtensions.Extensibility.Git.IGitExecutorProvider",
                 "GitUI.IRepositoryCurrentBranchNameCache",
                 "GitUI.CommandsDialogs.IInvalidRepositoryRemover"],
                ["GitUI.IRepositoryHistoryUIService"]);
            DefineImpl("GitExtensions.Extensibility.BrowseArguments", [], null);
            DefineImpl("GitUI.GitUICommands",
                ["System.IServiceProvider", "GitExtensions.Extensibility.Git.IGitModule"],
                ["GitExtensions.Extensibility.Git.IGitUICommands"]);
            foreach (var b in Builders.Values)
            {
                if (!b.IsInterface)
                    DefineStoreCtor(b);
            }
            // FormBrowse : host Form, designer-guarded parameterless ctor
            // plus the real commands ctor shape.
            Type hostForm = HostFormType();
            TypeBuilder fb = ClosureModule.DefineType("GitUI.CommandsDialogs.FormBrowse",
                TypeAttributes.Public | TypeAttributes.Class, hostForm);
            ConstructorBuilder plain = fb.DefineConstructor(
                MethodAttributes.Public, CallingConventions.Standard, Type.EmptyTypes);
            ILGenerator plainIl = plain.GetILGenerator();
            plainIl.Emit(OpCodes.Ldstr, GuardText);
            plainIl.Emit(OpCodes.Newobj, typeof(InvalidOperationException)
                .GetConstructor([typeof(string)])!);
            plainIl.Emit(OpCodes.Throw);
            Type cmdIface = Builders["GitExtensions.Extensibility.Git.IGitUICommands"];
            Type argsType = Builders["GitExtensions.Extensibility.BrowseArguments"];
            ConstructorBuilder svc = fb.DefineConstructor(
                MethodAttributes.Public, CallingConventions.Standard,
                [cmdIface, argsType]);
            ConstructorInfo hostCtor = hostForm.GetConstructor(Type.EmptyTypes)
                ?? throw new InvalidOperationException("fork Form lacks a parameterless ctor");
            ILGenerator svcIl = svc.GetILGenerator();
            svcIl.Emit(OpCodes.Ldarg_0);
            svcIl.Emit(OpCodes.Call, hostCtor);
            svcIl.Emit(OpCodes.Ret);
            Builders["GitUI.CommandsDialogs.FormBrowse"] = fb;
            foreach (var b in Builders.Values)
                b.CreateType();
            _formBrowse = fb;
        }

        public static Type FormBrowse => _formBrowse!;

        public static bool IsHostForm(object form) =>
            HostFormType().IsInstanceOfType(form);

        private static Type HostFormType()
        {
            // The host's fork Form without a static reference (which would be
            // ambiguous with desktop System.Windows.Forms in test code).
            Assembly? fork = null;
            try
            {
                fork = Assembly.Load("WfWasm.WinForms");
            }
            catch
            {
                fork = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => string.Equals(a.GetName().Name,
                        "WfWasm.WinForms", StringComparison.Ordinal));
            }
            Assert.NotNull(fork);
            Type? form = fork.GetType("System.Windows.Forms.Form");
            Assert.NotNull(form);
            return form;
        }

        private static void DefineImpl(string name, string[] ctorParams, string[]? ifaces)
        {
            TypeBuilder tb = ClosureModule.DefineType(name,
                TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Sealed);
            Builders[name] = tb;
            if (ifaces is not null)
            {
                foreach (string iface in ifaces)
                    tb.AddInterfaceImplementation(Builders[iface]);
            }
            PendingCtors.Add((tb, ctorParams));
        }

        private static readonly List<(TypeBuilder, string[])> PendingCtors = [];

        private static void DefineStoreCtor(TypeBuilder tb)
        {
            var pending = PendingCtors.First(p => p.Item1 == tb);
            Type[] paramTypes = pending.Item2.Select(Resolve).ToArray();
            ConstructorBuilder cb = tb.DefineConstructor(
                MethodAttributes.Public, CallingConventions.Standard, paramTypes);
            ILGenerator il = cb.GetILGenerator();
            if (string.Equals(tb.Name, "RepositoryDescriptionProvider", StringComparison.Ordinal))
            {
                // Mirror of the served wasm poisoning: the real
                // RepositoryDescriptionProvider..ctor touches AppSettings,
                // whose cctor builds FileSettingsCache -> new
                // FileSystemWatcher() -> PNSE. The switch reproduces the
                // throw without a wasm host.
                FieldInfo poison = typeof(GitExtensionsStartupAdapterTests)
                    .GetField(nameof(PoisonSettingsCache),
                        BindingFlags.Public | BindingFlags.Static)!;
                System.Reflection.Emit.Label ok = il.DefineLabel();
                il.Emit(OpCodes.Ldsfld, poison);
                il.Emit(OpCodes.Brfalse_S, ok);
                il.Emit(OpCodes.Ldstr,
                    "System.IO.FileSystemWatcher is not supported on this platform");
                il.Emit(OpCodes.Newobj, typeof(InvalidOperationException)
                    .GetConstructor([typeof(string)])!);
                il.Emit(OpCodes.Throw);
                il.MarkLabel(ok);
            }
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, typeof(object).GetConstructor(Type.EmptyTypes)!);
            il.Emit(OpCodes.Ret);
        }

        private static Type Resolve(string name) => name switch
        {
            "System.String" => typeof(string),
            "System.IServiceProvider" => typeof(IServiceProvider),
            _ => Builders[name],
        };
    }
}
