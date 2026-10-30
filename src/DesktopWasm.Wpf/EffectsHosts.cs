using System.IO;
using System.Reflection;
using System.Security;
using System.Xml;
using System.Xml.Linq;
using DesktopWasm.Graphics;

namespace DesktopWasm.Wpf;

// Effects/hosts slice (ScreenToGif effects/host cluster, shim, not port).
//
// Covered gaps: Effect + DropShadowEffect + RenderingBias
// (PresentationCore System.Windows.Media.Effects), RenderMode
// (PresentationCore System.Windows.Interop, driven through
// RenderOptions.ProcessRenderMode), ContainerVisual
// (PresentationCore System.Windows.Media), PasswordBox + WebBrowser
// (PresentationFramework System.Windows.Controls).
//
// Verified against real WPF (GAC PresentationCore/PresentationFramework):
// RenderingBias Performance=0/Quality=1, RenderMode Default=0/SoftwareOnly=1,
// DropShadowEffect defaults Color #FF000000 Direction 315 ShadowDepth 5
// Opacity 1 BlurRadius 5 RenderingBias Performance, PasswordBox : Control with
// PasswordChar '*' (42) and MaxLength 0, PasswordChanged routed through
// RoutedEventHandler, ContainerVisual : Visual with VisualCollection
// Children, DrawingVisual : ContainerVisual (so drawingVisual.Effect binds
// to the ContainerVisual Effect slot), Effect declared on UIElement AND on
// ContainerVisual sharing one DependencyProperty (ContainerVisual carries no
// own EffectProperty field), RenderOptions.ProcessRenderMode a static
// get/set of RenderMode.

/// <summary>Drop-shadow quality hint (matches System.Windows.Media.Effects.RenderingBias).</summary>
public enum RenderingBias
{
    Performance = 0,
    Quality = 1,
}

/// <summary>Compositor mode (matches System.Windows.Interop.RenderMode).</summary>
public enum RenderMode
{
    Default = 0,
    SoftwareOnly = 1,
}

/// <summary>
/// Effect root (matches System.Windows.Media.Effects.Effect : Animatable).
/// </summary>
public abstract class Effect : Animatable
{
}

/// <summary>
/// Drop shadow (matches System.Windows.Media.Effects.DropShadowEffect).
/// Plain CLR properties with the verified WPF defaults; every mutation
/// raises <see cref="Changed"/> so the owning visual repaints (converted
/// ScreenToGif ShadowPanel/Editor XAML and drawingVisual.Effect code).
/// </summary>
public sealed class DropShadowEffect : Effect
{
    private Color _color = Color.FromArgb(0xFF, 0, 0, 0);
    private double _direction = 315;
    private double _shadowDepth = 5;
    private double _opacity = 1;
    private double _blurRadius = 5;
    private RenderingBias _renderingBias = RenderingBias.Performance;

    /// <summary>Raised after any property changes (owners repaint).</summary>
    public event Action? Changed;

    /// <summary>Shadow color (default opaque black).</summary>
    public Color Color
    {
        get => _color;
        set { if (_color.Equals(value)) return; _color = value; Changed?.Invoke(); }
    }

    /// <summary>Shadow direction in degrees (default 315).</summary>
    public double Direction
    {
        get => _direction;
        set { if (_direction == value) return; _direction = value; Changed?.Invoke(); }
    }

    /// <summary>Shadow offset in pixels (default 5).</summary>
    public double ShadowDepth
    {
        get => _shadowDepth;
        set { if (_shadowDepth == value) return; _shadowDepth = value; Changed?.Invoke(); }
    }

    /// <summary>Shadow opacity 0..1 (default 1).</summary>
    public double Opacity
    {
        get => _opacity;
        set { if (_opacity == value) return; _opacity = value; Changed?.Invoke(); }
    }

    /// <summary>Blur radius in pixels (default 5; 0 is a hard offset).</summary>
    public double BlurRadius
    {
        get => _blurRadius;
        set { if (_blurRadius == value) return; _blurRadius = value; Changed?.Invoke(); }
    }

    /// <summary>Quality hint (default Performance).</summary>
    public RenderingBias RenderingBias
    {
        get => _renderingBias;
        set { if (_renderingBias == value) return; _renderingBias = value; Changed?.Invoke(); }
    }

    protected override Freezable CreateInstanceCore() => new DropShadowEffect();

    /// <summary>Deep copy (CLR fields ride along; handlers do not).</summary>
    public new DropShadowEffect Clone() => (DropShadowEffect)base.Clone();
}

/// <summary>Blur sampling kernel (matches KernelType; Gaussian=0 live-verified).</summary>
public enum KernelType
{
    /// <summary>Gaussian blur (the default).</summary>
    Gaussian = 0,

    /// <summary>Box blur.</summary>
    Box = 1,
}

/// <summary>
/// Blur effect (matches System.Windows.Media.Effects.BlurEffect):
/// store-backed Radius/KernelType/RenderingBias with the live WPF defaults
/// (radius 5, Gaussian, Performance bias). The converted MaterialDesign
/// drawer/dialog code constructs it and drives Radius in code; the
/// transport has no blur rasterization op, so v1 carries the values
/// without painting the blur (documented, like the retained
/// AllowsTransparency/Watermark visual limits elsewhere).
/// </summary>
public sealed class BlurEffect : Effect
{
    /// <summary>Identifies the Radius store.</summary>
    public static readonly DependencyProperty RadiusProperty =
        DependencyProperty.Register("Radius", typeof(double), typeof(BlurEffect), 5.0);

