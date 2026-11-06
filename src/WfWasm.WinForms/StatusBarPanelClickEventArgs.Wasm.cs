// Legacy-StatusBarPanelClickEventArgs overlay: replaces the
// runtimes/winforms binary-compat stub (Controls/Unsupported/StatusBar/
// StatusBarPanelClickEventArgs.cs, Compile-Removed in the csproj), whose
// constructor and StatusBarPanel getter unconditionally throw. Working
// model so the StatusBar hit-tested PanelClick path can construct args.
using System.ComponentModel;

namespace System.Windows.Forms;

/// <summary>
///  This type is provided for binary compatibility with .NET Framework and is not intended to be used directly from your code.
/// </summary>
[Obsolete(
    Obsoletions.StatusBarMessage,
    error: false,
    DiagnosticId = Obsoletions.UnsupportedControlsDiagnosticId,
    UrlFormat = Obsoletions.SharedUrlFormat)]
[EditorBrowsable(EditorBrowsableState.Never)]
[Browsable(false)]
public class StatusBarPanelClickEventArgs : MouseEventArgs
{
    public StatusBarPanelClickEventArgs(
        StatusBarPanel? statusBarPanel,
        MouseButtons button,
        int clicks,
        int x,
        int y) : base(button: button, clicks: clicks, x: x, y: y, delta: 0)
    {
        StatusBarPanel = statusBarPanel;
    }

    public StatusBarPanel? StatusBarPanel { get; }
}
