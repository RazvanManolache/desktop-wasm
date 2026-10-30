using System.Globalization;

namespace DesktopWasm.Wpf;

// Gallery DateAndTime/DatePickerPage slice (slice-38): the detail page hosts
// a bare <DatePicker/>, which the loader rejected as an unknown element. This
// is a shim, not a port: a text field (real TextBox, so typing, caret,
// selection and binding push are the proven ones) plus a drop-down toggle
// opening a Popup hosting the real slice-37 Calendar engine (real pointer
// day clicks, blackout/range guards, SelectedDate sync). Retained limits:
// no templating. The text field is a DatePickerTextBox carrying the
// watermark slot, and SelectedDateFormat switches the short/long render.

/// <summary>Failed text-commit payload (matches System.Windows.Controls.DatePickerDateValidationErrorEventArgs).</summary>
public sealed class DatePickerDateValidationErrorEventArgs : RoutedEventArgs
{
    public DatePickerDateValidationErrorEventArgs(Exception exception, string text)
    {
        Exception = exception;
        Text = text;
    }

    /// <summary>Why the text did not become a date.</summary>
    public Exception Exception { get; }

    /// <summary>Committed text that failed to parse.</summary>
    public string Text { get; }
}

/// <summary>
/// Editable date field with a drop-down Calendar (matches
/// System.Windows.Controls.DatePicker). The text box, toggle button and
/// drop-down calendar are composed from the proven TextBox/Button/Popup/
/// Calendar paths; this class only owns date math, text sync and guards.
/// </summary>
public sealed class DatePicker : Control
{
    private const double ToggleWidth = 28;

    /// <summary>Selection token (matches DatePicker.SelectedDateChangedEvent).</summary>
    public static readonly RoutedEvent SelectedDateChangedEvent = EventManager.RegisterRoutedEvent(
        "SelectedDateChanged", RoutingStrategy.Bubble, typeof(SelectionChangedEventHandler), typeof(DatePicker));

    /// <summary>Selected-date slot (matches DatePicker.SelectedDateProperty).</summary>
    public static readonly DependencyProperty SelectedDateProperty =
        DependencyProperty.Register("SelectedDate", typeof(DateTime?), typeof(DatePicker),
            new FrameworkPropertyMetadata(null, (d, e) => ((DatePicker)d).OnSelectedDatePropertyChanged(
                (DateTime?)e.OldValue, (DateTime?)e.NewValue)));

    /// <summary>Visible-month slot (matches DatePicker.DisplayDateProperty).</summary>
    public static readonly DependencyProperty DisplayDateProperty =
        DependencyProperty.Register("DisplayDate", typeof(DateTime), typeof(DatePicker),
            new FrameworkPropertyMetadata(default(DateTime), (d, e) => ((DatePicker)d).OnDisplayDatePropertyChanged(
                (DateTime)e.OldValue, (DateTime)e.NewValue)));

    /// <summary>Lower display bound (matches DatePicker.DisplayDateStartProperty).</summary>
    public static readonly DependencyProperty DisplayDateStartProperty =
        DependencyProperty.Register("DisplayDateStart", typeof(DateTime?), typeof(DatePicker),
            new FrameworkPropertyMetadata(null, (d, e) => ((DatePicker)d).OnDisplayBoundChanged()));

    /// <summary>Upper display bound (matches DatePicker.DisplayDateEndProperty).</summary>
    public static readonly DependencyProperty DisplayDateEndProperty =
        DependencyProperty.Register("DisplayDateEnd", typeof(DateTime?), typeof(DatePicker),
            new FrameworkPropertyMetadata(null, (d, e) => ((DatePicker)d).OnDisplayBoundChanged()));

    /// <summary>Week-start slot (matches DatePicker.FirstDayOfWeekProperty).</summary>
    public static readonly DependencyProperty FirstDayOfWeekProperty =
        DependencyProperty.Register("FirstDayOfWeek", typeof(DayOfWeek), typeof(DatePicker),
            new FrameworkPropertyMetadata(DayOfWeek.Sunday, (d, e) => ((DatePicker)d).OnFirstDayOfWeekChanged()));

    /// <summary>Drop-down slot (matches DatePicker.IsDropDownOpenProperty).</summary>
    public static readonly DependencyProperty IsDropDownOpenProperty =
        DependencyProperty.Register("IsDropDownOpen", typeof(bool), typeof(DatePicker),
            new FrameworkPropertyMetadata(false, (d, e) => ((DatePicker)d).OnDropDownPropertyChanged(
                (bool)e.OldValue, (bool)e.NewValue)));