    /// <summary>Identifies the KernelType store.</summary>
    public static readonly DependencyProperty KernelTypeProperty =
        DependencyProperty.Register("KernelType", typeof(KernelType), typeof(BlurEffect), KernelType.Gaussian);

    /// <summary>Identifies the RenderingBias store.</summary>
    public static readonly DependencyProperty RenderingBiasProperty =
        DependencyProperty.Register("RenderingBias", typeof(RenderingBias), typeof(BlurEffect), RenderingBias.Performance);

    /// <summary>Empty effect with the live WPF defaults.</summary>
    public BlurEffect()
    {
    }

    /// <summary>Blur radius in pixels (default 5, live-verified).</summary>
    public double Radius
    {
        get => (double)GetValue(RadiusProperty)!;
        set => SetValue(RadiusProperty, value);
    }

    /// <summary>Sampling kernel (default Gaussian, live-verified).</summary>
    public KernelType KernelType
    {
        get => (KernelType)GetValue(KernelTypeProperty)!;
        set => SetValue(KernelTypeProperty, value);
    }

    /// <summary>Quality hint (default Performance, live-verified).</summary>
    public RenderingBias RenderingBias
    {
        get => (RenderingBias)GetValue(RenderingBiasProperty)!;
        set => SetValue(RenderingBiasProperty, value);
    }

    /// <inheritdoc />
    protected override Freezable CreateInstanceCore() => new BlurEffect();

    /// <summary>Deep copy (stores ride along).</summary>
    public new BlurEffect Clone() => (BlurEffect)base.Clone();
}

/// <summary>
/// Visual container (matches System.Windows.Media.ContainerVisual):
/// a Visual whose <see cref="Children"/> parent through the shared visual
/// adoption core, so VisualTreeHelper counts and replay see them.
/// The Effect slot reuses <see cref="UIElement.EffectProperty"/> (real WPF
/// carries no own EffectProperty field here either).
/// </summary>
public class ContainerVisual : Visual
{
    /// <summary>Hosted visuals (converted adorners/selection visuals land here).</summary>
    public VisualCollection Children { get; } = null!;

    public ContainerVisual()
    {
        Children = new VisualCollection(this);
    }

    /// <summary>
    /// Visual effect (matches ContainerVisual.Effect; DrawingVisual inherits
    /// this slot, so converted drawingVisual.Effect assignments bind).
    /// </summary>
    public Effect? Effect
    {
        get => (Effect?)GetValue(UIElement.EffectProperty);
        set => SetValue(UIElement.EffectProperty, value);
    }

    /// <summary>
    /// Replays hosted visuals into a drawing context (DrawingVisual
    /// recordings first, nested containers recursively). Visual-level
    /// shadows need arranged bounds, so replay carries content only;
    /// arranged UIElement shadows paint in the element render path.
    /// </summary>
    public void Render(DrawingContext dc)
    {
        ArgumentNullException.ThrowIfNull(dc);
        foreach (Visual child in Children) RenderChild(dc, child);
    }

    internal static void RenderChild(DrawingContext dc, Visual child)
    {
        switch (child)
        {
            case DrawingVisual drawing:
                foreach (var command in drawing.Recorded) dc.Append(command);
                break;
            case ContainerVisual container:
                container.Render(dc);
                break;
        }
    }
}

/// <summary>Effect paint + binding-bridge helpers (UIElement render path calls in).</summary>
internal static class EffectPaint
{
    /// <summary>
    /// Paints the shadow underlay for an arranged element. The transport has
    /// no blur op, so a positive BlurRadius paints expanding rings with
    /// falling alpha (deterministic soft edge on every backend); radius 0
    /// paints one hard offset rect. Non-drop effects paint nothing.
    /// Direction follows WPF degrees (0 points right, 90 points down).
    /// </summary>
    internal static void PaintUnderlay(DrawingContext dc, WpfRect bounds, Effect? effect)
    {
        if (effect is not DropShadowEffect shadow) return;
        if (shadow.Opacity <= 0 || bounds.Width <= 0 || bounds.Height <= 0) return;
        var radians = shadow.Direction * Math.PI / 180.0;
        var dx = shadow.ShadowDepth * Math.Cos(radians);
        var dy = shadow.ShadowDepth * Math.Sin(radians);
        var blur = Math.Max(0, shadow.BlurRadius);
        var x = (float)(bounds.X + dx);
        var y = (float)(bounds.Y + dy);
        var w = (float)bounds.Width;
        var h = (float)bounds.Height;
        if (blur <= 0 || double.IsNaN(blur) || double.IsInfinity(blur))
        {
            dc.DrawRectangle(x, y, w, h, WithOpacity(shadow.Color, shadow.Opacity));
            return;
        }
        var rings = Math.Max(1, (int)Math.Ceiling(blur / 2.0));
        for (var i = rings; i >= 1; i--)
        {
            var spread = (float)(i * 2);
            var alpha = shadow.Opacity * (rings - i + 1) / (rings * (rings + 1) / 2.0);
            dc.DrawRectangle(x - spread, y - spread, w + spread * 2, h + spread * 2,
                WithOpacity(shadow.Color, alpha));
        }
    }

