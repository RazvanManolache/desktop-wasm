using System.Reflection;

namespace WfWasm.CanvasHost;

/// <summary>Exact real-form construction, without arbitrary-form or smoke-form fallbacks.</summary>
internal static class DesktopBenchmarkForms
{
    public static System.Windows.Forms.Form Create(DesktopBenchmarks.Benchmark benchmark)
    {
        if (benchmark.RequiresManualInput)
            throw new NotSupportedException($"{benchmark.Label}: application files and verified startup information are not prepared. {benchmark.Notes}");
        Type? type = Type.GetType(benchmark.FormType + ", " + benchmark.FormAssembly);
        type ??= AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => string.Equals(a.GetName().Name, benchmark.FormAssembly, StringComparison.OrdinalIgnoreCase))
            ?.GetType(benchmark.FormType);
        if (type is null)
            throw new TypeLoadException($"{benchmark.Label}: real form {benchmark.FormType} was not found in {benchmark.FormAssembly}.");
        // Application-startup init the harness would otherwise skip
        // (G5-bench gitextensions JTC). Best-effort: false keeps the
        // original chain, never a new failure. Runs before the adapter
        // branch: the adapted ctor graph needs the task context.
        VsThreadingInitShim.EnsureInitialized(type.Assembly);
        EntryStaticInit.EnsureInitialized(type.Assembly);
        // RT-ADAPTER-2327: adapter-marked benchmarks skip the generic
        // checks below (their declared startup type is intentionally not
        // directly constructible) and build through the startup adapter.
        // Adapter decline falls through to today's chain, never a new failure.
        if (string.Equals(benchmark.StartupAdapter, "GitExtensions", StringComparison.Ordinal))
        {
            if (GitExtensionsStartupAdapter.TryCreateForm(type, null, out object? adapted))
            {
                if (adapted is System.Windows.Forms.Form adaptedForm)
                    return adaptedForm;
                throw new InvalidOperationException($"{benchmark.Label}: startup adapter built {type.FullName} but it is not a host Form.");
            }
        }
        // MREMOTENG-0155: same pattern for FrmMain (private ctor behind
        // the public Default singleton). Decline falls through to today's
        // chain, never a new failure.
        if (string.Equals(benchmark.StartupAdapter, "mRemoteNG", StringComparison.Ordinal))
        {
            if (MRemoteNGStartupAdapter.TryCreateForm(type, null, out object? adapted))
            {
                if (adapted is System.Windows.Forms.Form adaptedForm)
                    return adaptedForm;
                throw new InvalidOperationException($"{benchmark.Label}: startup adapter built {type.FullName} but it is not a host Form.");
            }
        }
        if (!typeof(System.Windows.Forms.Form).IsAssignableFrom(type))
            throw new TypeLoadException($"{benchmark.Label}: declared startup type is not a compatible WinForms Form.");
        var constructor = type.GetConstructor(BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
        if (constructor is null)
            throw new NotSupportedException($"{benchmark.Label}: its real startup form requires application services. A startup adapter is needed. {benchmark.Notes}");
        return (System.Windows.Forms.Form)constructor.Invoke(null);
    }
}