    /// <summary>Calendar style slot (matches DatePicker.CalendarStyleProperty;
    /// null default, like WPF).</summary>
    public static readonly DependencyProperty CalendarStyleProperty =
        DependencyProperty.Register("CalendarStyle", typeof(Style), typeof(DatePicker),
            new FrameworkPropertyMetadata(null));

    /// <summary>Calendar style (null default, like WPF).</summary>
    public Style? CalendarStyle
    {
        get => (Style?)GetValue(CalendarStyleProperty);
        set => SetValue(CalendarStyleProperty, value);
    }

    /// <summary>Today-marker slot (matches DatePicker.IsTodayHighlightedProperty).</summary>
    public static readonly DependencyProperty IsTodayHighlightedProperty =
        DependencyProperty.Register("IsTodayHighlighted", typeof(bool), typeof(DatePicker),
            new FrameworkPropertyMetadata(true, (d, e) => ((DatePicker)d).OnTodayHighlightedChanged()));

    /// <summary>Text slot (matches DatePicker.TextProperty).</summary>
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register("Text", typeof(string), typeof(DatePicker),
            new FrameworkPropertyMetadata(string.Empty, (d, e) => ((DatePicker)d).OnTextPropertyChanged(
                (string?)e.OldValue, (string?)e.NewValue)));

    /// <summary>Format slot (matches DatePicker.SelectedDateFormatProperty; default Short like WPF).</summary>
    public static readonly DependencyProperty SelectedDateFormatProperty =
        DependencyProperty.Register("SelectedDateFormat", typeof(DatePickerFormat), typeof(DatePicker),
            new FrameworkPropertyMetadata(DatePickerFormat.Short, (d, e) => ((DatePicker)d).OnSelectedDateFormatChanged(
                (DatePickerFormat)e.OldValue, (DatePickerFormat)e.NewValue)));

    private readonly DatePickerTextBox _box = new();
    private readonly Button _toggle = new();
    private readonly Calendar _calendar = new();
    private readonly Popup _popup = new();
    private bool _syncing;
    private bool _wasOpenAtPress;

    /// <summary>Builds the empty picker: blank text, drop-down shut, today as the display month.</summary>
    public DatePicker()
    {
        Focusable = true;
        _toggle.Click += (_, _) => IsDropDownOpen = !_wasOpenAtPress;
        _box.TextChanged += (_, _) => OnBoxTextChanged();
        _box.LostFocus += (_, _) => CommitText();
        _calendar.SelectedDateChanged += (_, e) => OnCalendarPicked(e);
        _popup.Child = _calendar;
        _popup.Placement = PlacementMode.Bottom;
        _popup.PlacementTarget = this;
        _popup.StaysOpen = false;
        _popup.Opened += (_, _) => CalendarOpened?.Invoke(this, EventArgs.Empty);
        _popup.Closed += (_, _) =>
        {
            if (IsDropDownOpen) SetValue(IsDropDownOpenProperty, false);
            CalendarClosed?.Invoke(this, EventArgs.Empty);
        };
        AddChild(_box);
        AddChild(_toggle);
        AddChild(_popup);
        DisplayDate = DateTime.Today;
        SyncCalendarBounds();
        SetValue(TextProperty, string.Empty);
    }

    /// <summary>Selected day (null clears; blackout/out-of-range throws like Calendar).</summary>
    public DateTime? SelectedDate
    {
        get => (DateTime?)GetValue(SelectedDateProperty);
        set
        {
            var day = value?.Date;
            if (day is not null && !IsSelectable(day.Value))
                throw new ArgumentOutOfRangeException(nameof(value),
                    $"Cannot select {day:yyyy-MM-dd}: it is outside the display range.");
            SetValue(SelectedDateProperty, day);
        }
    }

    /// <summary>Visible month of the drop-down calendar (coerced into the display bounds).</summary>
    public DateTime DisplayDate
    {
        get => (DateTime)GetValue(DisplayDateProperty)!;
        set => SetValue(DisplayDateProperty, CoerceDisplayDate(value.Date));
    }