    internal static uint WithOpacity(Color color, double opacity)
    {
        var a = (uint)Math.Clamp((int)Math.Round(color.A * Math.Clamp(opacity, 0, 1)), 0, 255);
        return ((uint)color.R << 24) | ((uint)color.G << 16) | ((uint)color.B << 8) | a;
    }

    /// <summary>
    /// Pushes one attached "Effect.*" landing into the live effect (called
    /// from UIElement.SetAttached, so XAML {Binding} values on effect
    /// sub-properties flow through the normal binding engine and land here
    /// on every push, initial and INPC-driven).
    /// </summary>
    internal static void PushAttached(UIElement owner, string name, object? value)
    {
        if (!name.StartsWith("Effect.", StringComparison.Ordinal)) return;
        if (owner.Effect is not Effect effect) return;
        var leaf = name["Effect.".Length..];
        var prop = effect.GetType().GetProperty(leaf,
            BindingFlags.Public | BindingFlags.Instance);
        if (prop is null || !prop.CanWrite || prop.SetMethod?.IsPublic != true) return;
        try
        {
            prop.SetValue(effect, value is null ? null :
                BindingExpression.ConvertValue(value, prop.PropertyType));
        }
        catch (Exception ex) when (ex is TargetInvocationException or ArgumentException or InvalidCastException or FormatException or OverflowException)
        {
            // WPF keeps the old effect value on a mistyped landing.
        }
    }

    /// <summary>Effect sub-property names (attached bridge + XAML parsing share the list).</summary>
    internal static readonly string[] SubProperties =
        ["Color", "Direction", "ShadowDepth", "Opacity", "BlurRadius", "RenderingBias", "Radius", "KernelType"];

    /// <summary>
    /// Pulls already-landed "Effect.*" attached values into a freshly
    /// assigned effect (bindings attached before the property element ran).
    /// </summary>
    internal static void PullAttached(UIElement owner)
    {
        if (owner.Effect is not Effect effect) return;
        foreach (var leaf in SubProperties)
        {
            var landed = owner.GetAttached("Effect." + leaf);
            if (landed is null) continue;
            var prop = effect.GetType().GetProperty(leaf,
                BindingFlags.Public | BindingFlags.Instance);
            if (prop is null || !prop.CanWrite || prop.SetMethod?.IsPublic != true) continue;
            try { prop.SetValue(effect, BindingExpression.ConvertValue(landed, prop.PropertyType)); }
            catch (Exception ex) when (ex is TargetInvocationException or ArgumentException or InvalidCastException or FormatException or OverflowException)
            {
            }
        }
    }
}

/// <summary>
/// Masked password editor (matches System.Windows.Controls.PasswordBox).
/// Editing mirrors TextBox (focus-gated keys, caret, selection, MaxLength)
/// but paint shows PasswordChar glyphs and the text never lands in a
/// bindable Text property; PasswordChanged notifies subscribers after
/// every mutation. Verified WPF surface: PasswordChar '*' default,
/// MaxLength 0 (unlimited), PasswordChanged via RoutedEventHandler.
/// </summary>
public class PasswordBox : Control
{
    private const int Pad = 4;
    private string _password = string.Empty;
    private int _selectionStart;
    private int _selectionLength;
    private bool _focused;
    private char _passwordChar = '*';
    private int _maxLength;

    public PasswordBox()
    {
    }

    public static readonly RoutedEvent PasswordChangedEvent = EventManager.RegisterRoutedEvent(
        "PasswordChanged", RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(PasswordBox));

    /// <summary>Raised after the password changes (typing, code, selection edits).</summary>
    public event RoutedEventHandler? PasswordChanged;

    /// <summary>Selection fill DP (matches PasswordBox.SelectionBrush, owned here like native).</summary>
    public static readonly DependencyProperty SelectionBrushProperty =
        DependencyProperty.Register("SelectionBrush", typeof(Brush), typeof(PasswordBox),
            new FrameworkPropertyMetadata(null));

    /// <summary>Caret paint DP (matches PasswordBox.CaretBrush, owned here like native).</summary>
    public static readonly DependencyProperty CaretBrushProperty =
        DependencyProperty.Register("CaretBrush", typeof(Brush), typeof(PasswordBox),
            new FrameworkPropertyMetadata(null));

    /// <summary>Selection translucency DP (matches PasswordBox.SelectionOpacity, owned here like native).</summary>
    public static readonly DependencyProperty SelectionOpacityProperty =
        DependencyProperty.Register("SelectionOpacity", typeof(double), typeof(PasswordBox),
            new FrameworkPropertyMetadata(0.4));

    /// <summary>Selected-text paint DP (matches PasswordBox.SelectionTextBrush, owned here like native).</summary>
    public static readonly DependencyProperty SelectionTextBrushProperty =
        DependencyProperty.Register("SelectionTextBrush", typeof(Brush), typeof(PasswordBox),
            new FrameworkPropertyMetadata(null));

