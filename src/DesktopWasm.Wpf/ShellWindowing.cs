using System.Collections.ObjectModel;

namespace DesktopWasm.Wpf;

// ScreenToGif shell/windowing slice (NewRecorder/Recorder taskbar buttons,
// VideoSource column splitter, SizeToContent windows): the taskbar types are
// OS-shell integration, so the engine retains every value converted code
// reads and writes (progress state/value, overlay, thumbnail buttons with
// commands) without reaching the Windows shell; GridSplitter resizes the
// adjacent grid tracks through the real pointer path. Verified against the
// .NET 9 runtime PresentationFramework IL (DP defaults, enum values,
// ThumbButtonInfo ICommandSource, GridSplitter : Thumb with class-level
// drag handlers, Focusable + HorizontalAlignment.Right metadata).

/// <summary>Window autosize rule (matches System.Windows.SizeToContent).</summary>
public enum SizeToContent
{
    Manual,
    Width,
    Height,
    WidthAndHeight,
}

/// <summary>Taskbar progress band (matches System.Windows.Shell.TaskbarItemProgressState).</summary>
public enum TaskbarItemProgressState
{
    None,
    Indeterminate,
    Normal,
    Error,
    Paused,
}

/// <summary>Command source contract (matches System.Windows.Input.ICommandSource).</summary>
public interface ICommandSource
{
    ICommand? Command { get; }
    object? CommandParameter { get; }
    IInputElement? CommandTarget { get; }
}

/// <summary>
/// Taskbar thumbnail button (matches System.Windows.Shell.ThumbButtonInfo):
/// a Freezable carrying the shell button's command, image and labels.
/// Converted recorder windows declare these in XAML and drive progress and
/// overlays from code; the browser has no taskbar, so presses arrive through
/// <see cref="InvokeClick"/> (the shell-press seam) and run the same
/// Click-then-Command path the shell would trigger.
/// </summary>
public class ThumbButtonInfo : Freezable, ICommandSource
{
    public static readonly DependencyProperty CommandProperty =
        DependencyProperty.Register("Command", typeof(ICommand), typeof(ThumbButtonInfo),
            new FrameworkPropertyMetadata(null));
    public static readonly DependencyProperty CommandParameterProperty =
        DependencyProperty.Register("CommandParameter", typeof(object), typeof(ThumbButtonInfo),
            new FrameworkPropertyMetadata(null));
    public static readonly DependencyProperty CommandTargetProperty =
        DependencyProperty.Register("CommandTarget", typeof(IInputElement), typeof(ThumbButtonInfo),
            new FrameworkPropertyMetadata(null));
    public static readonly DependencyProperty DescriptionProperty =
        DependencyProperty.Register("Description", typeof(string), typeof(ThumbButtonInfo),
            new FrameworkPropertyMetadata(string.Empty));
    public static readonly DependencyProperty DismissWhenClickedProperty =
        DependencyProperty.Register("DismissWhenClicked", typeof(bool), typeof(ThumbButtonInfo),
            new FrameworkPropertyMetadata(false));
    public static readonly DependencyProperty ImageSourceProperty =
        DependencyProperty.Register("ImageSource", typeof(ImageSource), typeof(ThumbButtonInfo),
            new FrameworkPropertyMetadata(null));
    public static readonly DependencyProperty IsBackgroundVisibleProperty =
        DependencyProperty.Register("IsBackgroundVisible", typeof(bool), typeof(ThumbButtonInfo),
            new FrameworkPropertyMetadata(true));
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.Register("IsEnabled", typeof(bool), typeof(ThumbButtonInfo),
            new FrameworkPropertyMetadata(true));
    public static readonly DependencyProperty IsInteractiveProperty =
        DependencyProperty.Register("IsInteractive", typeof(bool), typeof(ThumbButtonInfo),
            new FrameworkPropertyMetadata(true));
    public static readonly DependencyProperty VisibilityProperty =
        DependencyProperty.Register("Visibility", typeof(Visibility), typeof(ThumbButtonInfo),
            new FrameworkPropertyMetadata(Visibility.Visible));

