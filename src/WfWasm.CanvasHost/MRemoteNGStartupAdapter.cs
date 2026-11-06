// MREMOTENG-0155: mRemoteNG startup adapter. The benchmark harness
// creates FrmMain directly, skipping Program.Main, but FrmMain has no
// public parameterless ctor (retained source
// mremoteng/mRemoteNG/UI/Forms/frmMain.cs:114 `private FrmMain()`, served
// gate: NotSupportedException "requires application services" on live
// w23). The app's own path is the public static `Default` singleton
// (frmMain.cs:49-52, Lazy STA-enforced). This builds through Default
// first, falling back to the non-public parameterless ctor. Scope:
// synthetic offline connection tree only (adapter creates the form;
// no RDP/SSH sessions, no credential storage — matches the catalog
// honest-scope note). Reflection-only: the host ships no mRemoteNG
// references (bundle integrity), so the form type is matched by exact
// full name. Best-effort: any miss returns false and the caller keeps
// today's chain.
using System.Reflection;

namespace WfWasm.CanvasHost;

internal static class MRemoteNGStartupAdapter
{
    private const string FormFullName = "mRemoteNG.UI.Forms.FrmMain";

    public static bool TryCreateForm(Type? formType, string? workingDirectory, out object? form)
    {
        form = null;
        // CREDIT1300-RT5: host wiring for the SystemEvents bridge. The
        // mRemoteNG served path is the only closure that binds both
        // broadcast claimants, so consulting this adapter ensures the
        // bridge subscription exactly once (idempotent). Wiring must never
        // break form creation: isolated try/catch, fail-closed.
        try
        {
            SystemEventsBridgeAdapter.EnsureSubscribed();
        }
        catch
        {
            // Wiring best-effort only; the form path below is unaffected.
        }
        try
        {
            if (formType is null || !string.Equals(formType.FullName, FormFullName, StringComparison.Ordinal))
                return false;
            // Path 1: the app's own singleton (Lazy, STA-enforced).
            try
            {
                PropertyInfo? def = formType.GetProperty("Default",
                    BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
                if (def is not null && def.CanRead && def.GetIndexParameters().Length == 0)
                {
                    object? instance = def.GetValue(null);
                    if (instance is System.Windows.Forms.Form defaultForm
                        && AcceptConstructed(defaultForm))
                    {
                        form = defaultForm;
                        return true;
                    }
                }
            }
            catch
            {
                // fall through to the ctor path
            }
            // Path 2: non-public parameterless ctor (same shape the
            // singleton uses internally).
            try
            {
                ConstructorInfo? ctor = formType.GetConstructor(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                    null, Type.EmptyTypes, null);
                if (ctor is not null && ctor.IsPublic == false)
                {
                    object? instance = ctor.Invoke(null);
                    if (instance is System.Windows.Forms.Form created
                        && AcceptConstructed(created))
                    {
                        form = created;
                        return true;
                    }
                }
            }
            catch
            {
                // decline below
            }
            return false;
        }
        catch
        {
            form = null;
            return false;
        }
    }

    // RVW3293: the wasm rewriter guards designer construction (stash +
    // degraded return), so Default/private-ctor can SUCCEED while yielding
    // a blank window (served w27/w28: Controls=0 + follow-on NREs in
    // Load from null child controls). A blank form is not a real window:
    // log the skip (never swallowed) and decline so the harness keeps
    // today's chain instead of serving the blank + NRE. Real FrmMain
    // carries msMain/tsContainer/pnlDock, so any constructed top-level
    // control counts as genuine.
    private static bool AcceptConstructed(System.Windows.Forms.Form form)
    {
        try
        {
            if (form.Controls.Count > 0)
                return true;
        }
        catch
        {
            return false;
        }
        try
        {
            Console.WriteLine("[Host][adapter-skip] mRemoteNG FrmMain: degraded construction " +
                "(top-level Controls=0; guarded-ctor wall: splash-TypeLoad/PresentationCore-FNF/Process-PNSE)");
        }
        catch { }
        return false;
    }
}