    /// <summary>Selection fill (matches PasswordBox.SelectionBrush).</summary>
    public Brush? SelectionBrush
    {
        get => (Brush?)GetValue(SelectionBrushProperty);
        set => SetValue(SelectionBrushProperty, value);
    }

    /// <summary>Caret paint (matches PasswordBox.CaretBrush; null keeps the legacy caret).</summary>
    public Brush? CaretBrush
    {
        get => (Brush?)GetValue(CaretBrushProperty);
        set => SetValue(CaretBrushProperty, value);
    }

    /// <summary>Selection translucency (matches PasswordBox.SelectionOpacity).</summary>
    public double SelectionOpacity
    {
        get => (double)(GetValue(SelectionOpacityProperty) ?? 0.4);
        set => SetValue(SelectionOpacityProperty, value);
    }

    /// <summary>Selected-text paint (matches PasswordBox.SelectionTextBrush).</summary>
    public Brush? SelectionTextBrush
    {
        get => (Brush?)GetValue(SelectionTextBrushProperty);
        set => SetValue(SelectionTextBrushProperty, value);
    }

    /// <summary>Current password (null coerces to empty; MaxLength trims).</summary>
    public string Password
    {
        get => _password;
        set
        {
            value ??= string.Empty;
            if (_maxLength > 0 && value.Length > _maxLength) value = value[.._maxLength];
            if (_password == value) return;
            _password = value;
            SelectionLength = 0;
            CaretIndex = Math.Min(CaretIndex, _password.Length);
            InvalidateMeasure();
            OnPasswordChanged();
        }
    }

    /// <summary>Masking glyph (default '*', the verified WPF default).</summary>
    public char PasswordChar
    {
        get => _passwordChar;
        set
        {
            if (_passwordChar == value) return;
            _passwordChar = value;
            InvalidateVisual();
        }
    }

    /// <summary>Maximum length (0 means unlimited, the WPF default).</summary>
    public int MaxLength
    {
        get => _maxLength;
        set
        {
            if (value < 0) throw new ArgumentOutOfRangeException(nameof(value), "MaxLength is non-negative.");
            if (_maxLength == value) return;
            _maxLength = value;
            if (_maxLength > 0 && _password.Length > _maxLength) Password = _password;
        }
    }

    /// <summary>Password copy as SecureString (fresh instance per call).</summary>
    public SecureString SecurePassword
    {
        get
        {
            var secure = new SecureString();
            foreach (var ch in _password) secure.AppendChar(ch);
            return secure;
        }
    }

    /// <summary>Insertion point (setting it collapses the selection).</summary>
    public int CaretIndex
    {
        get => SelectionStart + SelectionLength;
        set
        {
            SelectionStart = value;
            SelectionLength = 0;
        }
    }

    /// <summary>Anchor of the selection (clamped to the password).</summary>
    public int SelectionStart
    {
        get => _selectionStart;
        set
        {
            var clamped = Math.Clamp(value, 0, Password.Length);
            if (_selectionStart == clamped) return;
            _selectionStart = clamped;
            SelectionLength = Math.Min(SelectionLength, Password.Length - _selectionStart);
            InvalidateVisual();
        }
    }

    /// <summary>Selected length from the anchor (clamped to the password).</summary>
    public int SelectionLength
    {
        get => _selectionLength;
        set
        {
            var clamped = Math.Clamp(value, 0, Math.Max(0, Password.Length - SelectionStart));
            if (_selectionLength == clamped) return;
            _selectionLength = clamped;
            InvalidateVisual();
        }
    }

    /// <summary>Selected text (real chars; setting it replaces the selection).</summary>
    public string SelectedText
    {
        get => SelectionLength == 0 ? string.Empty : Password.Substring(SelectionStart, SelectionLength);
        set
        {
            if (IsReadOnlyBox) return;
            var start = SelectionStart;
            Password = Password.Remove(SelectionStart, SelectionLength).Insert(start, value ?? string.Empty);
            SelectionStart = start + (value?.Length ?? 0);
        }
    }

    /// <summary>Read-only boxes keep focus and selection but reject edits.</summary>
    public bool IsReadOnlyBox { get; set; }

    /// <summary>Selects the whole password.</summary>
    public void SelectAll()
    {
        SelectionStart = 0;
        SelectionLength = Password.Length;
    }

    /// <summary>Selects a range (matches PasswordBox.Select; internal like WPF).</summary>
    internal void Select(int start, int length)
    {
        if (start < 0) throw new ArgumentOutOfRangeException(nameof(start), "Selection start is non-negative.");
        if (length < 0) throw new ArgumentOutOfRangeException(nameof(length), "Selection length is non-negative.");
        SelectionStart = start;
        SelectionLength = length;
    }

    /// <summary>Live text selection (matches PasswordBox.Selection; internal like WPF).</summary>
    internal System.Windows.Documents.ITextSelection Selection => new PasswordBoxSelection(this);

    /// <summary>Live password selection (backs <see cref="Selection"/>).</summary>
    private sealed class PasswordBoxSelection(PasswordBox box) : System.Windows.Documents.ITextSelection
    {
        public System.Windows.Documents.ITextPointer Start =>
            new System.Windows.Controls.PasswordTextPointer(Math.Min(box.SelectionStart, box.SelectionStart + box.SelectionLength));

