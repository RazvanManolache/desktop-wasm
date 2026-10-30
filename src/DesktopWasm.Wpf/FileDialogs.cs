using System.ComponentModel;
using DesktopWasm.Core;
using DesktopWasm.Wpf;
using IOPath = System.IO.Path;

namespace Microsoft.Win32;

// File dialogs (matches the Microsoft.Win32 subset the gallery
// FileAndFolderDialogsPage touches: CommonDialog/FileDialog/CommonItemDialog
// plus Open/Save/Folder variants). A browser cannot block on a native
// picker, so ShowDialog runs the configured presenter synchronously and
// returns its verdict: true (picked), false (cancelled) or null
// (indeterminate, e.g. dismissed without a choice). Without a presenter
// ShowDialog is a precise error, never an invented file. Picked bytes live
// in declared WasmVirtualFileSystem mounts; OpenFile resolves through the
// active session instance, unmounted or escaping paths stay loud errors,
// and cancellation (false/null, or OperationCanceledException from the
// presenter) reports as a real state, not a silent skip.

/// <summary>Dialog pick result driver (matches MessageBox.Presenter precedent).</summary>
public abstract class CommonDialog
{
    /// <summary>
    /// Process-wide pick driver: receives the dialog, stages the choice on
    /// it (FileName/FileNames/FolderName) and returns true/false/null.
    /// Prefer the instance <see cref="DialogPresenter"/> (session-isolated);
    /// the static slot exists for host wiring like MessageBox.Presenter.
    /// </summary>
    public static Func<CommonDialog, bool?>? Presenter { get; set; }

    /// <summary>Session-isolated pick driver (checked before <see cref="Presenter"/>).</summary>
    public Func<CommonDialog, bool?>? DialogPresenter { get; set; }

    /// <summary>Dialog caption (retained; the browser picker shows its own chrome).</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Raised after a true pick, before ShowDialog returns (a Cancel veto reports false).</summary>
    public event CancelEventHandler? FileOk;

    /// <summary>Runs the presenter and reports true/false/null (owner accepted for modality parity).</summary>
    public bool? ShowDialog() => RunDialog(IntPtr.Zero);

    /// <summary>Runs the presenter and reports true/false/null (v1 keeps no owner modality gate).</summary>
    public bool? ShowDialog(Window owner)
    {
        _ = owner;
        return RunDialog(IntPtr.Zero);
    }

    /// <summary>Restores defaults (Title here; file state in the derived overrides).</summary>
    public virtual void Reset() => Title = string.Empty;

    /// <summary>
    /// Native-handle pick is unsupported in the browser: the presenter path
    /// above is the mediation. Derived dialogs keep this override point
    /// (bool? instead of WPF's bool so dismissal without a choice survives).
    /// </summary>
    protected virtual bool? RunDialog(IntPtr hwndOwner)
    {
        _ = hwndOwner;
        var pick = DialogPresenter ?? Presenter ??
            throw new InvalidOperationException(
                $"{GetType().Name}.ShowDialog has no presenter: the host must set DialogPresenter (or CommonDialog.Presenter) to a browser-mediated picker; without one there is no file to return.");
        bool? result;
        try
        {
            result = pick(this);
        }
        catch (OperationCanceledException)
        {
            return false; // presenter-level cancellation is the cancelled state
        }
        if (result != true) return result;
        NormalizeSelection();
        ValidateSelection();
        var approval = new CancelEventArgs();
        OnFileOk(approval);
        return !approval.Cancel;
    }

    /// <summary>Applies AddExtension/DefaultExt and name rules before validation.</summary>
    protected virtual void NormalizeSelection() { }

    /// <summary>Enforces existence/path contracts after normalization.</summary>
    protected virtual void ValidateSelection() { }

    /// <summary>Raises <see cref="FileOk"/>.</summary>
    protected virtual void OnFileOk(CancelEventArgs e) => FileOk?.Invoke(this, e);
}

/// <summary>
/// File dialog base (matches Microsoft.Win32.FileDialog for the members the
/// gallery calls, plus the stream entry points). One selection model lives
/// here: Multiselect is accepted on the base (the Save path requires exactly
/// one name). RestoreDirectory is retained without a process current
/// directory to restore (no browser CWD); everything else is enforced.
/// </summary>
public abstract class FileDialog : CommonDialog
{
    private string _fileName = string.Empty;
    private string[] _fileNames = [];

    /// <summary>Picked file (mount-relative virtual path, e.g. lab-uploads/a.txt).</summary>
    public string FileName
    {
        get => _fileName;
        set => _fileName = value ?? string.Empty;
    }

    /// <summary>Picked files (engine extension: public setter so presenters can stage multi-picks).</summary>
    public string[] FileNames
    {
        get => _fileNames;
        set => _fileNames = value ?? [];
    }

    /// <summary>File name without its mount prefix.</summary>
    public string SafeFileName => SafeName(FileName);

    /// <summary>Picked names without mount prefixes.</summary>
    public string[] SafeFileNames => FileNames.Select(SafeName).ToArray();

