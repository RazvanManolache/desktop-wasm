// Legacy-StatusBarPanel overlay: replaces the runtimes/winforms
// binary-compat stub (Controls/Unsupported/StatusBar/StatusBarPanel.cs,
// Compile-Removed in the csproj), whose constructor and every property
// getter unconditionally throw. Working in-memory model: the app's used
// text/panel lifecycle (Text, Width, Alignment, ToolTipText, Style,
// Icon, MinWidth, Name, Tag, AutoSize, BorderStyle, Parent backref).
// Signatures and attributes mirror the upstream stub exactly.
using System.ComponentModel;
using System.Drawing;

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
[ToolboxItem(false)]
[DesignTimeVisible(false)]
[DefaultProperty(nameof(Text))]
public class StatusBarPanel : Component, ISupportInitialize
{
    private HorizontalAlignment _alignment = HorizontalAlignment.Left;
    private StatusBarPanelAutoSize _autoSize = StatusBarPanelAutoSize.None;
    private StatusBarPanelBorderStyle _borderStyle = StatusBarPanelBorderStyle.Sunken;
    private Icon? _icon;
    private int _minWidth = 10;
    private string _name = string.Empty;
    private StatusBarPanelStyle _style = StatusBarPanelStyle.Text;
    private object? _tag;
    private string _text = string.Empty;
    private string _toolTipText = string.Empty;
    private int _width = 100;

    public StatusBarPanel()
    {
    }

    [DefaultValue(HorizontalAlignment.Left)]
    [Localizable(true)]
    public HorizontalAlignment Alignment
    {
        get => _alignment;
        set => _alignment = value;
    }

    [DefaultValue(StatusBarPanelAutoSize.None)]
    [RefreshProperties(RefreshProperties.All)]
    public StatusBarPanelAutoSize AutoSize
    {
        get => _autoSize;
        set => _autoSize = value;
    }

    [DefaultValue(StatusBarPanelBorderStyle.Sunken)]
    [Runtime.InteropServices.DispId(-504)]
    public StatusBarPanelBorderStyle BorderStyle
    {
        get => _borderStyle;
        set => _borderStyle = value;
    }

    [DefaultValue(null)]
    [Localizable(true)]
    public Icon? Icon
    {
        get => _icon;
        set => _icon = value;
    }

    [DefaultValue(10)]
    [Localizable(true)]
    [RefreshProperties(RefreshProperties.All)]
    public int MinWidth
    {
        get => _minWidth;
        set => _minWidth = value;
    }

    [Localizable(true)]
    public string Name
    {
        get => _name;
        set => _name = value ?? string.Empty;
    }

    [Browsable(false)]
    public StatusBar? Parent { get; internal set; }

    [DefaultValue(StatusBarPanelStyle.Text)]
    public StatusBarPanelStyle Style
    {
        get => _style;
        set => _style = value;
    }

    [Localizable(false)]
    [Bindable(true)]
    [DefaultValue(null)]
    [TypeConverter(typeof(StringConverter))]
    public object? Tag
    {
        get => _tag;
        set => _tag = value;
    }

    [Localizable(true)]
    [DefaultValue("")]
    public string Text
    {
        get => _text;
        set => _text = value ?? string.Empty;
    }

    [Localizable(true)]
    [DefaultValue("")]
    public string ToolTipText
    {
        get => _toolTipText;
        set => _toolTipText = value ?? string.Empty;
    }

    [Localizable(true)]
    [DefaultValue(100)]
    public int Width
    {
        get => _width;
        set => _width = value;
    }

    public void BeginInit() { }

    public void EndInit() { }

    public override string ToString() => Text;
}
