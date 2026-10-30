namespace DesktopWasm.AppModel;

/// <summary>Framework capabilities an app needs. Both flags set means mixed:
/// one session hosts WinForms and WPF together (see CATALOG-CONTRACT.md).
/// Never an exclusive enum: a single assembly may reference both.</summary>
[Flags]
public enum AppCapability
{
    None = 0,
    WinForms = 1,
    Wpf = 2,
    Mixed = WinForms | Wpf,
}