    /// <summary>Filter spec (label|pattern pairs, patterns ';'-separated; retained and enforced by MatchesFilter).</summary>
    public string Filter { get; set; } = string.Empty;

    /// <summary>1-based selected filter entry (out of range selects no filtering).</summary>
    public int FilterIndex { get; set; } = 1;

    /// <summary>Extension appended when AddExtension applies.</summary>
    public string DefaultExt { get; set; } = string.Empty;

    /// <summary>Initial folder hint (retained; the presenter maps it to its picker).</summary>
    public string InitialDirectory { get; set; } = string.Empty;

    /// <summary>Warns (here: fails) on missing files; default true like the open dialog.</summary>
    public bool CheckFileExists { get; set; } = true;

    /// <summary>Fails on undeclared mounts; default true.</summary>
    public bool CheckPathExists { get; set; } = true;

    /// <summary>Appends DefaultExt when the pick has no extension; default true.</summary>
    public bool AddExtension { get; set; } = true;

    /// <summary>Rejects illegal name characters; default true.</summary>
    public bool ValidateNames { get; set; } = true;

    /// <summary>Shortcut (.lnk) picks are an unsupported boundary; default true.</summary>
    public bool DereferenceLinks { get; set; } = true;

    /// <summary>Retained without a process current directory (browser has no CWD).</summary>
    public bool RestoreDirectory { get; set; }

    /// <summary>Allows several FileNames (the Save path still requires exactly one).</summary>
    public bool Multiselect { get; set; }

    /// <summary>Opens the picked file for reading (mount bytes; single pick required).</summary>
    public virtual Stream OpenFile()
    {
        if (string.IsNullOrEmpty(FileName))
            throw new InvalidOperationException($"{GetType().Name}.OpenFile has no picked file (ShowDialog did not return true).");
        return WasmVirtualFileSystem.Current.OpenRead(FileName);
    }

    /// <summary>Whether the file name passes the selected filter entry.</summary>
    public bool MatchesFilter(string fileName)
    {
        var entries = GetFilterEntries();
        if (entries.Count == 0 || FilterIndex < 1 || FilterIndex > entries.Count) return true;
        var name = IOPath.GetFileName(fileName ?? string.Empty);
        return entries[FilterIndex - 1].Patterns.Any(pattern => MatchesPattern(name, pattern));
    }

    /// <summary>Parses the Filter spec into (label, patterns) entries (odd trailing segment ignored).</summary>
    public IReadOnlyList<(string Label, string[] Patterns)> GetFilterEntries()
    {
        var entries = new List<(string, string[])>();
        if (string.IsNullOrEmpty(Filter)) return entries;
        var parts = Filter.Split('|');
        for (var i = 0; i + 1 < parts.Length; i += 2)
            entries.Add((parts[i], parts[i + 1].Split(';', StringSplitOptions.RemoveEmptyEntries)));
        return entries;
    }

    public override void Reset()
    {
        base.Reset();
        FileName = string.Empty;
        FileNames = [];
        Filter = string.Empty;
        FilterIndex = 1;
        DefaultExt = string.Empty;
        InitialDirectory = string.Empty;
        CheckFileExists = true;
        CheckPathExists = true;
        AddExtension = true;
        ValidateNames = true;
        DereferenceLinks = true;
        RestoreDirectory = false;
        Multiselect = false;
    }

