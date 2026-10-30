namespace DesktopWasm.Wpf;

/// <summary>
/// Converted-app codebehind contract (matches
/// System.Windows.Markup.IComponentConnector by name so rewritten
/// generated code binds: the converter maps the framework interface onto
/// this one). The engine hydrates instances through
/// <see cref="WpfAppBoot.LoadComponent"/> and never calls Connect; the
/// generated empty Connect body (no x:Name/event wiring) stays dead.
/// </summary>
public interface IComponentConnector
{
    /// <summary>Generated per-component loader (remapped onto the engine).</summary>
    void InitializeComponent();

    /// <summary>Generated namescope/event wiring (dead without names/events).</summary>
    void Connect(int connectionId, object target);
}
