using System.Reflection;

namespace WfWasm.CanvasHost;

// Application-startup init for benchmark apps whose harness creates the
// startup form directly, skipping the app's own Program.Main.
// G5-bench gitextensions: upstream Program.Main runs
// `ThreadHelper.JoinableTaskContext = new JoinableTaskContext()`; the
// harness builds GitUI FormBrowse straight away, so the helper stays
// uninitialized and every JTC touch throws InvalidOperationException.
// The setter is internal to the app (not callable cross-assembly), so
// this shim initializes it reflectively, guarded and best-effort: any
// failure returns false and the original chain is preserved.
internal static class VsThreadingInitShim
{
    public static bool EnsureInitialized(Assembly formAssembly)
    {
        try
        {
            if (formAssembly.GetName().Name == "GitExtUtils")
                return EnsureThreadHelperInitialized(formAssembly, "GitUI.ThreadHelper");
            if (!formAssembly.GetReferencedAssemblies()
                .Any(n => string.Equals(n.Name, "GitExtUtils", StringComparison.OrdinalIgnoreCase)))
                return false;
            Assembly helperAsm = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => string.Equals(a.GetName().Name, "GitExtUtils", StringComparison.OrdinalIgnoreCase))
                ?? Assembly.Load("GitExtUtils");
            return EnsureThreadHelperInitialized(helperAsm, "GitUI.ThreadHelper");
        }
        catch
        {
            return false;
        }
    }

    internal static bool EnsureThreadHelperInitialized(Assembly helperAssembly, string helperTypeName)
    {
        try
        {
            Type? helper = helperAssembly.GetType(helperTypeName);
            helper ??= Type.GetType(helperTypeName + ", " + helperAssembly.GetName().Name);
            if (helper is null)
                return false;
            object? has = helper.GetProperty("HasJoinableTaskContext",
                BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            if (has is true)
                return true;
            PropertyInfo? prop = helper.GetProperty("JoinableTaskContext",
                BindingFlags.Public | BindingFlags.Static);
            MethodInfo? set = prop?.GetSetMethod(nonPublic: true);
            if (prop is null || set is null)
                return false;
            object context = Activator.CreateInstance(prop.PropertyType)
                ?? throw new InvalidOperationException("JoinableTaskContext ctor returned null.");
            set.Invoke(null, [context]);
            object? hasAfter = helper.GetProperty("HasJoinableTaskContext",
                BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            return hasAfter is true;
        }
        catch
        {
            return false;
        }
    }
}