    protected override void NormalizeSelection()
    {
        if (AddExtension && !string.IsNullOrEmpty(DefaultExt))
        {
            if (!string.IsNullOrEmpty(FileName) && !IOPath.HasExtension(SafeName(FileName)))
                FileName += "." + DefaultExt.TrimStart('.');
            FileNames = FileNames
                .Select(name => !string.IsNullOrEmpty(name) && !IOPath.HasExtension(SafeName(name))
                    ? name + "." + DefaultExt.TrimStart('.')
                    : name)
                .ToArray();
        }
        if (ValidateNames)
        {
            foreach (var name in FileNames.Prepend(FileName))
            {
                if (string.IsNullOrEmpty(name)) continue;
                var leaf = SafeName(name);
                var bad = leaf.IndexOfAny(IOPath.GetInvalidFileNameChars());
                if (bad >= 0)
                    throw new InvalidOperationException(
                        $"FileDialog pick '{name}' contains an invalid name character '{leaf[bad]}'.");
            }
        }
        if (DereferenceLinks && FileNames.Prepend(FileName)
                .Any(name => name.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException(
                "FileDialog shortcut (.lnk) picks need host link resolution, which v1 does not implement.");
    }

    protected override void ValidateSelection()
    {
        var names = FileNames.Length > 0 ? FileNames : (string.IsNullOrEmpty(FileName) ? [] : [FileName]);
        if (CheckPathExists || CheckFileExists)
        {
            foreach (var name in names)
            {
                // Exists resolves the mount (undeclared mount is InvalidOperationException).
                if (!WasmVirtualFileSystem.Current.Exists(name))
                {
                    if (CheckFileExists)
                        throw new FileNotFoundException($"FileDialog pick '{name}' is not staged in a declared virtual mount.", name);
                    if (CheckPathExists)
                        _ = WasmVirtualFileSystem.Current.ListFiles(MountOf(name));
                }
            }
            if (names.Length == 0 && CheckFileExists)
                throw new FileNotFoundException("FileDialog pick is empty (no file was staged by the presenter).");
        }
    }

    private static string SafeName(string path)
    {
        if (string.IsNullOrEmpty(path)) return string.Empty;
        var leaf = path.Replace('\\', '/');
        var slash = leaf.LastIndexOf('/');
        return slash >= 0 ? leaf[(slash + 1)..] : leaf;
    }

    private static string MountOf(string path)
    {
        var slash = (path ?? string.Empty).IndexOf('/');
        if (slash <= 0)
            throw new InvalidOperationException($"FileDialog pick '{path}' must be mount-relative ('mount/relative/name').");
        return path![..slash];
    }

    private static bool MatchesPattern(string name, string pattern)
    {
        var candidate = (pattern ?? string.Empty).Trim();
        if (candidate is "" or "*" or "*.*") return true;
        if (candidate.StartsWith("*.", StringComparison.Ordinal))
            return name.EndsWith(candidate[1..], StringComparison.OrdinalIgnoreCase);
        if (candidate.StartsWith("*", StringComparison.Ordinal))
            return name.EndsWith(candidate[1..], StringComparison.OrdinalIgnoreCase);
        return string.Equals(name, candidate, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>Common-item dialog base (matches Microsoft.Win32.CommonItemDialog).</summary>
public abstract class CommonItemDialog : CommonDialog
{
    /// <summary>Initial folder hint (retained; the presenter maps it to its picker).</summary>
    public string InitialDirectory { get; set; } = string.Empty;

    public override void Reset()
    {
        base.Reset();
        InitialDirectory = string.Empty;
    }
}

/// <summary>Open dialog (matches Microsoft.Win32.OpenFileDialog).</summary>
public sealed class OpenFileDialog : FileDialog
{
}

/// <summary>
/// Save dialog (matches Microsoft.Win32.SaveFileDialog): existence is not
/// required (CheckFileExists defaults false) and OpenFile returns a writable
/// stream that commits into the declared mount on Dispose.
/// </summary>
public sealed class SaveFileDialog : FileDialog
{
    public SaveFileDialog() => CheckFileExists = false;

    /// <summary>Whether overwriting prompts (retained; the presenter owns the prompt).</summary>
    public bool OverwritePrompt { get; set; } = true;

    /// <summary>Whether creation prompts (retained; the presenter owns the prompt).</summary>
    public bool CreatePrompt { get; set; }

    public override Stream OpenFile()
    {
        if (string.IsNullOrEmpty(FileName))
            throw new InvalidOperationException("SaveFileDialog.OpenFile has no picked file (ShowDialog did not return true).");
        if (FileNames.Length > 1)
            throw new InvalidOperationException("SaveFileDialog cannot save several files at once.");
        return WasmVirtualFileSystem.Current.OpenWrite(FileName);
    }

    public override void Reset()
    {
        base.Reset();
        CheckFileExists = false;
        OverwritePrompt = true;
        CreatePrompt = false;
    }
}

/// <summary>
/// Folder dialog (matches Microsoft.Win32.OpenFolderDialog): the pick is a
/// mount-relative folder path; any path under a declared mount is valid.
/// </summary>
public sealed class OpenFolderDialog : CommonItemDialog
{
    private string _folderName = string.Empty;
    private string[] _folderNames = [];

    /// <summary>Picked folder (mount-relative virtual path).</summary>
    public string FolderName
    {
        get => _folderName;
        set => _folderName = value ?? string.Empty;
    }

    /// <summary>Picked folders (engine extension: public setter for presenter staging).</summary>
    public string[] FolderNames
    {
        get => _folderNames;
        set => _folderNames = value ?? [];
    }

    /// <summary>Allows several FolderNames.</summary>
    public bool Multiselect { get; set; }

    public override void Reset()
    {
        base.Reset();
        FolderName = string.Empty;
        FolderNames = [];
        Multiselect = false;
    }

    protected override void ValidateSelection()
    {
        var names = FolderNames.Length > 0 ? FolderNames : (string.IsNullOrEmpty(FolderName) ? [] : [FolderName]);
        if (names.Length == 0)
            throw new InvalidOperationException("OpenFolderDialog pick is empty (no folder was staged by the presenter).");
        foreach (var name in names)
        {
            // ListFiles resolves the mount (a bare mount name is its own
            // root); undeclared mounts stay loud errors.
            _ = WasmVirtualFileSystem.Current.ListFiles(MountOf(name));
        }
    }

    private static string MountOf(string path)
    {
        var slash = (path ?? string.Empty).IndexOf('/');
        if (slash < 0)
        {
            if (!string.IsNullOrEmpty(path)) return path;
            throw new InvalidOperationException("OpenFolderDialog pick is empty (no folder was staged by the presenter).");
        }
        if (slash == 0)
            throw new InvalidOperationException($"OpenFolderDialog pick '{path}' must be mount-relative ('mount/relative/name').");
        return path![..slash];
    }
}