        public System.Windows.Documents.ITextPointer End =>
            new System.Windows.Controls.PasswordTextPointer(Math.Max(box.SelectionStart, box.SelectionStart + box.SelectionLength));
    }

    /// <summary>Clears the password (raises PasswordChanged when non-empty).</summary>
    public void Clear() => Password = string.Empty;

    /// <summary>Pastes session text at the caret (denied clipboard pastes nothing).</summary>
    public void Paste()
    {
        if (IsReadOnlyBox) return;
        string text;
        try { text = Clipboard.GetText(); }
        catch (Exception ex) when (ex is ClipboardException or NotSupportedException or InvalidOperationException)
        {
            return;
        }
        if (string.IsNullOrEmpty(text)) return;
        DeleteSelection();
        var room = _maxLength > 0 ? Math.Max(0, _maxLength - Password.Length) : text.Length;
        var insert = room >= text.Length ? text : text[..room];
        Password = Password.Insert(CaretIndex, insert);
        CaretIndex += insert.Length;
    }

    /// <summary>Copy is disabled (matches WPF: the password never reaches the clipboard).</summary>
    public void Copy()
    {
    }

    protected virtual void OnPasswordChanged()
    {
        var args = new RoutedEventArgs { RoutedEvent = PasswordChangedEvent, Source = this, OriginalSource = this };
        PasswordChanged?.Invoke(this, args);
    }

    public override bool IsFocused => _focused;

    /// <summary>Password boxes take focus (store-backed; the Control default is true).</summary>
    public override bool Focusable
    {
        get => (bool?)GetValue(FocusableProperty) ?? true;
        set => SetValue(FocusableProperty, value);
    }

    public override void OnGotFocus()
    {
        _focused = true;
        InvalidateVisual();
    }

    public override void OnLostFocus()
    {
        _focused = false;
        InvalidateVisual();
    }

    private string _fontCss = "14px sans-serif";

    /// <summary>Font description (changes measured size).</summary>
    public string FontCss
    {
        get => _fontCss;
        set
        {
            if (_fontCss == value) return;
            _fontCss = value;
            InvalidateMeasure();
        }
    }

    private uint _foregroundRgba = 0x000000FF;

    /// <summary>Text color (paint-only change).</summary>
    public uint ForegroundRgba
    {
        get => _foregroundRgba;
        set
        {
            if (_foregroundRgba == value) return;
            _foregroundRgba = value;
            InvalidateVisual();
        }
    }

    /// <summary>Field fill (paint-only; set directly).</summary>
    public uint BackgroundRgba { get; set; } = 0xFFFFFFFF;

    /// <summary>1px border color (paint-only; set directly).</summary>
    public uint BorderRgba { get; set; } = 0x707070FF;

    /// <summary>Masked display (one glyph per char).</summary>
    internal string MaskedText => new string(PasswordChar, Password.Length);

    protected override WpfSize MeasureCore(WpfSize available)
    {
        var (shapedW, shapedH) = TryMeasure(MaskedText, FontCss, Password.Length * 8, 20);
        return new(
            double.IsNaN(Width) ? Math.Min(available.Width, Math.Max(40, shapedW + Pad * 2 + 2)) : Width,
            double.IsNaN(Height) ? Math.Max(28, shapedH + Pad * 2 + 2) : Height);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var r = RenderRect;
        dc.DrawRectangle((float)r.X, (float)r.Y, (float)r.Width, (float)r.Height, BorderRgba);
        if (r.Width > 2 && r.Height > 2)
            dc.DrawRectangle((float)r.X + 1, (float)r.Y + 1, (float)r.Width - 2, (float)r.Height - 2, BackgroundRgba);
        var masked = MaskedText;
        var highlight = SelectionLength > 0 && SelectionStart < masked.Length
            ? (Start: SelectionStart, Length: Math.Min(SelectionLength, masked.Length - SelectionStart))
            : ((int Start, int Length)?)null;
        if (highlight is { } sel && r.Width > 2 && r.Height > 2)
        {
            var (selX, _) = TryMeasure(masked[..sel.Start], FontCss, sel.Start * 8, 20);
            var (selW, selH) = TryMeasure(masked.Substring(sel.Start, sel.Length), FontCss, sel.Length * 8, 20);
            dc.DrawRectangle((float)r.X + Pad + (float)selX, (float)r.Y + Pad,
                (float)Math.Max(1, selW), (float)selH, SystemColors.HighlightBrush.ToRgba());
        }
        if (!string.IsNullOrEmpty(masked))
            dc.DrawText(masked, (float)r.X + Pad, (float)r.Y + Pad, FontCss, ForegroundRgba, 0, 1, -1);
        if (_focused && SelectionLength == 0 && r.Width > 2 && r.Height > 2)
        {
            var (prefixW, lineH) = TryMeasure(masked[..CaretIndex], FontCss, CaretIndex * 8, 20);
            dc.DrawRectangle((float)r.X + Pad + (float)prefixW, (float)r.Y + Pad, 1, (float)lineH, 0x000000FF);
        }
    }

    /// <summary>Deletes the selection, collapsing the caret to its anchor.</summary>
    private bool DeleteSelection()
    {
        if (SelectionLength == 0) return false;
        Password = Password.Remove(SelectionStart, SelectionLength);
        SelectionStart = Math.Min(SelectionStart, Password.Length);
        return true;
    }