    /// <summary>Button action (matches ICommandSource.Command).</summary>
    public ICommand? Command
    {
        get => (ICommand?)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    /// <summary>Command argument (matches ICommandSource.CommandParameter).</summary>
    public object? CommandParameter
    {
        get => GetValue(CommandParameterProperty);
        set => SetValue(CommandParameterProperty, value);
    }

    /// <summary>Command routing target (matches ICommandSource.CommandTarget).</summary>
    public IInputElement? CommandTarget
    {
        get => (IInputElement?)GetValue(CommandTargetProperty);
        set => SetValue(CommandTargetProperty, value);
    }

    /// <summary>Tooltip text (default empty, like WPF).</summary>
    public string Description
    {
        get => (string?)GetValue(DescriptionProperty) ?? string.Empty;
        set => SetValue(DescriptionProperty, value ?? string.Empty);
    }

    /// <summary>Whether a press dismisses the thumbnail (default false).</summary>
    public bool DismissWhenClicked
    {
        get => (bool?)GetValue(DismissWhenClickedProperty) ?? false;
        set => SetValue(DismissWhenClickedProperty, value);
    }

    /// <summary>Button glyph (default null).</summary>
    public ImageSource? ImageSource
    {
        get => (ImageSource?)GetValue(ImageSourceProperty);
        set => SetValue(ImageSourceProperty, value);
    }

    /// <summary>Whether the button paints its border/background (default true).</summary>
    public bool IsBackgroundVisible
    {
        get => (bool?)GetValue(IsBackgroundVisibleProperty) ?? true;
        set => SetValue(IsBackgroundVisibleProperty, value);
    }

    /// <summary>Whether the button runs (default true).</summary>
    public bool IsEnabled
    {
        get => (bool?)GetValue(IsEnabledProperty) ?? true;
        set => SetValue(IsEnabledProperty, value);
    }

    /// <summary>Whether the shell shows this button as pressable (default true).</summary>
    public bool IsInteractive
    {
        get => (bool?)GetValue(IsInteractiveProperty) ?? true;
        set => SetValue(IsInteractiveProperty, value);
    }

    /// <summary>Button visibility (default Visible).</summary>
    public Visibility Visibility
    {
        get => (Visibility?)GetValue(VisibilityProperty) ?? Visibility.Visible;
        set => SetValue(VisibilityProperty, value);
    }

    /// <summary>Shell press notification (matches ThumbButtonInfo.Click).</summary>
    public event EventHandler? Click;

    /// <summary>
    /// Shell-press seam: raises Click, then runs Command while it can
    /// execute (mirrors the WPF shell path; disabled buttons stay silent).
    /// </summary>
    internal void InvokeClick()
    {
        if (!IsEnabled) return;
        Click?.Invoke(this, EventArgs.Empty);
        var command = Command;
        if (command is not null && command.CanExecute(CommandParameter))
            command.Execute(CommandParameter);
    }

    protected override Freezable CreateInstanceCore() => new ThumbButtonInfo();
}

/// <summary>
/// Thumbnail button list (matches System.Windows.Shell.ThumbButtonInfoCollection).
/// </summary>
public sealed class ThumbButtonInfoCollection : ObservableCollection<ThumbButtonInfo>
{
}

/// <summary>
/// Taskbar item state (matches System.Windows.Shell.TaskbarItemInfo): progress
/// band, overlay icon and thumbnail buttons. The browser has no taskbar, so
/// every value is retained and queryable; ProgressValue coerces into 0..1
/// like the shell band.
/// </summary>
public class TaskbarItemInfo : Freezable
{
    public static readonly DependencyProperty DescriptionProperty =
        DependencyProperty.Register("Description", typeof(string), typeof(TaskbarItemInfo),
            new FrameworkPropertyMetadata(string.Empty));
    public static readonly DependencyProperty OverlayProperty =
        DependencyProperty.Register("Overlay", typeof(ImageSource), typeof(TaskbarItemInfo),
            new FrameworkPropertyMetadata(null));
    public static readonly DependencyProperty ProgressStateProperty =
        DependencyProperty.Register("ProgressState", typeof(TaskbarItemProgressState), typeof(TaskbarItemInfo),
            new FrameworkPropertyMetadata(TaskbarItemProgressState.None));
    public static readonly DependencyProperty ProgressValueProperty =
        DependencyProperty.Register("ProgressValue", typeof(double), typeof(TaskbarItemInfo),
            new FrameworkPropertyMetadata(0.0));
    public static readonly DependencyProperty ThumbButtonInfosProperty =
        DependencyProperty.Register("ThumbButtonInfos", typeof(ThumbButtonInfoCollection), typeof(TaskbarItemInfo),
            new FrameworkPropertyMetadata(null));
    public static readonly DependencyProperty ThumbnailClipMarginProperty =
        DependencyProperty.Register("ThumbnailClipMargin", typeof(Thickness), typeof(TaskbarItemInfo),
            new FrameworkPropertyMetadata(new Thickness(0)));