    /// <summary>Lower display bound (null is unbounded; beyond End throws like Calendar).</summary>
    public DateTime? DisplayDateStart
    {
        get => (DateTime?)GetValue(DisplayDateStartProperty);
        set
        {
            if (value is not null && DisplayDateEnd is DateTime end && value.Value.Date > end)
                throw new ArgumentOutOfRangeException(nameof(value), "DisplayDateStart must not pass DisplayDateEnd.");
            SetValue(DisplayDateStartProperty, value?.Date);
        }
    }

    /// <summary>Upper display bound (null is unbounded; before Start throws like Calendar).</summary>
    public DateTime? DisplayDateEnd
    {
        get => (DateTime?)GetValue(DisplayDateEndProperty);
        set
        {
            if (value is not null && DisplayDateStart is DateTime start && value.Value.Date < start)
                throw new ArgumentOutOfRangeException(nameof(value), "DisplayDateEnd must not precede DisplayDateStart.");
            SetValue(DisplayDateEndProperty, value?.Date);
        }
    }

    /// <summary>First column of the drop-down week grid (default Sunday, like Calendar).</summary>
    public DayOfWeek FirstDayOfWeek
    {
        get => (DayOfWeek)GetValue(FirstDayOfWeekProperty)!;
        set => SetValue(FirstDayOfWeekProperty, value);
    }

    /// <summary>Whether the drop-down calendar is open.</summary>
    public bool IsDropDownOpen
    {
        get => (bool)GetValue(IsDropDownOpenProperty)!;
        set => SetValue(IsDropDownOpenProperty, value);
    }

    /// <summary>Whether the drop-down marks today (default true, like Calendar).</summary>
    public bool IsTodayHighlighted
    {
        get => (bool)GetValue(IsTodayHighlightedProperty)!;
        set => SetValue(IsTodayHighlightedProperty, value);
    }

    /// <summary>Edited text (setting it commits a parseable date like WPF).</summary>
    public string Text
    {
        get => (string?)GetValue(TextProperty) ?? string.Empty;
        set => SetValue(TextProperty, value ?? string.Empty);
    }

    /// <summary>Drop-down calendar (proof/test seam; day cells resolve through it).</summary>
    internal Calendar DropDownCalendar => _calendar;

    /// <summary>Display format (matches DatePicker.SelectedDateFormat; Short renders "d", Long renders "D").</summary>
    public DatePickerFormat SelectedDateFormat
    {
        get => (DatePickerFormat?)GetValue(SelectedDateFormatProperty) ?? DatePickerFormat.Short;
        set => SetValue(SelectedDateFormatProperty, value);
    }

    /// <summary>Text field (proof/test seam for focus and caret).</summary>
    internal TextBox TextField => _box;

    /// <summary>Typed text field (the WPF template part type).</summary>
    internal DatePickerTextBox DateTextBox => _box;

    /// <summary>Drop-down toggle (proof/test seam for the clickable rect).</summary>
    internal Button ToggleButton => _toggle;

    /// <summary>Arranged bounds of the drop-down toggle (gallery proof seam; false when not laid out).</summary>
    public bool TryGetToggleRect(out WpfRect rect)
    {
        var r = _toggle.RenderRect;
        if (r.Width > 0 && r.Height > 0)
        {
            rect = r;
            return true;
        }
        rect = default;
        return false;
    }

    /// <summary>Raised when the selected day changes (matches DatePicker.SelectedDateChanged).</summary>
    public event SelectionChangedEventHandler? SelectedDateChanged;

    /// <summary>Raised after the drop-down opens (matches DatePicker.CalendarOpened).</summary>
    public event EventHandler? CalendarOpened;

    /// <summary>Raised after the drop-down closes (matches DatePicker.CalendarClosed).</summary>
    public event EventHandler? CalendarClosed;

    /// <summary>Raised when committed text is not a usable date (matches DatePicker.DateValidationError).</summary>
    public event EventHandler<DatePickerDateValidationErrorEventArgs>? DateValidationError;

    /// <summary>Whether a day can be picked (inside the display bounds).</summary>
    public bool IsSelectable(DateTime day)
    {
        var d = day.Date;
        if (DisplayDateStart is DateTime start && d < start) return false;
        if (DisplayDateEnd is DateTime end && d > end) return false;
        return true;
    }

    /// <summary>Commits the current text: empty clears, parseable sets the date, else the error event fires.</summary>
    public void CommitText() => CommitText(_box.Text);