    public override void OnRoutedEvent(WasmRoutedEvent e)
    {
        if (e.Handled || e.Phase != WasmRoutedPhase.Bubbling) return;
        if (e.Name != WasmRoutedEvent.KeyDownName || !_focused) return;
        if (e.Source is not Core.WasmInputEvent.Key k || !k.Args.IsDown) return;
        switch (k.Args.Key)
        {
            case "Backspace":
                if (IsReadOnlyBox) break;
                if (DeleteSelection()) { e.Handled = true; break; }
                if (CaretIndex > 0) { CaretIndex--; Password = Password.Remove(CaretIndex, 1); e.Handled = true; }
                break;
            case "Delete":
                if (IsReadOnlyBox) break;
                if (DeleteSelection()) { e.Handled = true; break; }
                if (CaretIndex < Password.Length) { Password = Password.Remove(CaretIndex, 1); e.Handled = true; }
                break;
            case "ArrowLeft":
                CaretIndex = SelectionLength > 0 ? SelectionStart : CaretIndex - 1;
                e.Handled = true;
                break;
            case "ArrowRight":
                CaretIndex = SelectionLength > 0 ? SelectionStart + SelectionLength : CaretIndex + 1;
                e.Handled = true;
                break;
            case "Home": CaretIndex = 0; e.Handled = true; break;
            case "End": CaretIndex = Password.Length; e.Handled = true; break;
            default:
                var key = k.Args.Key;
                if (key.Length == 1 && (key == "a" || key == "A") && k.Args.Ctrl && !k.Args.Alt)
                {
                    SelectAll();
                    e.Handled = true;
                }
                else if (key.Length == 1 && !k.Args.Ctrl && !k.Args.Alt && !IsReadOnlyBox)
                {
                    if (_maxLength > 0 && Password.Length >= _maxLength) break;
                    DeleteSelection();
                    Password = Password.Insert(CaretIndex, key);
                    CaretIndex++;
                    e.Handled = true;
                }
                break;
        }
    }
}

/// <summary>
/// Web content host (matches System.Windows.Controls.WebBrowser).
/// Real WPF hosts the native browser (ActiveXHost) with a live DOM; the
/// engine keeps the retained-tree contract instead: Source/Navigate journal,
/// about:blank HTML/Stream documents, and a precise boundary where the DOM
/// would be (Document stays null, InvokeScript throws). Converted
/// ScreenToGif FeedbackPreview NavigateToString binds here.
/// </summary>
public class WebBrowser : FrameworkElement
{
    private readonly List<Uri?> _back = new();
    private readonly List<Uri?> _forward = new();
    private Uri? _source;
    private string? _html;

    public WebBrowser()
    {
    }

    /// <summary>Current address (WPF navigates on set).</summary>
    public Uri? Source
    {
        get => _source;
        set
        {
            if (Equals(_source, value)) return;
            PushJournal(value);
            _source = value;
            _html = null;
            InvalidateVisual();
            RaiseNavigated();
        }
    }

    /// <summary>Latest NavigateToString/Stream document (host handoff, not a DOM).</summary>
    internal string? HtmlDocument => _html;

    /// <summary>Live DOM (always null: the retained tree hosts no browser control).</summary>
    public object? Document => null;

    /// <summary>Script bridge object (parsed and retained; no script engine runs it).</summary>
    public object? ObjectForScripting { get; set; }

    /// <summary>Raised after every committed navigation.</summary>
    public event NavigatedEventHandler? Navigated;

    /// <summary>Whether GoBack can move.</summary>
    public bool CanGoBack => _back.Count > 0;

    /// <summary>Whether GoForward can move.</summary>
    public bool CanGoForward => _forward.Count > 0;

    /// <summary>Navigates to an address.</summary>
    public void Navigate(Uri source)
    {
        ArgumentNullException.ThrowIfNull(source);
        Source = source;
    }

    /// <summary>Navigates to an address string.</summary>
    public void Navigate(string address) =>
        Navigate(new Uri(address, UriKind.RelativeOrAbsolute));

    /// <summary>Shows literal HTML (Source becomes about:blank, like WPF).</summary>
    public void NavigateToString(string html)
    {
        ArgumentNullException.ThrowIfNull(html);
        PushJournal(new Uri("about:blank"));
        _source = new Uri("about:blank");
        _html = html;
        InvalidateVisual();
        RaiseNavigated();
    }

    /// <summary>Shows stream content (read fully; Source becomes about:blank, like WPF).</summary>
    public void NavigateToStream(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        PushJournal(new Uri("about:blank"));
        _source = new Uri("about:blank");
        _html = System.Text.Encoding.UTF8.GetString(copy.ToArray());
        InvalidateVisual();
        RaiseNavigated();
    }

    /// <summary>Moves back in the journal (no-op when empty).</summary>
    public void GoBack()
    {
        if (_back.Count == 0) return;
        _forward.Add(_source);
        _source = _back[^1];
        _back.RemoveAt(_back.Count - 1);
        _html = null;
        InvalidateVisual();
        RaiseNavigated();
    }