    /// <summary>Tooltip text (default empty).</summary>
    public string Description
    {
        get => (string?)GetValue(DescriptionProperty) ?? string.Empty;
        set => SetValue(DescriptionProperty, value ?? string.Empty);
    }

    /// <summary>Status overlay glyph (default null).</summary>
    public ImageSource? Overlay
    {
        get => (ImageSource?)GetValue(OverlayProperty);
        set => SetValue(OverlayProperty, value);
    }

    /// <summary>Progress band mode (default None).</summary>
    public TaskbarItemProgressState ProgressState
    {
        get => (TaskbarItemProgressState?)GetValue(ProgressStateProperty) ?? TaskbarItemProgressState.None;
        set => SetValue(ProgressStateProperty, value);
    }

    /// <summary>Progress fraction, coerced into 0..1 (default 0).</summary>
    public double ProgressValue
    {
        get => (double?)GetValue(ProgressValueProperty) ?? 0.0;
        set => SetValue(ProgressValueProperty, Math.Clamp(value, 0.0, 1.0));
    }

    /// <summary>Thumbnail buttons (never null; lazily created like WPF's shared empty).</summary>
    public ThumbButtonInfoCollection ThumbButtonInfos
    {
        get
        {
            var current = (ThumbButtonInfoCollection?)GetValue(ThumbButtonInfosProperty);
            if (current is null)
            {
                current = new ThumbButtonInfoCollection();
                SetValue(ThumbButtonInfosProperty, current);
            }
            return current;
        }
        set => SetValue(ThumbButtonInfosProperty, value ?? new ThumbButtonInfoCollection());
    }

    /// <summary>Thumbnail clipping margin (default empty).</summary>
    public Thickness ThumbnailClipMargin
    {
        get => (Thickness?)GetValue(ThumbnailClipMarginProperty) ?? new Thickness(0);
        set => SetValue(ThumbnailClipMarginProperty, value);
    }

    protected override Freezable CreateInstanceCore() => new TaskbarItemInfo();
}

/// <summary>Splitter orientation rule (matches System.Windows.Controls.GridResizeDirection).</summary>
public enum GridResizeDirection
{
    Auto,
    Columns,
    Rows,
}

/// <summary>Tracks a drag redistributes (matches System.Windows.Controls.GridResizeBehavior).</summary>
public enum GridResizeBehavior
{
    BasedOnAlignment,
    CurrentAndNext,
    PreviousAndCurrent,
    PreviousAndNext,
}

/// <summary>
/// Grid track handle (matches System.Windows.Controls.GridSplitter): a Thumb
/// that resizes the neighboring row/column definitions through the real
/// pointer path. Star/auto tracks convert to pixels when a drag starts (like
/// WPF), Min/Max clamps hold, keyboard arrows nudge by KeyboardIncrement,
/// and ShowsPreview defers the definition change to drag completion while
/// the pressed thumb shows the drag offset.
/// </summary>
public class GridSplitter : Thumb
{
    public static readonly DependencyProperty ResizeDirectionProperty =
        DependencyProperty.Register("ResizeDirection", typeof(GridResizeDirection), typeof(GridSplitter),
            new FrameworkPropertyMetadata(GridResizeDirection.Auto),
            static value => value is GridResizeDirection);
    public static readonly DependencyProperty ResizeBehaviorProperty =
        DependencyProperty.Register("ResizeBehavior", typeof(GridResizeBehavior), typeof(GridSplitter),
            new FrameworkPropertyMetadata(GridResizeBehavior.BasedOnAlignment),
            static value => value is GridResizeBehavior);
    public static readonly DependencyProperty ShowsPreviewProperty =
        DependencyProperty.Register("ShowsPreview", typeof(bool), typeof(GridSplitter),
            new FrameworkPropertyMetadata(false));
    public static readonly DependencyProperty PreviewStyleProperty =
        DependencyProperty.Register("PreviewStyle", typeof(Style), typeof(GridSplitter),
            new FrameworkPropertyMetadata(null));
    public static readonly DependencyProperty KeyboardIncrementProperty =
        DependencyProperty.Register("KeyboardIncrement", typeof(double), typeof(GridSplitter),
            new FrameworkPropertyMetadata(10.0),
            static value => value is double d && !double.IsNaN(d) && d >= 0);
    public static readonly DependencyProperty DragIncrementProperty =
        DependencyProperty.Register("DragIncrement", typeof(double), typeof(GridSplitter),
            new FrameworkPropertyMetadata(1.0),
            static value => value is double d && !double.IsNaN(d) && d >= 0);