    private string Format(DateTime day) =>
        day.ToString(SelectedDateFormat == DatePickerFormat.Long ? "D" : "d", CultureInfo.CurrentCulture);

    private void OnSelectedDateFormatChanged(DatePickerFormat oldValue, DatePickerFormat newValue)
    {
        if (oldValue == newValue) return;
        if (SelectedDate is DateTime day && !_syncing)
        {
            _syncing = true;
            try
            {
                _box.Text = Format(day);
                SetValue(TextProperty, _box.Text);
            }
            finally { _syncing = false; }
        }
        UpdateSources();
        InvalidateVisual();
    }

    private static DateTime? TryParseDate(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces, out var current))
            return current.Date;
        if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var invariant))
            return invariant.Date;
        return null;
    }

    private void CommitText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            SelectedDate = null;
            return;
        }
        var parsed = TryParseDate(text);
        if (parsed is null)
        {
            DateValidationError?.Invoke(this, new DatePickerDateValidationErrorEventArgs(
                new FormatException($"'{text}' is not a recognized date."), text));
            return;
        }
        if (!IsSelectable(parsed.Value))
        {
            DateValidationError?.Invoke(this, new DatePickerDateValidationErrorEventArgs(
                new ArgumentOutOfRangeException(nameof(text),
                    $"Cannot select {parsed:yyyy-MM-dd}: it is outside the display range."), text));
            return;
        }
        SelectedDate = parsed;
    }

    private DateTime CoerceDisplayDate(DateTime day)
    {
        if (DisplayDateStart is DateTime start && day < start) return start;
        if (DisplayDateEnd is DateTime end && day > end) return end;
        return day;
    }

    private void OnSelectedDatePropertyChanged(DateTime? oldValue, DateTime? newValue)
    {
        if (!_syncing)
        {
            _syncing = true;
            try
            {
                if (newValue is DateTime day)
                {
                    DisplayDate = day;
                    _calendar.DisplayDate = day;
                    _calendar.SelectedDate = day;
                    _box.Text = Format(day);
                    SetValue(TextProperty, _box.Text);
                }
                else
                {
                    _calendar.SelectedDate = null;
                    _box.Text = string.Empty;
                    SetValue(TextProperty, string.Empty);
                }
            }
            finally { _syncing = false; }
        }
        var removed = oldValue is DateTime prev ? new List<object> { prev } : new List<object>();
        var added = newValue is DateTime next ? new List<object> { next } : new List<object>();
        if (removed.Count > 0 || added.Count > 0)
            SelectedDateChanged?.Invoke(this, new SelectionChangedEventArgs(SelectedDateChangedEvent, removed, added));
        UpdateSources();
        InvalidateVisual();
    }

    private void OnDisplayDatePropertyChanged(DateTime oldValue, DateTime newValue)
    {
        var coerced = CoerceDisplayDate(newValue);
        if (coerced != newValue)
        {
            SetValue(DisplayDateProperty, coerced);
            return;
        }
        if (_syncing) return;
        _calendar.DisplayDate = coerced;
    }

    private void OnDisplayBoundChanged()
    {
        if (_syncing) return;
        _syncing = true;
        try
        {
            DisplayDate = DisplayDate;
            SyncCalendarBounds();
        }
        finally { _syncing = false; }
    }

    private void SyncCalendarBounds()
    {
        _calendar.DisplayDateStart = DisplayDateStart;
        _calendar.DisplayDateEnd = DisplayDateEnd;
        _calendar.DisplayDate = DisplayDate;
    }

    private void OnFirstDayOfWeekChanged() => _calendar.FirstDayOfWeek = FirstDayOfWeek;

    private void OnTodayHighlightedChanged() => _calendar.IsTodayHighlighted = IsTodayHighlighted;

    private void OnDropDownPropertyChanged(bool oldValue, bool newValue)
    {
        if (oldValue == newValue) return;
        if (newValue)
        {
            _calendar.DisplayDate = DisplayDate;
            _popup.IsOpen = true;
        }
        else if (_popup.IsOpen) _popup.IsOpen = false;
        InvalidateVisual();
    }

    private void OnTextPropertyChanged(string? oldValue, string? newValue)
    {
        if (_syncing) return;
        var text = newValue ?? string.Empty;
        if (_box.Text == text)
        {
            // Programmatic set that only reached the DP (e.g. a binding
            // push): still commit it like WPF does for Text sets.
            if (oldValue != newValue) CommitText(text);
            return;
        }
        _syncing = true;
        try { _box.Text = text; }
        finally { _syncing = false; }
        if (oldValue != newValue) CommitText(text);
        UpdateSources();
        InvalidateVisual();
    }

    private void OnBoxTextChanged()
    {
        if (_syncing) return;
        var text = _box.Text;
        if (Text == text) return;
        _syncing = true;
        try { SetValue(TextProperty, text); }
        finally { _syncing = false; }
        UpdateSources();
        InvalidateVisual();
    }

    private void OnCalendarPicked(SelectionChangedEventArgs e)
    {
        var picked = e.AddedItems.Count > 0 ? e.AddedItems[e.AddedItems.Count - 1] as DateTime? : null;
        if (picked is null) return;
        if (picked != SelectedDate) SetSelectedDate(picked);
        if (IsDropDownOpen) IsDropDownOpen = false;
    }

    private void SetSelectedDate(DateTime? day)
    {
        if (day is not null && !IsSelectable(day.Value))
            throw new ArgumentOutOfRangeException(nameof(day),
                $"Cannot select {day:yyyy-MM-dd}: it is outside the display range.");
        SetValue(SelectedDateProperty, day?.Date);
    }

    /// <summary>Drop-down toggle: opens and closes the calendar popup.</summary>
    public void ToggleDropDown() => IsDropDownOpen = !IsDropDownOpen;

    public override void OnGotFocus()
    {
        // Typing lands in the text field: the picker itself never keeps
        // focus, matching WPF where the DatePickerTextBox owns the caret.
        if (!_box.IsFocused) _box.Focus();
        InvalidateVisual();
    }

    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonDown(e);
        // Snapshot before light-dismiss runs later in the same press: the
        // toggle flips from the pre-press state, so pressing it while open
        // closes (the dismiss already shut the popup) and pressing it
        // while shut opens.
        _wasOpenAtPress = IsDropDownOpen;
    }

    public override void OnRoutedEvent(WasmRoutedEvent e)
    {
        if (!e.Handled && e.Phase == WasmRoutedPhase.Bubbling &&
            e.Name == WasmRoutedEvent.KeyDownName && IsEffectivelyEnabled &&
            e.Source is Core.WasmInputEvent.Key k && k.Args.IsDown && k.Args.Key == "Enter")
        {
            CommitText();
            e.Handled = true;
        }
    }

    protected override WpfSize MeasureCore(WpfSize available)
    {
        var toggle = new WpfSize(ToggleWidth, 0);
        _toggle.Measure(new WpfSize(ToggleWidth, available.Height));
        toggle = new WpfSize(ToggleWidth, _toggle.DesiredSize.Height);
        _box.Measure(new WpfSize(Math.Max(0, available.Width - ToggleWidth), available.Height));
        _popup.Measure(available);
        return new WpfSize(
            _box.DesiredSize.Width + ToggleWidth,
            Math.Max(_box.DesiredSize.Height, toggle.Height));
    }

    protected override void ArrangeCore(WpfRect finalRect)
    {
        var boxWidth = Math.Max(0, finalRect.Width - ToggleWidth);
        _box.Arrange(new WpfRect(finalRect.X, finalRect.Y, boxWidth, finalRect.Height));
        _toggle.Arrange(new WpfRect(finalRect.X + boxWidth, finalRect.Y, ToggleWidth, finalRect.Height));
        // The popup slot itself stays empty so it never swallows presses
        // meant for the text field or the toggle; the floating child is
        // placed by Popup.ArrangeCore and reached through overlay routing.
        _popup.Arrange(new WpfRect(finalRect.X, finalRect.Y, 0, 0));
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (RenderRect.Width <= 0 || RenderRect.Height <= 0) return;
        RenderVisible(dc, _box);
        RenderVisible(dc, _toggle);
        var toggle = _toggle.RenderRect;
        if (toggle.Width > 0 && toggle.Height > 0)
        {
            var cx = (float)(toggle.X + toggle.Width / 2);
            var cy = (float)(toggle.Y + toggle.Height / 2);
            dc.DrawLine(cx - 5, cy - 3, cx, cy + 3, 2, 0x000000FFu);
            dc.DrawLine(cx, cy + 3, cx + 5, cy - 3, 2, 0x000000FFu);
        }
        RenderVisible(dc, _popup);
    }
}