namespace DesktopWasm.Wpf;

// ToggleButton (WPF subset): two/three-state press button. Converted apps
// bind generated add_Checked/add_Unchecked references here (calculator View
// menu uses IsChecked; Geometry SampleViewer wires Checked handlers and
// sets IsChecked programmatically).
public class ToggleButton : ButtonBase
{
    /// <summary>Checked-state store (matches ToggleButton.IsCheckedProperty).</summary>
    public static readonly DependencyProperty IsCheckedProperty =
        DependencyProperty.Register("IsChecked", typeof(bool?), typeof(ToggleButton),
            new FrameworkPropertyMetadata(false, OnIsCheckedChanged));

    /// <summary>Checked state (null only when three-state).</summary>
    public virtual bool? IsChecked
    {
        get => (bool?)GetValue(IsCheckedProperty);
        set => SetValue(IsCheckedProperty, value);
    }

    private static void OnIsCheckedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ToggleButton toggle) toggle.RaiseCheckedChanged((bool?)e.OldValue, (bool?)e.NewValue);
    }

    private void RaiseCheckedChanged(bool? oldValue, bool? newValue)
    {
        if (oldValue == newValue) return;
        InvalidateVisual();
        var args = new RoutedEventArgs { Source = this };
        if (newValue == true) Checked?.Invoke(this, args);
        else if (newValue == false) Unchecked?.Invoke(this, args);
        else Indeterminate?.Invoke(this, args);
    }

    /// <summary>Raised after IsChecked becomes true.</summary>
    public event RoutedEventHandler? Checked;

    /// <summary>Raised after IsChecked becomes false.</summary>
    public event RoutedEventHandler? Unchecked;

    /// <summary>Raised after IsChecked becomes null.</summary>
    public event RoutedEventHandler? Indeterminate;

    /// <summary>
    /// Whether the toggle takes keyboard focus (matches
    /// System.Windows.Controls.Primitives.ToggleButton: settable like real
    /// WPF, where Control overrides the default to true; the PC Space Map
    /// ComboBox template sets Focusable="False" on its drop-down toggle).
    /// Store-backed; the Control default is true.
    /// </summary>
    public override bool Focusable
    {
        get => (bool?)GetValue(FocusableProperty) ?? true;
        set => SetValue(FocusableProperty, value);
    }

    /// <summary>
    /// Assigns the state and raises the matching event. Matches real WPF:
    /// programmatic sets raise the same Checked/Unchecked/Indeterminate
    /// events as user toggles; equal values are silent.
    /// </summary>
    protected void SetChecked(bool? value) => IsChecked = value;

    /// <summary>Press toggles the state, then Click fires (ButtonBase press semantics).</summary>
    protected virtual void Toggle()
    {
        SetChecked(!(IsChecked == true));
    }

    /// <summary>
    /// Click dispatch (matches ToggleButton.OnClick: toggle first, then the
    /// ButtonBase click; converted AvalonDock drop-down buttons override it
    /// to open their popups instead).
    /// </summary>
    protected override void OnClick()
    {
        Toggle();
        base.OnClick();
    }

    private bool _pressed;
    private bool _hoverInside;

    public override void OnRoutedEvent(WasmRoutedEvent e)
    {
        // ClickMode-parity with Button (matches the real press state machine):
        // Release toggles+clicks on release-inside, Press on the way down,
        // Hover on pointer entry; release-outside and cancel only disarm.
        if (e.Handled || e.Phase != WasmRoutedPhase.Bubbling || !IsEffectivelyEnabled) return;
        switch (e.Name)
        {
            case WasmRoutedEvent.PointerDownName when e.IsLeftButtonDown:
                _pressed = true;
                if (ClickMode == ClickMode.Press)
                {
                    OnClick();
                }
                e.Handled = true;
                break;
            case WasmRoutedEvent.PointerUpName
                when e.Source is Core.WasmInputEvent.Pointer p && p.Args.Kind == Core.WasmPointerKind.Up:
                if (!_pressed) break;
                _pressed = false;
                // Press already toggled+clicked on the way down.
                if (ClickMode != ClickMode.Release) break;
                if (HitTest(new WpfPoint(p.Args.X, p.Args.Y)))
                {
                    OnClick();
                    e.Handled = true;
                }
                break;
            case WasmRoutedEvent.PointerCancelName:
                _pressed = false;
                _hoverInside = false;
                break;
            case WasmRoutedEvent.PointerMoveName
                when e.Source is Core.WasmInputEvent.Pointer hover && hover.Args.Kind == Core.WasmPointerKind.Move:
                var over = HitTest(new WpfPoint(hover.Args.X, hover.Args.Y));
                if (over && !_hoverInside && ClickMode == ClickMode.Hover)
                {
                    OnClick();
                }
                _hoverInside = over;
                break;
        }
    }

    protected override WpfSize MeasureCore(WpfSize available)
    {
        var label = Content?.ToString() ?? string.Empty;
        var shaped = TryMeasure(label, FontCss, label.Length * 8, 16);
        return new(
            double.IsNaN(Width) ? Math.Min(available.Width, Math.Max(64, GlyphSize + GlyphGap + shaped.Width + 8)) : Width,
            double.IsNaN(Height) ? Math.Max(24, shaped.Height + 8) : Height);
    }

    /// <summary>Glyph box edge (subclasses paint the mark inside it).</summary>
    protected const double GlyphSize = 14;

    /// <summary>Gap between the glyph box and the content text.</summary>
    protected const double GlyphGap = 6;

    public virtual string FontCss { get; set; } = "14px sans-serif";
    public uint ForegroundRgba { get; set; } = 0x000000FF;

    protected override void OnForegroundChanged(Brush? brush)
    {
        if (brush is SolidColorBrush solid) ForegroundRgba = solid.ToRgba();
    }

    protected override void OnFontChanged() => FontCss = ComposeFontCss();

    protected override void OnRender(DrawingContext dc)
    {
        var r = RenderRect;
        var fill = IsEffectivelyEnabled ? 0xFFFFFFFFu : 0xCCCCCCFFu;
        var border = IsEffectivelyEnabled ? 0x707070FFu : 0x909090FFu;
        var ink = IsEffectivelyEnabled ? ForegroundRgba : 0x707070FFu;
        var top = r.Y + (r.Height - GlyphSize) / 2;
        dc.DrawRectangle((float)r.X, (float)top, (float)GlyphSize, (float)GlyphSize, border);
        dc.DrawRectangle((float)r.X + 1, (float)top + 1, (float)GlyphSize - 2, (float)GlyphSize - 2, fill);
        RenderMark(dc, (float)r.X, (float)top, ink);
        var label = Content?.ToString() ?? string.Empty;
        if (!string.IsNullOrEmpty(label))
            dc.DrawText(label, (float)(r.X + GlyphSize + GlyphGap), (float)(r.Y + r.Height / 2), FontCss, ink, 0, 2, -1);
    }

    /// <summary>Paints the checked mark inside the glyph box (none when unchecked).</summary>
    protected virtual void RenderMark(DrawingContext dc, float x, float y, uint ink) { }
}