    private double _pendingDelta;
    private bool _hasPending;

    public GridSplitter()
    {
        // Real GridSplitter metadata: focusable with a right-docked default.
        Focusable = true;
        HorizontalAlignment = HorizontalAlignment.Right;
        DragDelta += (_, e) => ApplyDrag(e.HorizontalChange, e.VerticalChange, commit: !ShowsPreview);
        DragCompleted += (_, e) =>
        {
            if (!e.Canceled && _hasPending) ApplyPending();
            _pendingDelta = 0;
            _hasPending = false;
        };
        UpdateCursor();
    }

    /// <summary>Orientation rule (default Auto, resolved from alignment/extent).</summary>
    public GridResizeDirection ResizeDirection
    {
        get => (GridResizeDirection?)GetValue(ResizeDirectionProperty) ?? GridResizeDirection.Auto;
        set
        {
            SetValue(ResizeDirectionProperty, value);
            UpdateCursor();
        }
    }

    /// <summary>Which tracks move (default BasedOnAlignment).</summary>
    public GridResizeBehavior ResizeBehavior
    {
        get => (GridResizeBehavior?)GetValue(ResizeBehaviorProperty) ?? GridResizeBehavior.BasedOnAlignment;
        set => SetValue(ResizeBehaviorProperty, value);
    }

    /// <summary>Defer definition changes to drag completion (default false).</summary>
    public bool ShowsPreview
    {
        get => (bool?)GetValue(ShowsPreviewProperty) ?? false;
        set => SetValue(ShowsPreviewProperty, value);
    }

    /// <summary>Preview adorner style slot (retained, no template inflation).</summary>
    public Style? PreviewStyle
    {
        get => (Style?)GetValue(PreviewStyleProperty);
        set => SetValue(PreviewStyleProperty, value);
    }

    /// <summary>Keyboard nudge step (default 10).</summary>
    public double KeyboardIncrement
    {
        get => (double?)GetValue(KeyboardIncrementProperty) ?? 10.0;
        set => SetValue(KeyboardIncrementProperty, value);
    }

    /// <summary>Drag snap step (default 1).</summary>
    public double DragIncrement
    {
        get => (double?)GetValue(DragIncrementProperty) ?? 1.0;
        set => SetValue(DragIncrementProperty, value);
    }

    private bool IsColumnSplitter()
    {
        var direction = ResizeDirection;
        if (direction == GridResizeDirection.Columns) return true;
        if (direction == GridResizeDirection.Rows) return false;
        if (HorizontalAlignment != HorizontalAlignment.Stretch || !double.IsNaN(Width)) return true;
        if (VerticalAlignment != VerticalAlignment.Stretch || !double.IsNaN(Height)) return false;
        return true;
    }

    private void UpdateCursor()
    {
        Cursor = IsColumnSplitter() ? Cursors.SizeWE : Cursors.SizeNS;
    }

    private void ApplyDrag(double dx, double dy, bool commit)
    {
        var delta = Snap(IsColumnSplitter() ? dx : dy, DragIncrement);
        if (delta == 0) return;
        if (commit) MoveTracks(delta);
        else { _pendingDelta += delta; _hasPending = true; InvalidateVisual(); }
    }

    private void ApplyPending()
    {
        var delta = Snap(_pendingDelta, DragIncrement);
        _pendingDelta = 0;
        _hasPending = false;
        if (delta != 0) MoveTracks(delta);
    }

    private static double Snap(double delta, double increment) =>
        increment > 0 ? Math.Round(delta / increment) * increment : delta;

    private static double ClampMove(double delta, double min, double max) =>
        min > max ? 0 : Math.Clamp(delta, min, max);

    private static int AttachedIndex(UIElement element, string name)
    {
        var raw = element.GetAttached(name);
        if (raw is int i) return i;
        if (raw is double d) return (int)d;
        return 0;
    }

