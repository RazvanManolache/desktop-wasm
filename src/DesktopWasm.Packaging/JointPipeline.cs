using Mono.Cecil;

namespace DesktopWasm.Packaging;

/// <summary>
/// Fixed stage ordering for the composed conversion. WinForms positions
/// rewrite first, WPF positions second, validation last: the same source
/// bytes are never rewritten twice blindly, and validation always runs on
/// the live graph after both passes.
/// </summary>
public enum JointStage
{
    ScanClosure = 1,
    RewriteWinForms = 2,
    RewriteWpf = 3,
    ValidateLiveGraph = 4,
    HashAndManifest = 5,
}

public static class JointPipeline
{
    public static IReadOnlyList<JointStage> OrderedStages { get; } =
        [JointStage.ScanClosure, JointStage.RewriteWinForms, JointStage.RewriteWpf,
         JointStage.ValidateLiveGraph, JointStage.HashAndManifest];

    /// <summary>
    /// An already-converted module carries engine references and no
    /// remaining convertible source references: re-running the pipeline
    /// must be a no-op (idempotency), never a double rewrite.
    /// </summary>
    public static bool IsAlreadyConverted(ModuleDefinition module)
    {
        ArgumentNullException.ThrowIfNull(module);
        var refs = module.AssemblyReferences.Select(r => r.Name).ToHashSet<string>(StringComparer.OrdinalIgnoreCase);
        var hasEngine = refs.Contains(JointMap.WpfEngineAssembly) || refs.Contains(JointMap.WinFormsEngineAssembly);
        if (!hasEngine) return false;
        return !refs.Any(r =>
            JointMap.WpfSourceAssemblies.Contains(r) || JointMap.WinFormsSourceAssemblies.Contains(r));
    }

    /// <summary>
    /// Rejects out-of-order stage application visibly: validation or
    /// manifesting before both rewrites is an order violation, not a pass.
    /// </summary>
    public static JointGap? CheckOrder(IReadOnlyList<JointStage> applied)
    {
        var last = 0;
        foreach (var stage in applied)
        {
            if ((int)stage < last)
                return new JointGap("", JointGap.OrderViolation,
                    $"Stage {stage} applied after a later stage: pipeline order is {string.Join(" < ", OrderedStages)}.");
            last = (int)stage;
        }
        if (applied.Contains(JointStage.ValidateLiveGraph) &&
            !(applied.Contains(JointStage.RewriteWinForms) && applied.Contains(JointStage.RewriteWpf)))
            return new JointGap("", JointGap.OrderViolation,
                "ValidateLiveGraph ran before both rewrite passes: validation must see the fully composed graph.");
        return null;
    }
}
