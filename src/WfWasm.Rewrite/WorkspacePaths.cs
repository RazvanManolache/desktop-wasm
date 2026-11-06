// TMPMOVE rootfix: canonical workspace-root resolution shared by the rewrite
// engine and the host server. Replaces fixed-depth AppContext.BaseDirectory
// climbs (five parents), which resolve differently per deployment nesting: a
// test binary at tests/<t>/bin/Debug/net9.0 needs five parents to reach the
// repo, while a staged host at .artifacts/demo-deployments/<gen>/WfWasm.Host.Server
// needs four (five escapes to E:/AI/Code and breaks every retained/cache default).
namespace WfWasm.Rewrite;

/// <summary>
/// Resolves the DesktopWasm workspace root without fixed-depth guessing.
/// A workspace root holds catalog/apps.json plus src/ or scripts/.
/// Precedence: explicit parameter, WF_WORKSPACE_ROOT env (both validated),
/// then ancestor walk from the base dir, then from the current dir.
/// Missing valid root throws instead of silently parent-walking outward.
/// </summary>
public static class WorkspacePaths
{
    public const string WorkspaceRootEnvVar = "WF_WORKSPACE_ROOT";

    public static bool IsWorkspaceRoot(string dir) =>
        File.Exists(Path.Combine(dir, "catalog", "apps.json")) &&
        (Directory.Exists(Path.Combine(dir, "src")) || Directory.Exists(Path.Combine(dir, "scripts")));

    public static string FindWorkspaceRoot(string? explicitRoot = null, string? baseDirectory = null, string? currentDirectory = null)
    {
        string? env = Environment.GetEnvironmentVariable(WorkspaceRootEnvVar);
        string? wanted = !string.IsNullOrWhiteSpace(explicitRoot) ? explicitRoot
            : !string.IsNullOrWhiteSpace(env) ? env : null;
        if (wanted is not null)
        {
            string full = Path.GetFullPath(wanted);
            if (IsWorkspaceRoot(full)) return full;
            throw new InvalidOperationException(
                $"Workspace root override is not a workspace root (need catalog/apps.json plus src/ or scripts/): {full}");
        }

        string[] seeds = [baseDirectory ?? AppContext.BaseDirectory, currentDirectory ?? Directory.GetCurrentDirectory()];
        foreach (string seed in seeds)
        {
            string start = seed;
            if (File.Exists(start)) start = Path.GetDirectoryName(Path.GetFullPath(start))!;
            for (DirectoryInfo? node = new DirectoryInfo(Path.GetFullPath(start));
                 node is not null;
                 node = node.Parent)
            {
                if (IsWorkspaceRoot(node.FullName)) return node.FullName;
            }
        }

        throw new InvalidOperationException(
            $"No workspace root above base '{seeds[0]}' or cwd '{seeds[1]}' (need catalog/apps.json plus src/ or scripts/). " +
            $"Provide one via parameter or {WorkspaceRootEnvVar}.");
    }
}