    private void MoveTracks(double delta)
    {
        if (Parent is not Grid grid) return;
        var columns = IsColumnSplitter();
        if (columns) MoveColumns(grid, delta);
        else MoveRows(grid, delta);
        grid.InvalidateMeasure();
    }

    private void MoveColumns(Grid grid, double delta)
    {
        var count = grid.ColumnDefinitions.Count;
        if (count == 0) return;
        var at = Math.Clamp(AttachedIndex(this, "Grid.Column"), 0, count - 1);
        var (first, second) = ResizeBehavior switch
        {
            GridResizeBehavior.CurrentAndNext => (at, at + 1),
            GridResizeBehavior.PreviousAndCurrent => (at - 1, at),
            GridResizeBehavior.PreviousAndNext => (at - 1, at + 1),
            _ => HorizontalAlignment == HorizontalAlignment.Right ? (at - 1, at)
                : HorizontalAlignment == HorizontalAlignment.Left ? (at, at + 1)
                : (at - 1, at + 1),
        };
        if (first < 0 || second < first || second >= count) return;
        var a = grid.ColumnDefinitions[first];
        var b = grid.ColumnDefinitions[second];
        var (wa, wb) = (PixelWidth(grid, first, true), PixelWidth(grid, second, true));
        var move = ClampMove(delta, a.MinWidth - wa, a.MaxWidth - wa);
        move = ClampMove(move, wb - b.MaxWidth, wb - b.MinWidth);
        if (move == 0) return;
        a.Width = GridLength.Pixel(wa + move);
        b.Width = GridLength.Pixel(wb - move);
    }

    private void MoveRows(Grid grid, double delta)
    {
        var count = grid.RowDefinitions.Count;
        if (count == 0) return;
        var at = Math.Clamp(AttachedIndex(this, "Grid.Row"), 0, count - 1);
        var (first, second) = ResizeBehavior switch
        {
            GridResizeBehavior.CurrentAndNext => (at, at + 1),
            GridResizeBehavior.PreviousAndCurrent => (at - 1, at),
            GridResizeBehavior.PreviousAndNext => (at - 1, at + 1),
            _ => VerticalAlignment == VerticalAlignment.Bottom ? (at - 1, at)
                : VerticalAlignment == VerticalAlignment.Top ? (at, at + 1)
                : (at - 1, at + 1),
        };
        if (first < 0 || second < first || second >= count) return;
        var a = grid.RowDefinitions[first];
        var b = grid.RowDefinitions[second];
        var (ha, hb) = (PixelWidth(grid, first, false), PixelWidth(grid, second, false));
        var move = ClampMove(delta, a.MinHeight - ha, a.MaxHeight - ha);
        move = ClampMove(move, hb - b.MaxHeight, hb - b.MinHeight);
        if (move == 0) return;
        a.Height = GridLength.Pixel(ha + move);
        b.Height = GridLength.Pixel(hb - move);
    }

    private static double PixelWidth(Grid grid, int index, bool columns)
    {
        // The arranged pixel span is the honest star/auto measure; explicit
        // pixels land verbatim. ColumnDefinitions/RowDefinitions never go
        // negative (layout clamps), so the max() only guards test harnesses
        // that resize before the first arrange.
        if (columns)
        {
            var def = grid.ColumnDefinitions[index];
            if (def.Width.Unit == GridUnitType.Pixel) return Math.Max(0, def.Width.Value);
            return Math.Max(0, grid.ColumnPixelWidth(index));
        }
        var row = grid.RowDefinitions[index];
        if (row.Height.Unit == GridUnitType.Pixel) return Math.Max(0, row.Height.Value);
        return Math.Max(0, grid.RowPixelHeight(index));
    }

    /// <summary>Splitters take focus (store-backed; the Control default is true).</summary>
    public override bool Focusable
    {
        get => (bool?)GetValue(FocusableProperty) ?? true;
        set => SetValue(FocusableProperty, value);
    }

    public override void OnRoutedEvent(WasmRoutedEvent e)
    {
        base.OnRoutedEvent(e);
        if (e.Handled || e.Phase != WasmRoutedPhase.Bubbling || !IsEffectivelyEnabled) return;
        if (e.Source is Core.WasmInputEvent.Key k && k.Args.IsDown && IsFocused)
        {
            var step = KeyboardIncrement;
            double delta = k.Args.Key switch
            {
                "ArrowLeft" => IsColumnSplitter() ? -step : 0,
                "ArrowRight" => IsColumnSplitter() ? step : 0,
                "ArrowUp" => IsColumnSplitter() ? 0 : -step,
                "ArrowDown" => IsColumnSplitter() ? 0 : step,
                _ => 0,
            };
            if (delta != 0)
            {
                MoveTracks(delta);
                e.Handled = true;
            }
        }
    }