    /// <summary>Moves forward in the journal (no-op when empty).</summary>
    public void GoForward()
    {
        if (_forward.Count == 0) return;
        _back.Add(_source);
        _source = _forward[^1];
        _forward.RemoveAt(_forward.Count - 1);
        _html = null;
        InvalidateVisual();
        RaiseNavigated();
    }

    /// <summary>Re-raises Navigated for the current address.</summary>
    public void Refresh()
    {
        InvalidateVisual();
        RaiseNavigated();
    }

    /// <summary>Script invocation (no script engine behind the retained tree).</summary>
    public object? InvokeScript(string scriptName, params object?[] args) =>
        throw new NotSupportedException(
            $"WebBrowser.InvokeScript('{scriptName}') needs the native browser DOM bridge, which the retained tree does not host.");

    private void PushJournal(Uri? next)
    {
        if (Equals(_source, next)) return;
        // The pristine host holds no document yet; the first navigation
        // starts the journal instead of journaling the empty host (like
        // WPF, where the first Navigate leaves CanGoBack false).
        if (_source is null && _html is null && _back.Count == 0) return;
        _back.Add(_source);
        _forward.Clear();
    }

    private void RaiseNavigated() =>
        Navigated?.Invoke(this, new NavigationEventArgs(null, _source));

    protected override WpfSize MeasureCore(WpfSize available) =>
        new(double.IsNaN(Width) ? Math.Min(available.Width, 300) : Width,
            double.IsNaN(Height) ? Math.Min(available.Height, 200) : Height);

    protected override void OnRender(DrawingContext dc)
    {
        var r = RenderRect;
        dc.DrawRectangle((float)r.X, (float)r.Y, (float)r.Width, (float)r.Height, 0x707070FF);
        if (r.Width > 2 && r.Height > 2)
            dc.DrawRectangle((float)r.X + 1, (float)r.Y + 1, (float)r.Width - 2, (float)r.Height - 2, 0xFFFFFFFF);
        var label = _source?.ToString() ?? "about:blank";
        if (!string.IsNullOrEmpty(label) && r.Width > 4 && r.Height > 4)
            dc.DrawText(label, (float)(r.X + r.Width / 2), (float)(r.Y + r.Height / 2),
                "12px sans-serif", 0x404040FF, 1, 2, -1);
    }
}

/// <summary>Effect XAML surface (partial XamlLoader: property elements + resource entries).</summary>
public static partial class XamlLoader
{
    /// <summary>
    /// Reads an element Effect property (positioned on the property start
    /// tag): assigns a nested DropShadowEffect or BlurEffect with literal
    /// attributes plus {Binding ...} attributes (the ShadowPanel/Editor
    /// shapes and the ComboBox drop-down blur). Anything else is a precise
    /// error.
    /// </summary>
    private static bool TryReadEffectProperty(UIElement element, string property, XmlReader reader, IXmlLineInfo lineInfo)
    {
        if (property != "Effect") return false;
        if (reader.IsEmptyElement)
        {
            reader.Read();
            element.Effect = null;
            return true;
        }
        reader.Read(); // past the property start tag
        SkipWhitespace(reader);
        if (reader.NodeType == XmlNodeType.EndElement)
        {
            reader.Read(); // past the property end tag
            element.Effect = null;
            return true;
        }
        if (reader.NodeType == XmlNodeType.Element && reader.LocalName is "DropShadowEffect" or "BlurEffect")
        {
            var blur = reader.LocalName == "BlurEffect";
            var node = XElement.Parse(reader.ReadOuterXml());
            Effect effect = blur ? new BlurEffect() : new DropShadowEffect();
            ApplyEffectAttributes(element, effect, node, lineInfo);
            element.Effect = effect;
            SkipWhitespace(reader);
            if (reader.NodeType != XmlNodeType.EndElement)
                throw Fail(lineInfo, $"Malformed <{element.GetType().Name}.Effect>.");
            reader.Read(); // past the property end tag
            return true;
        }
        throw Fail(lineInfo, $"<{element.GetType().Name}.Effect> needs a DropShadowEffect or BlurEffect entry.");
    }

    /// <summary>Effect literal leaves by runtime type (DropShadowEffect and BlurEffect share RenderingBias only).</summary>
    private static readonly string[] DropShadowLiterals =
        ["Color", "Direction", "ShadowDepth", "Opacity", "BlurRadius", "RenderingBias"];

    /// <summary>BlurEffect literal leaves (Radius/KernelType/RenderingBias).</summary>
    private static readonly string[] BlurLiterals = ["Radius", "KernelType", "RenderingBias"];

