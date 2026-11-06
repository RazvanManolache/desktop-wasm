// RT-ADAPTER-2327: GitExtensions startup adapter. The benchmark harness
// creates FormBrowse directly, skipping Program.Main, so the designer-only
// parameterless ctor throws InvalidOperationException (RT-CTOR-2314: first
// failing frame GitUI.GitModuleForm..ctor). This builds the minimal real
// service graph and invokes FormBrowse(IGitUICommands, BrowseArguments).
// Reflection-only: the host ships no GitUI references (bundle integrity),
// so every vendor type is resolved by exact full name from the form's own
// closure (shapes proven by Cecil recon in rot2327-adapter/). Best-effort:
// any miss returns false and the caller keeps today's chain.
using System.Reflection;

namespace WfWasm.CanvasHost;

internal static class GitExtensionsStartupAdapter
{
    private const string FormFullName = "GitUI.CommandsDialogs.FormBrowse";
    private const string CommandsFullName = "GitUI.GitUICommands";
    private const string CommandsIfaceFullName = "GitExtensions.Extensibility.Git.IGitUICommands";
    private const string BrowseArgsFullName = "GitExtensions.Extensibility.BrowseArguments";

    // Service interfaces the provider must answer (GetRequiredService
    // throws when one is missing). Verified against the built graph after
    // construction; the concrete type per interface is built in BuildGraph.
    // PUSH1106-gitext-0315: only CONSTRUCTION-ESSENTIAL services are
    // required here. Peripheral services (settings-backed repository
    // descriptions, title generation, jump lists, scripts, hotkeys, console
    // emulators, shells, submodule status, history UI, branch names) stay in
    // the plan as best-effort: on wasm a poisoned leaf (e.g. FileSystemWatcher
    // PNSE via AppSettings) must not veto the real window; the skip is logged,
    // never swallowed. Served verification decides whether the reduced graph
    // is sufficient at runtime (missing services resolve to null).
    private static readonly string[] ServiceIfaces =
    [
        "GitCommands.Git.IGitDirectoryResolver",
        "GitExtensions.Extensibility.Git.IGitExecutorProvider",
        "GitExtensions.Extensibility.Git.IGitModule",
        "GitExtensions.Extensibility.Git.IGitUICommands",
    ];

    public static bool TryCreateForm(Type? formType, string? workingDirectory, out object? form)
    {
        form = null;
        try
        {
            if (formType is null || !string.Equals(formType.FullName, FormFullName, StringComparison.Ordinal))
                return false;
            string workDir = string.IsNullOrEmpty(workingDirectory)
                ? AppDomain.CurrentDomain.BaseDirectory
                : workingDirectory;
            Dictionary<Type, object> built = new();
            if (!BuildGraph(formType, workDir, built))
                return false;
            Type? commandsIface = FindType(formType, CommandsIfaceFullName);
            Type? argsType = FindType(formType, BrowseArgsFullName);
            object? commands = FindService(built, commandsIface);
            if (commandsIface is null || argsType is null || commands is null)
                return false;
            object? args = TryConstruct(argsType, built, null, workDir);
            if (args is null)
                return false;
            ConstructorInfo? ctor = formType.GetConstructor([commandsIface, argsType]);
            if (ctor is null)
                return false;
            form = ctor.Invoke([commands, args]);
            return form is not null;
        }
        catch
        {
            form = null;
            return false;
        }
    }

    // Construction-essential plan members: without one of these the
    // FormBrowse(IGitUICommands, BrowseArguments) call cannot be assembled.
    private static readonly string[] EssentialPlan =
    [
        "GitCommands.Git.GitDirectoryResolver",
        "GitCommands.GitExecutorProvider",
        "GitCommands.GitModule",
        CommandsFullName,
    ];