    protected override WpfSize MeasureCore(WpfSize available)
    {
        // A splitter with no explicit extent still hits-tests: keep a thin
        // minimum in the resize axis (real splitters get their size from
        // alignment stretch or explicit Width/Height).
        var columns = IsColumnSplitter();
        var w = double.IsNaN(Width) ? (columns ? 4 : 0) : Width;
        var h = double.IsNaN(Height) ? (columns ? 0 : 4) : Height;
        return new WpfSize(Math.Min(w, available.Width), Math.Min(h, available.Height));
    }

    protected override void OnRender(DrawingContext dc)
    {
        var r = RenderRect;
        if (r.Width <= 0 || r.Height <= 0) return;
        var face = Background is SolidColorBrush solid ? solid.ToRgba() : 0x808080FFu;
        dc.DrawRectangle((float)r.X, (float)r.Y, (float)r.Width, (float)r.Height, face);
        if (BorderBrush is SolidColorBrush edge)
            dc.DrawRectangle((float)r.X, (float)r.Y, (float)r.Width, (float)r.Height, edge.ToRgba());
        if (_hasPending)
        {
            // Preview mode: the definitions hold until release, so the bar
            // paints the accumulated drag offset marker instead. The marker
            // paints in the splitter's own pass, so later siblings cover
            // the overflow exactly like any other overflowing child paint.
            var columns = IsColumnSplitter();
            var mark = columns
                ? new WpfRect(r.X + r.Width / 2 + _pendingDelta - 1, r.Y, 2, r.Height)
                : new WpfRect(r.X, r.Y + r.Height / 2 + _pendingDelta - 1, r.Width, 2);
            dc.DrawRectangle((float)mark.X, (float)mark.Y, (float)mark.Width, (float)mark.Height, 0x000000FFu);
        }
    }
}

/// <summary>
/// Startup placement math (matches Window.WindowStartupLocation semantics):
/// centers a dialog rect in the session working area (the single
/// Screen.PrimaryScreen viewport) and clamps it inside, so converted
/// CenterScreen/CenterOwner windows (NewRecorder, editor dialogs) land
/// visibly. Pure struct geometry over System.Drawing.Primitives; no GDI+
/// execution, no handle or multi-monitor lookup.
/// </summary>
public static class WindowPlacement
{
    /// <summary>
    /// Places a dialog of the given size per the startup rule against the
    /// session working area (CenterOwner centers in ownerRect, falling back
    /// to the working area when none is given; Manual keeps the area
    /// origin). Results are always clamped inside the working area.
    /// </summary>
    public static System.Drawing.Rectangle PlaceDialog(
        double width, double height, WindowStartupLocation location,
        System.Drawing.Rectangle? ownerRect = null) =>
        PlaceIn(width, height, location, ownerRect,
            System.Windows.Forms.Screen.PrimaryScreen.WorkingArea);

    internal static System.Drawing.Rectangle PlaceIn(
        double width, double height, WindowStartupLocation location,
        System.Drawing.Rectangle? ownerRect, System.Drawing.Rectangle area)
    {
        var w = (int)Math.Round(double.IsNaN(width) ? 0 : Math.Max(0, width));
        var h = (int)Math.Round(double.IsNaN(height) ? 0 : Math.Max(0, height));
        w = Math.Min(w, area.Width);
        h = Math.Min(h, area.Height);
        var anchor = location switch
        {
            WindowStartupLocation.CenterOwner when ownerRect.HasValue => ownerRect.Value,
            WindowStartupLocation.CenterScreen => area,
            WindowStartupLocation.CenterOwner => area,
            _ => new System.Drawing.Rectangle(area.X, area.Y, 0, 0),
        };
        var x = anchor.X + (anchor.Width - w) / 2;
        var y = anchor.Y + (anchor.Height - h) / 2;
        x = Math.Clamp(x, area.X, Math.Max(area.X, area.Right - w));
        y = Math.Clamp(y, area.Y, Math.Max(area.Y, area.Bottom - h));
        return new System.Drawing.Rectangle(x, y, w, h);
    }
}