    /// <summary>Applies effect attributes (literals set; bindings attach to the owner).</summary>
    private static void ApplyEffectAttributes(UIElement owner, Effect effect, XElement node, IXmlLineInfo line)
    {
        var allowed = effect is BlurEffect ? BlurLiterals : DropShadowLiterals;
        foreach (var attribute in node.Attributes().Where(a => !a.IsNamespaceDeclaration))
        {
            if (attribute.Name.Namespace == XamlNamespace) continue;
            var leaf = attribute.Name.LocalName;
            if (!allowed.Contains(leaf))
                throw Fail(line, $"'{attribute.Name}' is not a {effect.GetType().Name} property ({string.Join("/", allowed)}).");
            var text = attribute.Value;
            if (text.StartsWith("{Binding", StringComparison.Ordinal) && text.EndsWith('}'))
            {
                if (owner is not FrameworkElement target)
                    throw Fail(line, $"Cannot bind 'Effect.{leaf}' on <{owner.GetType().Name}>.");
                var binding = ParseBinding(text["{Binding".Length..^1].Trim(), text, line);
                target.SetAttachedBinding("Effect." + leaf, binding);
                continue;
            }
            if (text.StartsWith('{'))
                throw Fail(line, $"'{text}' is not supported on DropShadowEffect.{leaf} (literals and {{Binding ...}} only).");
            SetEffectLiteral(effect, leaf, text, line);
        }
    }

    /// <summary>Sets one literal effect property (resource + property-element paths share this).</summary>
    private static void SetEffectLiteral(Effect effect, string leaf, string text, IXmlLineInfo line)
    {
        try
        {
            switch (leaf)
            {
                case "Color" when effect is DropShadowEffect drop:
                    drop.Color = StyleValueConverter.ColorFromRgba(ParseColor(text, line)); return;
                case "Direction" when effect is DropShadowEffect drop:
                    drop.Direction = ParseDouble(text, line); return;
                case "ShadowDepth" when effect is DropShadowEffect drop:
                    drop.ShadowDepth = ParseDouble(text, line); return;
                case "Opacity" when effect is DropShadowEffect drop:
                    drop.Opacity = ParseDouble(text, line); return;
                case "BlurRadius" when effect is DropShadowEffect drop:
                    drop.BlurRadius = ParseDouble(text, line); return;
                case "Radius" when effect is BlurEffect blur:
                    blur.Radius = ParseDouble(text, line); return;
                case "KernelType" when effect is BlurEffect blur:
                    if (!Enum.TryParse<KernelType>(text.Trim(), ignoreCase: true, out var kernel))
                        throw Fail(line, $"'{text}' is not a KernelType (Gaussian/Box).");
                    blur.KernelType = kernel;
                    return;
                case "RenderingBias" when effect is DropShadowEffect dropBias:
                    if (!Enum.TryParse<RenderingBias>(text.Trim(), ignoreCase: true, out var dropRendering))
                        throw Fail(line, $"'{text}' is not a RenderingBias (Performance/Quality).");
                    dropBias.RenderingBias = dropRendering;
                    return;
                case "RenderingBias" when effect is BlurEffect blurBias:
                    if (!Enum.TryParse<RenderingBias>(text.Trim(), ignoreCase: true, out var blurRendering))
                        throw Fail(line, $"'{text}' is not a RenderingBias (Performance/Quality).");
                    blurBias.RenderingBias = blurRendering;
                    return;
            }
            throw Fail(line, $"'{leaf}' is not a {effect.GetType().Name} property.");
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or OverflowException)
        {
            throw Fail(line, $"'{text}' is not a {effect.GetType().Name}.{leaf} value ({ex.Message}).");
        }
    }

    /// <summary>
    /// Reads a DropShadowEffect resource entry (the ScreenToGif theme
    /// Shadow.* dictionaries): x:Key plus literal attributes only.
    /// </summary>
    private static bool TryReadEffectResourceEntry(XmlReader reader, IXmlLineInfo lineInfo, ResourceDictionary dict)
    {
        if (reader.LocalName != "DropShadowEffect") return false;
        var node = XElement.Parse(reader.ReadOuterXml());
        var key = (string?)node.Attribute(XName.Get("Key", XamlNamespace))
            ?? throw Fail(lineInfo, "<DropShadowEffect> resource entry needs x:Key.");
        var effect = new DropShadowEffect();
        var freeze = false;
        foreach (var attribute in node.Attributes().Where(a => !a.IsNamespaceDeclaration))
        {
            if (attribute.Name.Namespace == XamlNamespace) continue;
            if (attribute.Name.NamespaceName.EndsWith("/presentation/options", StringComparison.Ordinal))
            {
                if (attribute.Name.LocalName == "Freeze" && bool.TryParse(attribute.Value, out var parsed)) freeze = parsed;
                continue;
            }
            var leaf = attribute.Name.LocalName;
            if (!EffectPaint.SubProperties.Contains(leaf))
                throw Fail(lineInfo, $"'{attribute.Name}' is not a DropShadowEffect property (Color/Direction/ShadowDepth/Opacity/BlurRadius/RenderingBias).");
            if (attribute.Value.StartsWith('{'))
            {
                // Theme shadows tint from the palette (StaticResource) or the
                // live theme (DynamicResource); other leaves stay literal.
                if (leaf != "Color")
                    throw Fail(lineInfo, $"DropShadowEffect.{leaf} resource entries take literals (markup extensions bind at use sites).");
                SetEffectLiteral(effect, leaf, ResolveEntryEffectColor(attribute.Value, lineInfo), lineInfo);
                continue;
            }
            SetEffectLiteral(effect, leaf, attribute.Value, lineInfo);
        }
        if (freeze && !effect.IsFrozen) effect.Freeze();
        try { dict.Add(key, effect); }
        catch (InvalidOperationException ex) { throw Fail(lineInfo, ex.Message); }
        return true;
    }
}
