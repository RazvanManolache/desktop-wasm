// Legacy-StatusBar overlay: replaces the runtimes/winforms binary-compat stub
// (Controls/Unsupported/StatusBar/StatusBar.cs, Compile-Removed in the
// csproj), whose constructor unconditionally throws
// PlatformNotSupportedException. Framework-era apps (Reflector builds its
// ApplicationBar on a StatusBar subclass) need a working in-memory model:
// construction, Text, Panels add/count/text, ShowPanels, SizingGrip, and a
// hit-tested PanelClick. There is no native STATUSCLASSNAME window in the
// browser: rendering is the canvas Control path plus the host's
// StatusBar-name branch, Handle stays default, no P/Invoke.
// Signatures and attributes mirror the upstream stub exactly so the rest of
// the fork keeps compiling.
// Regression: PNSE StatusBar..ctor <- ApplicationBar..ctor <- Reflector
// MainForm create (form-create fails before first paint).
using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;

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
[ComVisible(true)]
[ClassInterface(ClassInterfaceType.AutoDispatch)]
[DefaultEvent(nameof(PanelClick))]
[DefaultProperty(nameof(Text))]
[Designer($"System.Windows.Forms.Design.StatusBarDesigner, {Assemblies.SystemDesign}")]
public partial class StatusBar : Control
{
    private readonly StatusBarPanelCollection _panels;

    public StatusBar()
    {
        _panels = new StatusBarPanelCollection(this);
        base.Dock = DockStyle.Bottom;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public override Color BackColor
    {
        get => base.BackColor;
        set => base.BackColor = value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public override Image BackgroundImage
    {
        get => base.BackgroundImage;
        set => base.BackgroundImage = value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public override ImageLayout BackgroundImageLayout
    {
        get => base.BackgroundImageLayout;
        set => base.BackgroundImageLayout = value;
    }

    [Localizable(true)]
    [DefaultValue(DockStyle.Bottom)]
    public override DockStyle Dock
    {
        get => base.Dock;
        set => base.Dock = value;
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    protected override bool DoubleBuffered
    {
        get => base.DoubleBuffered;
        set => base.DoubleBuffered = value;
    }

    [Localizable(true)]
    public override Font Font
    {
        get => base.Font;
        set => base.Font = value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public override Color ForeColor
    {
        get => base.ForeColor;
        set => base.ForeColor = value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new ImeMode ImeMode
    {
        get => base.ImeMode;
        set => base.ImeMode = value;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Content)]
    [Localizable(true)]
    [MergableProperty(false)]
    public StatusBarPanelCollection Panels => _panels;

    [DefaultValue(false)]
    public bool ShowPanels { get; set; }

    [DefaultValue(true)]
    public bool SizingGrip { get; set; } = true;

    [DefaultValue(false)]
    public new bool TabStop { get; set; }

    [Localizable(true)]
    public override string Text
    {
        get => base.Text;
        set
        {
            base.Text = value;
            Invalidate();
        }
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new event EventHandler? BackColorChanged;

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new event EventHandler? BackgroundImageChanged;

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new event EventHandler? BackgroundImageLayoutChanged;

    public event StatusBarDrawItemEventHandler? DrawItem;

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new event EventHandler? ForeColorChanged;

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new event EventHandler? ImeModeChanged;

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new event PaintEventHandler? Paint;

    public event StatusBarPanelClickEventHandler? PanelClick;

    protected virtual void OnPanelClick(StatusBarPanelClickEventArgs e) => PanelClick?.Invoke(this, e);

    protected virtual void OnDrawItem(StatusBarDrawItemEventArgs sbdievent) => DrawItem?.Invoke(this, sbdievent);

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (!ShowPanels || Panels.Count == 0)
            return;
        int x = 0;
        foreach (StatusBarPanel panel in Panels)
        {
            int w = Math.Max(0, panel.Width);
            if (e.X >= x && e.X < x + w)
            {
                OnPanelClick(new StatusBarPanelClickEventArgs(panel, e.Button, e.Clicks, e.X, e.Y));
                return;
            }
            x += w;
        }
    }
}