/// <summary>
/// Check box (WPF subset): glyph box with a check mark. Supports the
/// three-state cycle when <see cref="IsThreeState"/> is set.
/// </summary>
public class CheckBox : ToggleButton
{
    /// <summary>Whether the toggle cycles through null (default false, like real WPF).</summary>
    public bool IsThreeState { get; set; }

    protected override void Toggle()
    {
        // Real WPF CheckBox cycle: true -> (null when three-state, else false),
        // null -> false, false -> true.
        if (IsChecked == true) SetChecked(IsThreeState ? null : false);
        else if (IsChecked is null) SetChecked(false);
        else SetChecked(true);
    }

    protected override void RenderMark(DrawingContext dc, float x, float y, uint ink)
    {
        if (IsChecked == true)
            dc.DrawText("✓", x + (float)GlyphSize / 2, y + (float)GlyphSize / 2, "12px sans-serif", ink, 1, 2, -1);
        else if (IsChecked is null)
            dc.DrawRectangle(x + 3, y + 5, (float)GlyphSize - 6, (float)GlyphSize - 10, ink);
    }
}

/// <summary>
/// Radio button (WPF subset): selecting one option clears the others in its
/// group (same <see cref="GroupName"/> under the same parent; empty names
/// group by parent, like real WPF). A pressed checked radio stays checked.
/// </summary>
public class RadioButton : ToggleButton
{
    /// <summary>Option group (empty groups by parent).</summary>
    public string GroupName { get; set; } = string.Empty;

    protected override void Toggle()
    {
        if (IsChecked == true) return;
        ClearGroup();
        SetChecked(true);
    }

    private void ClearGroup()
    {
        if (Parent is not FrameworkElement parent) return;
        foreach (var sibling in parent.LogicalChildren)
        {
            if (ReferenceEquals(sibling, this) || sibling is not RadioButton other) continue;
            if (!string.Equals(other.GroupName ?? string.Empty, GroupName ?? string.Empty, StringComparison.Ordinal)) continue;
            other.SetChecked(false);
        }
    }

    protected override void RenderMark(DrawingContext dc, float x, float y, uint ink)
    {
        if (IsChecked == true)
            dc.DrawRectangle(x + 3, y + 3, (float)GlyphSize - 6, (float)GlyphSize - 6, ink);
    }
}
