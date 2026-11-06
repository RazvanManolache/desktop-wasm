using System.Diagnostics;

namespace DesktopWasm.Core.Tests;

/// <summary>
/// Mechanism tests for scripts/wpf/Ensure-FullAssembly.ps1: staging copies the
/// source assembly byte-identical with a hash sidecar, skips when up to date,
/// hard-errors on an explicit missing source, and soft-skips when nothing is
/// discoverable. The fixture stages our own built Core assembly, so the test
/// exercises the real hash/copy/sidecar path with a genuine managed DLL.
/// </summary>
public sealed class StagingScriptTests
{
    private static DirectoryInfo CreateWorkDirectory() => Directory.CreateDirectory(
        Path.Combine(RepoRoot(), ".artifacts", "tmp", "tests", "core-staging", Guid.NewGuid().ToString("N")));

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DesktopWasm.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repo root (DesktopWasm.slnx) not found.");
    }

    private static string? FindShell()
    {
        foreach (var shell in new[] { "powershell", "pwsh" })
        {
            try
            {
                var probe = Process.Start(new ProcessStartInfo(shell, "-NoProfile -Command exit 0")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });
                if (probe is null) continue;
                probe.WaitForExit(15000);
                if (probe.ExitCode == 0) return shell;
            }
            catch (Exception) { /* try next */ }
        }

        return null;
    }

    private static (int Exit, string Out) RunScript(string shell, string script, string args, string workDir)
    {
        using var proc = Process.Start(new ProcessStartInfo(shell,
            $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\" {args}")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workDir,
        }) ?? throw new InvalidOperationException("Could not start shell.");
        var stdout = proc.StandardOutput.ReadToEnd();
        var stderr = proc.StandardError.ReadToEnd();
        if (!proc.WaitForExit(120000)) throw new TimeoutException("Staging script timed out.");
        return (proc.ExitCode, stdout + stderr);
    }

    [Fact]
    public void Stage_copies_byte_identical_and_skips_when_uptodate()
    {
        var shell = FindShell();
        if (shell is null) return; // No PowerShell on this machine; mechanism covered by host build gate.
        var root = RepoRoot();
        var work = CreateWorkDirectory();
        try
        {
            var srcDir = Directory.CreateDirectory(Path.Combine(work.FullName, "src")).FullName;
            var objDir = Directory.CreateDirectory(Path.Combine(work.FullName, "obj")).FullName;
            var pristine = typeof(WasmDispatcherPump).Assembly.Location;
            File.Copy(pristine, Path.Combine(srcDir, "FixtureLib.dll"));

            var (exit, first) = RunScript(shell, Path.Combine(root, "scripts", "wpf", "Ensure-FullAssembly.ps1"),
                "-AssemblyName FixtureLib -SourceDir \"" + srcDir + "\" -IntermediateDir \"" + objDir + "\"", root);
            Assert.Equal(0, exit);
            var staged = Path.Combine(objDir, "full-assembly", "FixtureLib.dll");
            Assert.True(File.Exists(staged));
            Assert.True(File.Exists(staged + ".pristine.sha256"));
            Assert.Equal(File.ReadAllBytes(Path.Combine(srcDir, "FixtureLib.dll")), File.ReadAllBytes(staged));

            var (exit2, second) = RunScript(shell, Path.Combine(root, "scripts", "wpf", "Ensure-FullAssembly.ps1"),
                "-AssemblyName FixtureLib -SourceDir \"" + srcDir + "\" -IntermediateDir \"" + objDir + "\"", root);
            Assert.Equal(0, exit2);
            Assert.Contains("up to date", second, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { work.Delete(recursive: true); } catch (Exception) { }
        }
    }

    [Fact]
    public void Explicit_missing_source_is_hard_error()
    {
        var shell = FindShell();
        if (shell is null) return;
        var root = RepoRoot();
        var work = CreateWorkDirectory();
        try
        {
            var (exit, _) = RunScript(shell, Path.Combine(root, "scripts", "wpf", "Ensure-FullAssembly.ps1"),
                "-AssemblyName FixtureLib -SourceDir \"" + Path.Combine(work.FullName, "nope") + "\" -IntermediateDir \"" + work.FullName + "\"", root);
            Assert.NotEqual(0, exit);
        }
        finally
        {
            try { work.Delete(recursive: true); } catch (Exception) { }
        }
    }

    [Fact]
    public void Undiscoverable_assembly_skips_soft()
    {
        var shell = FindShell();
        if (shell is null) return;
        var root = RepoRoot();
        var work = CreateWorkDirectory();
        try
        {
            var (exit, output) = RunScript(shell, Path.Combine(root, "scripts", "wpf", "Ensure-FullAssembly.ps1"),
                "-AssemblyName NoSuchAssemblyXYZ -IntermediateDir \"" + work.FullName + "\"", root);
            Assert.Equal(0, exit);
            Assert.Contains("skipping", output, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { work.Delete(recursive: true); } catch (Exception) { }
        }
    }
}