    private static bool BuildGraph(Type formType, string workDir, Dictionary<Type, object> built)
    {
        var provider = new MinimalServiceProvider(built);
        // Concrete types in dependency order (dependencies first). Each is
        // built by the greedy ctor rule below; every shape below was proven
        // against the staged vendor closure by Cecil recon (rot2327-adapter/)
        // and each type here has exactly one all-resolvable ctor, so the
        // rule is deterministic, not heuristic.
        // PUSH1106-gitext-0315: non-essential members are best-effort. A
        // member that fails to build is SKIPPED with a served-console line
        // (never swallowed); only an essential failure declines the adapter.
        string[] plan =
        [
            "GitCommands.Git.GitDirectoryResolver",
            "GitCommands.GitExecutorProvider",
            "GitCommands.GitModule",
            "GitCommands.UserRepositoryHistory.RepositoryDescriptionProvider",
            "GitUI.ScriptsEngine.ScriptsManager",
            "GitUI.Hotkey.HotkeySettingsManager",
            "GitUI.ConsoleEmulation.PlainText.PlainTextConsoleEmulatorsRegistry",
            "GitUI.Shells.ShellProvider",
            "GitCommands.AppTitleGenerator",
            "GitUI.WindowsJumpListManager",
            "GitCommands.Submodules.SubmoduleStatusProvider",
            "GitUI.RepositoryCurrentBranchNameProvider",
            "GitUI.RepositoryCurrentBranchNameCache",
            "GitUI.CommandsDialogs.InvalidRepositoryRemover",
            "GitUI.RepositoryHistoryUIService",
            CommandsFullName,
        ];
        foreach (string typeName in plan)
        {
            Type? t = FindType(formType, typeName);
            if (t is null)
            {
                if (EssentialPlan.Contains(typeName))
                    return false;
                LogSkip(typeName, "type-not-found");
                continue;
            }
            object? instance = TryConstruct(t, built, provider, workDir, out Exception? failure);
            if (instance is null)
            {
                if (EssentialPlan.Contains(typeName))
                {
                    LogSkip(typeName, failure);
                    return false;
                }
                LogSkip(typeName, failure);
                continue;
            }
            built[t] = instance;
            foreach (Type iface in t.GetInterfaces())
                built.TryAdd(iface, instance);
        }
        foreach (string ifaceName in ServiceIfaces)
        {
            Type? iface = FindType(formType, ifaceName);
            if (iface is null)
                return false;
            if (!built.Any(kv => iface.IsAssignableFrom(kv.Key)))
                return false;
        }
        return true;
    }

    private static object? FindService(Dictionary<Type, object> built, Type? serviceType)
    {
        if (serviceType is null)
            return null;
        if (built.TryGetValue(serviceType, out object? direct) && direct is not null)
            return direct;
        foreach (var kv in built)
        {
            if (serviceType.IsAssignableFrom(kv.Key))
                return kv.Value;
        }
        return null;
    }

    // Greedy ctor rule: fewest parameters first; string takes the working
    // directory, IServiceProvider takes the minimal provider, every other
    // parameter must resolve from already-built instances by assignability.
    // Any ctor that throws is skipped for the next overload.
    private static object? TryConstruct(Type t, Dictionary<Type, object> built,
        IServiceProvider? self, string workDir, out Exception? failure)
    {
        failure = null;
        foreach (ConstructorInfo c in t.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .OrderBy(c => c.GetParameters().Length))
        {
            ParameterInfo[] ps = c.GetParameters();
            var args = new object?[ps.Length];
            bool ok = true;
            for (int i = 0; i < ps.Length; i++)
            {
                Type pt = ps[i].ParameterType;
                if (pt == typeof(string))
                    args[i] = workDir;
                else if (pt == typeof(IServiceProvider))
                    args[i] = self;
                else
                    args[i] = FindService(built, pt);
                if (args[i] is null || !pt.IsInstanceOfType(args[i]))
                {
                    ok = false;
                    break;
                }
            }
            if (!ok)
                continue;
            try
            {
                object? o = c.Invoke(args);
                if (o is not null)
                    return o;
            }
            catch (Exception ex)
            {
                // try next overload; remember the first failure for the
                // skip line so the cause is never swallowed.
                failure ??= ex.InnerException ?? ex;
            }
        }
        return null;
    }

    private static object? TryConstruct(Type t, Dictionary<Type, object> built,
        IServiceProvider? self, string workDir)
        => TryConstruct(t, built, self, workDir, out _);

    private static void LogSkip(string typeName, object? failure)
    {
        try
        {
            string detail = failure is Exception ex
                ? ex.GetType().Name + ": " + (ex.Message ?? string.Empty)
                : failure?.ToString() ?? "unresolvable";
            if (detail.Length > 160)
                detail = detail.Substring(0, 160);
            Console.WriteLine("[Host][adapter-skip] GitExtensions " + typeName + ": " + detail);
        }
        catch { }
    }

    private static Type? FindType(Type formType, string fullName)
    {
        try
        {
            Type? t = formType.Assembly.GetType(fullName, throwOnError: false);
            if (t is not null)
                return t;
            foreach (AssemblyName name in formType.Assembly.GetReferencedAssemblies())
            {
                Assembly asm;
                try
                {
                    asm = Assembly.Load(name);
                }
                catch
                {
                    continue;
                }
                t = asm.GetType(fullName, throwOnError: false);
                if (t is not null)
                    return t;
            }
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                t = asm.GetType(fullName, throwOnError: false);
                if (t is not null)
                    return t;
            }
        }
        catch
        {
            return null;
        }
        return null;
    }

    private sealed class MinimalServiceProvider(Dictionary<Type, object> services) : IServiceProvider
    {
        public object? GetService(Type serviceType)
        {
            if (services.TryGetValue(serviceType, out object? direct))
                return direct;
            foreach (var kv in services)
            {
                if (serviceType.IsAssignableFrom(kv.Key))
                    return kv.Value;
            }
            return null;
        }
    }
}
