using System.Collections.ObjectModel;
using System.Globalization;

namespace DesktopWasm.Wpf;

// Gallery DateAndTime/CalendarPage slice (slice-37): the detail page hosts a
// bare <Calendar/>, which the loader rejected as an unknown element. This is
// a shim, not a port: month/year/decade selection grids composed from the
// proven Button/TextBlock/Grid paths (real pointer clicks, keyboard moves,
// blackout/range guards, SelectedDate/SelectedDates sync and month paint),
// with the WPF public names so the default converter namespace rule maps
// converted references. Retained limits: invariant (English) month/day
// names, no templating, no drag selection, no time components (dates only).
/// <summary>Calendar selection behavior (matches System.Windows.Controls.CalendarSelectionMode).</summary>
public enum CalendarSelectionMode
{
    SingleDate = 0,
    SingleRange = 1,
    MultipleRange = 2,
}

/// <summary>Calendar display granularity (matches System.Windows.Controls.CalendarMode).</summary>
public enum CalendarMode
{
    Month = 0,
    Year = 1,
    Decade = 2,
}

/// <summary>Blackout span (matches System.Windows.Controls.CalendarDateRange).</summary>
public sealed class CalendarDateRange : IEquatable<CalendarDateRange>
{
    public CalendarDateRange(DateTime day) : this(day, day)
    {
    }

    public CalendarDateRange(DateTime start, DateTime end)
    {
        if (end.Date < start.Date)
            throw new ArgumentException("End must not be earlier than Start.", nameof(end));
        Start = start.Date;
        End = end.Date;
    }

    /// <summary>First blacked-out day (date part only).</summary>
    public DateTime Start { get; }

    /// <summary>Last blacked-out day, inclusive (date part only).</summary>
    public DateTime End { get; }

    /// <summary>Whether the date part of <paramref name="day"/> falls inside the span.</summary>
    public bool Contains(DateTime day) => day.Date >= Start && day.Date <= End;

    public bool Equals(CalendarDateRange? other) =>
        other is not null && other.Start == Start && other.End == End;

    public override bool Equals(object? obj) => Equals(obj as CalendarDateRange);

    public override int GetHashCode() => HashCode.Combine(Start, End);

    public override string ToString() => Start == End
        ? Start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
        : Start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".." +
          End.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}

/// <summary>Blackout set (matches System.Windows.Controls.CalendarBlackoutDatesCollection).</summary>
public sealed class CalendarBlackoutDatesCollection : ObservableCollection<CalendarDateRange>
{
    /// <summary>Whether the date part of <paramref name="day"/> is blacked out.</summary>
    public bool Contains(DateTime day)
    {
        var d = day.Date;
        foreach (var range in this)
            if (range.Contains(d)) return true;
        return false;
    }

    /// <summary>Whether any day in [<paramref name="start"/>, <paramref name="end"/>] is blacked out.</summary>
    public bool Contains(DateTime start, DateTime end)
    {
        var s = start.Date;
        var e = end.Date;
        foreach (var range in this)
            if (range.Start <= e && range.End >= s) return true;
        return false;
    }

    /// <summary>Blacks out every past day before today (matches the WPF helper).</summary>
    public void AddDatesInPast()
    {
        var today = DateTime.Today;
        if (today > DateTime.MinValue)
            Add(new CalendarDateRange(DateTime.MinValue, today.AddDays(-1)));
    }
}

/// <summary>Selected-day set (matches System.Windows.Controls.SelectedDatesCollection).</summary>
public sealed class SelectedDatesCollection : ObservableCollection<DateTime>
{
    internal SelectedDatesCollection(Func<DateTime, bool>? isAllowed = null) => IsAllowed = isAllowed;

    internal Func<DateTime, bool>? IsAllowed { get; set; }

    /// <summary>Adds several dates through the same guards as <see cref="Add"/>.</summary>
    public void AddRange(IEnumerable<DateTime> dates)
    {
        ArgumentNullException.ThrowIfNull(dates);
        foreach (var date in dates) Add(date);
    }

    protected override void InsertItem(int index, DateTime item)
    {
        var day = item.Date;
        if (Contains(day)) return;
        if (IsAllowed is not null && !IsAllowed(day))
            throw new ArgumentOutOfRangeException(nameof(item),
                $"Cannot select {day:yyyy-MM-dd}: it is blacked out or outside the display range.");
        base.InsertItem(index, day);
    }

    protected override void SetItem(int index, DateTime item)
    {
        var day = item.Date;
        if (IsAllowed is not null && !IsAllowed(day))
            throw new ArgumentOutOfRangeException(nameof(item),
                $"Cannot select {day:yyyy-MM-dd}: it is blacked out or outside the display range.");
        base.SetItem(index, day);
    }
}

/// <summary>Display-date change payload (matches System.Windows.Controls.CalendarDateChangedEventArgs).</summary>
public sealed class CalendarDateChangedEventArgs : RoutedEventArgs
{
    public CalendarDateChangedEventArgs(DateTime? removedDate, DateTime? addedDate)
    {
        RemovedDate = removedDate?.Date;
        AddedDate = addedDate?.Date;
    }

    /// <summary>Previously displayed date (null when unset).</summary>
    public DateTime? RemovedDate { get; }

    /// <summary>Newly displayed date (null when unset).</summary>
    public DateTime? AddedDate { get; }
}

/// <summary>Display-mode change payload (matches System.Windows.Controls.CalendarModeChangedEventArgs).</summary>
public sealed class CalendarModeChangedEventArgs : EventArgs
{
    public CalendarModeChangedEventArgs(CalendarMode oldMode, CalendarMode newMode)
    {
        OldMode = oldMode;
        NewMode = newMode;
    }

    /// <summary>Mode before the change.</summary>
    public CalendarMode OldMode { get; }

    /// <summary>Mode after the change.</summary>
    public CalendarMode NewMode { get; }
}

/// <summary>
/// Month/year/decade date picker (matches System.Windows.Controls.Calendar).
/// The month grid, year grid and decade grid are composed from Grid, Button
/// and TextBlock children, so layout, paint, hit-testing and the real
/// pointer path are the proven ones; this class only owns date math,
/// selection sync, guards and keyboard moves.
/// </summary>
public sealed class Calendar : Control
{
    /// <summary>Selection token (matches Calendar.SelectedDateChangedEvent).</summary>
    public static readonly RoutedEvent SelectedDateChangedEvent = EventManager.RegisterRoutedEvent(
        "SelectedDateChanged", RoutingStrategy.Bubble, typeof(SelectionChangedEventHandler), typeof(Calendar));

    /// <summary>Selection-set token (matches Calendar.SelectedDatesChangedEvent).</summary>
    public static readonly RoutedEvent SelectedDatesChangedEvent = EventManager.RegisterRoutedEvent(
        "SelectedDatesChanged", RoutingStrategy.Bubble, typeof(SelectionChangedEventHandler), typeof(Calendar));

    /// <summary>Selected-date slot (matches Calendar.SelectedDateProperty).</summary>
    public static readonly DependencyProperty SelectedDateProperty =
        DependencyProperty.Register("SelectedDate", typeof(DateTime?), typeof(Calendar),
            new FrameworkPropertyMetadata(null, (d, e) => ((Calendar)d).OnSelectedDatePropertyChanged(
                (DateTime?)e.OldValue, (DateTime?)e.NewValue)));

    /// <summary>Visible-month slot (matches Calendar.DisplayDateProperty).</summary>
    public static readonly DependencyProperty DisplayDateProperty =
        DependencyProperty.Register("DisplayDate", typeof(DateTime), typeof(Calendar),
            new FrameworkPropertyMetadata(default(DateTime), (d, e) => ((Calendar)d).OnDisplayDatePropertyChanged(
                (DateTime)e.OldValue, (DateTime)e.NewValue)));

    /// <summary>Lower display bound (matches Calendar.DisplayDateStartProperty).</summary>
    public static readonly DependencyProperty DisplayDateStartProperty =
        DependencyProperty.Register("DisplayDateStart", typeof(DateTime?), typeof(Calendar),
            new FrameworkPropertyMetadata(null, (d, e) => ((Calendar)d).OnDisplayBoundChanged()));

    /// <summary>Upper display bound (matches Calendar.DisplayDateEndProperty).</summary>
    public static readonly DependencyProperty DisplayDateEndProperty =
        DependencyProperty.Register("DisplayDateEnd", typeof(DateTime?), typeof(Calendar),
            new FrameworkPropertyMetadata(null, (d, e) => ((Calendar)d).OnDisplayBoundChanged()));

    /// <summary>Granularity slot (matches Calendar.DisplayModeProperty).</summary>
    public static readonly DependencyProperty DisplayModeProperty =
        DependencyProperty.Register("DisplayMode", typeof(CalendarMode), typeof(Calendar),
            new FrameworkPropertyMetadata(CalendarMode.Month, (d, e) => ((Calendar)d).OnDisplayModePropertyChanged(
                (CalendarMode)e.OldValue, (CalendarMode)e.NewValue)));

    /// <summary>Selection-behavior slot (matches Calendar.SelectionModeProperty).</summary>
    public static readonly DependencyProperty SelectionModeProperty =
        DependencyProperty.Register("SelectionMode", typeof(CalendarSelectionMode), typeof(Calendar),
            new FrameworkPropertyMetadata(CalendarSelectionMode.SingleDate, (d, e) => ((Calendar)d).OnSelectionModePropertyChanged()));

    /// <summary>Today-marker slot (matches Calendar.IsTodayHighlightedProperty).</summary>
    public static readonly DependencyProperty IsTodayHighlightedProperty =
        DependencyProperty.Register("IsTodayHighlighted", typeof(bool), typeof(Calendar),
            new FrameworkPropertyMetadata(true, (d, e) => ((Calendar)d).RefreshDayVisuals()));

    /// <summary>Week-start slot (matches Calendar.FirstDayOfWeekProperty).</summary>
    public static readonly DependencyProperty FirstDayOfWeekProperty =
        DependencyProperty.Register("FirstDayOfWeek", typeof(DayOfWeek), typeof(Calendar),
            new FrameworkPropertyMetadata(DayOfWeek.Sunday, (d, e) => ((Calendar)d).Rebuild()));

    private readonly Grid _root = new();
    private readonly Dictionary<DateTime, Button> _dayCells = new();
    private readonly List<Button> _modeCells = new();
    private Button? _prevButton;
    private Button? _nextButton;
    private Button? _headerButton;
    private DateTime? _anchor;
    private DateTime? _lastNotifiedDate;
    private bool _syncing;
    private bool _focused;

    /// <summary>Builds the empty month view on today's month (matches the WPF default DisplayDate).</summary>
    public Calendar()
    {
        Focusable = true;
        SelectedDates = new SelectedDatesCollection(IsSelectable);
        SelectedDates.CollectionChanged += (_, _) => OnSelectedDatesModified();
        BlackoutDates = new CalendarBlackoutDatesCollection();
        BlackoutDates.CollectionChanged += (_, _) => Rebuild();
        AddChild(_root);
        DisplayDate = DateTime.Today;
        Rebuild();
    }

    /// <summary>Selected day (null clears; blackout/out-of-range throws like WPF).</summary>
    public DateTime? SelectedDate
    {
        get => (DateTime?)GetValue(SelectedDateProperty);
        set
        {
            var day = value?.Date;
            if (day is not null && !IsSelectable(day.Value))
                throw new ArgumentOutOfRangeException(nameof(value),
                    $"Cannot select {day:yyyy-MM-dd}: it is blacked out or outside the display range.");
            SetValue(SelectedDateProperty, day);
        }
    }

    /// <summary>Visible date (coerced into the display bounds; defaults to today).</summary>
    public DateTime DisplayDate
    {
        get => (DateTime)GetValue(DisplayDateProperty)!;
        set => SetValue(DisplayDateProperty, CoerceDisplayDate(value.Date));
    }

    /// <summary>Lower display bound (null is unbounded; beyond End throws like WPF).</summary>
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

    /// <summary>Upper display bound (null is unbounded; before Start throws like WPF).</summary>
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

    /// <summary>Visible granularity (switching raises DisplayModeChanged and rebuilds).</summary>
    public CalendarMode DisplayMode
    {
        get => (CalendarMode)GetValue(DisplayModeProperty)!;
        set => SetValue(DisplayModeProperty, value);
    }

    /// <summary>Selection behavior (switching clears the selection, like WPF).</summary>
    public CalendarSelectionMode SelectionMode
    {
        get => (CalendarSelectionMode)GetValue(SelectionModeProperty)!;
        set => SetValue(SelectionModeProperty, value);
    }

    /// <summary>Month-view part style slot (matches Calendar.CalendarItemStyleProperty).</summary>
    public static readonly DependencyProperty CalendarItemStyleProperty =
        DependencyProperty.Register("CalendarItemStyle", typeof(Style), typeof(Calendar),
            new FrameworkPropertyMetadata(null));

    /// <summary>Style applied to the month-view part (Material themes bind it by template).</summary>
    public Style? CalendarItemStyle
    {
        get => (Style?)GetValue(CalendarItemStyleProperty);
        set => SetValue(CalendarItemStyleProperty, value);
    }

    /// <summary>Button part style slot (matches Calendar.CalendarButtonStyleProperty).</summary>
    public static readonly DependencyProperty CalendarButtonStyleProperty =
        DependencyProperty.Register("CalendarButtonStyle", typeof(Style), typeof(Calendar),
            new FrameworkPropertyMetadata(null));

    /// <summary>Style applied to the month/year button parts (Material themes bind it by template).</summary>
    public Style? CalendarButtonStyle
    {
        get => (Style?)GetValue(CalendarButtonStyleProperty);
        set => SetValue(CalendarButtonStyleProperty, value);
    }

    /// <summary>Day-button part style slot (matches Calendar.CalendarDayButtonStyleProperty).</summary>
    public static readonly DependencyProperty CalendarDayButtonStyleProperty =
        DependencyProperty.Register("CalendarDayButtonStyle", typeof(Style), typeof(Calendar),
            new FrameworkPropertyMetadata(null));

    /// <summary>Style applied to the day button parts (Material themes bind it by template).</summary>
    public Style? CalendarDayButtonStyle
    {
        get => (Style?)GetValue(CalendarDayButtonStyleProperty);
        set => SetValue(CalendarDayButtonStyleProperty, value);
    }

    /// <summary>Whether today carries the marker border (default true, like WPF).</summary>
    public bool IsTodayHighlighted
    {
        get => (bool)GetValue(IsTodayHighlightedProperty)!;
        set => SetValue(IsTodayHighlightedProperty, value);
    }

    /// <summary>First column of the week grid (default Sunday, like WPF).</summary>
    public DayOfWeek FirstDayOfWeek
    {
        get => (DayOfWeek)GetValue(FirstDayOfWeekProperty)!;
        set => SetValue(FirstDayOfWeekProperty, value);
    }

    /// <summary>Selected days (blackout/out-of-range adds throw; duplicates are ignored).</summary>
    public SelectedDatesCollection SelectedDates { get; }

    /// <summary>Disabled spans (changes rebuild the enabled states).</summary>
    public CalendarBlackoutDatesCollection BlackoutDates { get; }

    /// <summary>Raised when the selected day changes (matches Calendar.SelectedDateChanged).</summary>
    public event SelectionChangedEventHandler? SelectedDateChanged;

    /// <summary>Raised when the selected set changes (matches Calendar.SelectedDatesChanged).</summary>
    public event SelectionChangedEventHandler? SelectedDatesChanged;

    /// <summary>Raised when the visible date moves (matches Calendar.DisplayDateChanged).</summary>
    public event EventHandler<CalendarDateChangedEventArgs>? DisplayDateChanged;

    /// <summary>Raised when the granularity changes (matches Calendar.DisplayModeChanged).</summary>
    public event EventHandler<CalendarModeChangedEventArgs>? DisplayModeChanged;

    /// <summary>Raised when the selection behavior changes (matches Calendar.SelectionModeChanged).</summary>
    public event EventHandler? SelectionModeChanged;

    /// <summary>Whether a day can be picked (inside the bounds and not blacked out).</summary>
    public bool IsSelectable(DateTime day)
    {
        var d = day.Date;
        if (DisplayDateStart is DateTime start && d < start) return false;
        if (DisplayDateEnd is DateTime end && d > end) return false;
        return !BlackoutDates.Contains(d);
    }

    /// <summary>Arranged bounds of a visible day cell (proof/test seam; false when off-grid).</summary>
    public bool TryGetDateRect(DateTime day, out WpfRect rect)
    {
        if (_dayCells.TryGetValue(day.Date, out var cell) &&
            cell.ArrangedRect.Width > 0 && cell.ArrangedRect.Height > 0)
        {
            rect = cell.ArrangedRect;
            return true;
        }
        rect = default;
        return false;
    }

    private DateTime CoerceDisplayDate(DateTime day)
    {
        if (DisplayDateStart is DateTime start && day < start) return start;
        if (DisplayDateEnd is DateTime end && day > end) return end;
        return day;
    }

    private void OnSelectedDatePropertyChanged(DateTime? oldValue, DateTime? newValue)
    {
        if (_syncing) return;
        _syncing = true;
        try
        {
            SelectedDates.Clear();
            if (newValue is DateTime day)
            {
                SelectedDates.Add(day);
                _anchor = day;
            }
            else _anchor = null;
        }
        finally { _syncing = false; }
        NotifySelectionChanged();
    }

    private void OnSelectedDatesModified()
    {
        if (_syncing) return;
        _syncing = true;
        try
        {
            var last = SelectedDates.Count > 0 ? SelectedDates[SelectedDates.Count - 1] : (DateTime?)null;
            SetValue(SelectedDateProperty, last);
            _anchor ??= last;
        }
        finally { _syncing = false; }
        NotifySelectionChanged();
    }

    private void NotifySelectionChanged()
    {
        var removed = _lastNotifiedDate is DateTime prev && (SelectedDates.Count == 0 || !SelectedDates.Contains(prev))
            ? new List<object> { prev } : new List<object>();
        var added = SelectedDates.Count > 0 && SelectedDates[SelectedDates.Count - 1] != _lastNotifiedDate
            ? new List<object> { SelectedDates[SelectedDates.Count - 1] } : new List<object>();
        _lastNotifiedDate = SelectedDates.Count > 0 ? SelectedDates[SelectedDates.Count - 1] : null;
        SelectedDatesChanged?.Invoke(this, new SelectionChangedEventArgs(SelectedDatesChangedEvent, removed, added));
        if (removed.Count > 0 || added.Count > 0)
            SelectedDateChanged?.Invoke(this, new SelectionChangedEventArgs(SelectedDateChangedEvent, removed, added));
        RefreshDayVisuals();
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
        if (coerced == oldValue) return;
        DisplayDateChanged?.Invoke(this, new CalendarDateChangedEventArgs(oldValue, coerced));
        Rebuild();
    }

    private void OnDisplayBoundChanged()
    {
        DisplayDate = DisplayDate;
        Rebuild();
    }

    private void OnDisplayModePropertyChanged(CalendarMode oldValue, CalendarMode newValue)
    {
        if (oldValue == newValue) return;
        DisplayModeChanged?.Invoke(this, new CalendarModeChangedEventArgs(oldValue, newValue));
        Rebuild();
    }

    private void OnSelectionModePropertyChanged()
    {
        var had = _lastNotifiedDate;
        _syncing = true;
        try
        {
            SelectedDates.Clear();
            SetValue(SelectedDateProperty, null);
        }
        finally { _syncing = false; }
        _anchor = null;
        _lastNotifiedDate = null;
        var removed = had is DateTime prev ? new List<object> { prev } : new List<object>();
        SelectedDatesChanged?.Invoke(this, new SelectionChangedEventArgs(
            SelectedDatesChangedEvent, removed, new List<object>()));
        if (removed.Count > 0)
            SelectedDateChanged?.Invoke(this, new SelectionChangedEventArgs(
                SelectedDateChangedEvent, removed, new List<object>()));
        SelectionModeChanged?.Invoke(this, EventArgs.Empty);
        RefreshDayVisuals();
    }

    private void OnDayClick(DateTime day)
    {
        if (!IsSelectable(day)) return;
        Focus();
        switch (SelectionMode)
        {
            case CalendarSelectionMode.SingleDate:
                SelectedDate = day;
                break;
            case CalendarSelectionMode.SingleRange:
                // Plain clicks reset the anchor like WPF (range grows through
                // Shift+arrows and SelectedDates, which carry the anchor).
                SelectedDate = day;
                break;
            default:
                _syncing = true;
                try
                {
                    if (SelectedDates.Contains(day)) SelectedDates.Remove(day);
                    else SelectedDates.Add(day);
                    _anchor ??= day;
                }
                finally { _syncing = false; }
                NotifySelectionChanged();
                break;
        }
    }

    private void SelectRange(DateTime from, DateTime to)
    {
        var start = from <= to ? from : to;
        var end = from <= to ? to : from;
        var days = new List<DateTime>();
        for (var d = start; d <= end; d = d.AddDays(1))
        {
            if (!IsSelectable(d))
                throw new ArgumentOutOfRangeException(nameof(to),
                    $"Cannot extend the range over {d:yyyy-MM-dd}: it is blacked out or outside the display range.");
            days.Add(d);
        }
        _syncing = true;
        try
        {
            SelectedDates.Clear();
            SelectedDates.AddRange(days);
            SetValue(SelectedDateProperty, days.Count > 0 ? days[days.Count - 1] : null);
        }
        finally { _syncing = false; }
        NotifySelectionChanged();
    }

    private void MoveSelection(int days, bool extend)
    {
        var current = SelectedDate ?? DisplayDate;
        var target = current.AddDays(days).Date;
        if (!IsSelectable(target)) return;
        if (extend && SelectionMode != CalendarSelectionMode.SingleDate && _anchor is DateTime from)
            SelectRange(from, target);
        else if (SelectionMode == CalendarSelectionMode.MultipleRange && !extend)
        {
            if (target.Month != DisplayDate.Month || target.Year != DisplayDate.Year)
                DisplayDate = new DateTime(target.Year, target.Month, 1);
        }
        else
            SelectedDate = target;
    }

    private static void Place(UIElement child, int row, int column, int columnSpan = 1)
    {
        child.SetAttached("Grid.Row", row);
        child.SetAttached("Grid.Column", column);
        if (columnSpan != 1) child.SetAttached("Grid.ColumnSpan", columnSpan);
    }

    private void Rebuild()
    {
        foreach (var child in _root.LogicalChildren.ToList()) _root.RemoveChild(child);
        _dayCells.Clear();
        _modeCells.Clear();
        _root.RowDefinitions.Clear();
        _root.ColumnDefinitions.Clear();
        for (var c = 0; c < 7; c++)
            _root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star() });
        switch (DisplayMode)
        {
            case CalendarMode.Year: BuildYearGrid(); break;
            case CalendarMode.Decade: BuildDecadeGrid(); break;
            default: BuildMonthGrid(); break;
        }
        InvalidateMeasure();
        InvalidateVisual();
    }

    private void BuildChrome(string title, Action<int> step)
    {
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _prevButton = new Button { Content = "<" };
        _prevButton.Click += (_, _) => { step(-1); Focus(); };
        Place(_prevButton, 0, 0);
        _root.AddChild(_prevButton);
        _headerButton = new Button { Content = title, FontSize = 16 };
        _headerButton.Click += (_, _) =>
        {
            Focus();
            DisplayMode = DisplayMode switch
            {
                CalendarMode.Month => CalendarMode.Year,
                CalendarMode.Year => CalendarMode.Decade,
                _ => CalendarMode.Decade,
            };
        };
        Place(_headerButton, 0, 1, 5);
        _root.AddChild(_headerButton);
        _nextButton = new Button { Content = ">" };
        _nextButton.Click += (_, _) => { step(1); Focus(); };
        Place(_nextButton, 0, 6);
        _root.AddChild(_nextButton);
    }

    private void BuildMonthGrid()
    {
        var view = DisplayDate;
        var first = new DateTime(view.Year, view.Month, 1);
        BuildChrome(
            first.ToString("MMMM yyyy", CultureInfo.InvariantCulture),
            delta => DisplayDate = first.AddMonths(delta));
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var abbrev = CultureInfo.InvariantCulture.DateTimeFormat.AbbreviatedDayNames;
        for (var c = 0; c < 7; c++)
        {
            var dow = (DayOfWeek)(((int)FirstDayOfWeek + c) % 7);
            var label = new TextBlock
            {
                Text = abbrev[(int)dow],
                FontWeight = FontWeights.Bold,
                TextAlignment = TextAlignment.Center,
            };
            Place(label, 1, c);
            _root.AddChild(label);
        }
        var offset = ((int)first.DayOfWeek - (int)FirstDayOfWeek + 7) % 7;
        var cursor = first.AddDays(-offset);
        for (var week = 0; week < 6; week++)
        {
            _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Star() });
            for (var c = 0; c < 7; c++)
            {
                var day = cursor;
                var cell = new Button { Content = day.Day.ToString(CultureInfo.InvariantCulture) };
                if (day.Month != first.Month) cell.Foreground = Brushes.Gray;
                cell.IsEnabled = IsSelectable(day);
                cell.Click += (_, _) => OnDayClick(day);
                Place(cell, 2 + week, c);
                _root.AddChild(cell);
                _dayCells[day] = cell;
                cursor = cursor.AddDays(1);
            }
        }
        RefreshDayVisuals();
    }

    private void BuildYearGrid()
    {
        var year = DisplayDate.Year;
        BuildChrome(year.ToString(CultureInfo.InvariantCulture), delta => DisplayDate = SafeDate(year + delta, DisplayDate.Month, 1));
        var names = CultureInfo.InvariantCulture.DateTimeFormat.AbbreviatedMonthNames;
        for (var row = 0; row < 4; row++)
        {
            _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Star() });
            for (var col = 0; col < 3; col++)
            {
                var month = row * 3 + col + 1;
                var cell = new Button { Content = names[month - 1] };
                var target = SafeDate(year, month, Math.Min(DisplayDate.Day, DateTime.DaysInMonth(year, month)));
                cell.IsEnabled = IsSelectable(target);
                cell.Click += (_, _) =>
                {
                    Focus();
                    DisplayDate = target;
                    DisplayMode = CalendarMode.Month;
                };
                Place(cell, 1 + row, col * 2, 2);
                _root.AddChild(cell);
                _modeCells.Add(cell);
            }
        }
    }

    private void BuildDecadeGrid()
    {
        var year = DisplayDate.Year;
        var start = year / 10 * 10;
        BuildChrome(
            start.ToString(CultureInfo.InvariantCulture) + "-" + (start + 9).ToString(CultureInfo.InvariantCulture),
            delta => DisplayDate = SafeDate(year + delta * 10, DisplayDate.Month, 1));
        for (var row = 0; row < 4; row++)
        {
            _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Star() });
            for (var col = 0; col < 3; col++)
            {
                var cellYear = start - 1 + row * 3 + col;
                var cell = new Button { Content = cellYear.ToString(CultureInfo.InvariantCulture) };
                if (cellYear < start || cellYear > start + 9) cell.Foreground = Brushes.Gray;
                var target = SafeDate(cellYear, DisplayDate.Month, 1);
                cell.IsEnabled = IsSelectable(target);
                cell.Click += (_, _) =>
                {
                    Focus();
                    DisplayDate = target;
                    DisplayMode = CalendarMode.Year;
                };
                Place(cell, 1 + row, col * 2, 2);
                _root.AddChild(cell);
                _modeCells.Add(cell);
            }
        }
    }

    private static DateTime SafeDate(int year, int month, int day)
    {
        year = Math.Clamp(year, 1, 9999);
        month = Math.Clamp(month, 1, 12);
        return new DateTime(year, month, Math.Min(day, DateTime.DaysInMonth(year, month)));
    }

    private void RefreshDayVisuals()
    {
        if (_dayCells.Count == 0) return;
        var today = DateTime.Today;
        foreach (var (day, cell) in _dayCells)
        {
            var selected = SelectedDates.Contains(day);
            cell.Background = selected ? Brushes.DarkBlue : null;
            cell.Foreground = selected ? Brushes.White : day.Month != DisplayDate.Month ? Brushes.Gray : null;
            cell.IsEnabled = IsSelectable(day);
            if (IsTodayHighlighted && day == today)
            {
                cell.BorderBrush = Brushes.DarkBlue;
                cell.BorderThickness = new Thickness(2);
            }
            else cell.BorderThickness = new Thickness(0);
        }
        InvalidateVisual();
    }

    public override void OnGotFocus() { _focused = true; InvalidateVisual(); }

    public override void OnLostFocus() { _focused = false; InvalidateVisual(); }

    public override void OnRoutedEvent(WasmRoutedEvent e)
    {
        if (e.Handled || e.Phase != WasmRoutedPhase.Bubbling) return;
        if (e.Source is not Core.WasmInputEvent ev) return;
        if (ev is Core.WasmInputEvent.Key k && k.Args.IsDown && (_focused || IsKeyboardFocusWithin))
        {
            switch (k.Args.Key)
            {
                case "ArrowLeft": MoveSelection(-1, k.Args.Shift); e.Handled = true; break;
                case "ArrowRight": MoveSelection(1, k.Args.Shift); e.Handled = true; break;
                case "ArrowUp": MoveSelection(-7, k.Args.Shift); e.Handled = true; break;
                case "ArrowDown": MoveSelection(7, k.Args.Shift); e.Handled = true; break;
                case "PageUp":
                    DisplayDate = DisplayDate.AddMonths(k.Args.Shift ? -12 : -1);
                    e.Handled = true;
                    break;
                case "PageDown":
                    DisplayDate = DisplayDate.AddMonths(k.Args.Shift ? 12 : 1);
                    e.Handled = true;
                    break;
            }
        }
    }

    protected override WpfSize MeasureCore(WpfSize available)
    {
        _root.Measure(available);
        return _root.DesiredSize;
    }

    protected override void ArrangeCore(WpfRect finalRect)
    {
        _root.Arrange(finalRect);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var r = RenderRect;
        if (r.Width <= 0 || r.Height <= 0) return;
        dc.DrawRectangle((float)r.X, (float)r.Y, (float)r.Width, (float)r.Height,
            BrushRender.ResolveFill(Background, 0x00000000));
        RenderVisible(dc, _root);
    }
}

/// <summary>Month/year/decade selector button (matches CalendarButton : ButtonBase).</summary>
public sealed class CalendarButton : ButtonBase
{
    /// <summary>Selected-days token (matches CalendarButton.HasSelectedDaysProperty; drives the
    /// Material month-button foreground trigger; producer wiring stays a follow-up).</summary>
    public static readonly DependencyProperty HasSelectedDaysProperty =
        DependencyProperty.Register("HasSelectedDays", typeof(bool), typeof(CalendarButton),
            new FrameworkPropertyMetadata(false));

    /// <summary>Whether the button represents days in the selection.</summary>
    public bool HasSelectedDays
    {
        get => (bool)(GetValue(HasSelectedDaysProperty) ?? false);
        set => SetValue(HasSelectedDaysProperty, value);
    }

    /// <summary>Out-of-month token (matches CalendarDayButton.IsInactiveProperty; the
    /// month-button template dims out-of-range months with the same trigger).</summary>
    public static readonly DependencyProperty IsInactiveProperty =
        DependencyProperty.Register("IsInactive", typeof(bool), typeof(CalendarButton),
            new FrameworkPropertyMetadata(false));

    /// <summary>Whether the button lies outside the display range.</summary>
    public bool IsInactive
    {
        get => (bool)(GetValue(IsInactiveProperty) ?? false);
        set => SetValue(IsInactiveProperty, value);
    }
}

/// <summary>Day selector button (matches CalendarDayButton : ButtonBase).</summary>
public sealed class CalendarDayButton : ButtonBase
{
    /// <summary>Out-of-month token (matches CalendarDayButton.IsInactiveProperty; drives the
    /// Material day dimming trigger; producer wiring stays a follow-up).</summary>
    public static readonly DependencyProperty IsInactiveProperty =
        DependencyProperty.Register("IsInactive", typeof(bool), typeof(CalendarDayButton),
            new FrameworkPropertyMetadata(false));

    /// <summary>Whether the day lies outside the display month.</summary>
    public bool IsInactive
    {
        get => (bool)(GetValue(IsInactiveProperty) ?? false);
        set => SetValue(IsInactiveProperty, value);
    }

    /// <summary>Selection token (matches CalendarDayButton.IsSelectedProperty; drives the
    /// Material day foreground trigger; producer wiring stays a follow-up).</summary>
    public static readonly DependencyProperty IsSelectedProperty =
        DependencyProperty.Register("IsSelected", typeof(bool), typeof(CalendarDayButton),
            new FrameworkPropertyMetadata(false));

    /// <summary>Whether the day is selected.</summary>
    public bool IsSelected
    {
        get => (bool)(GetValue(IsSelectedProperty) ?? false);
        set => SetValue(IsSelectedProperty, value);
    }

    /// <summary>Blackout token (matches CalendarDayButton.IsBlackedOutProperty; drives the
    /// Material blackout opacity/cursor triggers; producer wiring stays a follow-up).</summary>
    public static readonly DependencyProperty IsBlackedOutProperty =
        DependencyProperty.Register("IsBlackedOut", typeof(bool), typeof(CalendarDayButton),
            new FrameworkPropertyMetadata(false));

    /// <summary>Whether the day is blacked out.</summary>
    public bool IsBlackedOut
    {
        get => (bool)(GetValue(IsBlackedOutProperty) ?? false);
        set => SetValue(IsBlackedOutProperty, value);
    }
}

/// <summary>Month-view part (matches CalendarItem; Material themes template it).</summary>
public sealed class CalendarItem : Control
{
    /// <summary>Day-title template key (matches CalendarItem.DayTitleTemplateResourceKey).</summary>
    public static readonly ComponentResourceKey DayTitleTemplateResourceKey =
        new ComponentResourceKey(typeof(CalendarItem), "DayTitleTemplate");

    /// <summary>Day-title template slot (matches CalendarItem.DayTitleTemplateProperty).</summary>
    public static readonly DependencyProperty DayTitleTemplateProperty =
        DependencyProperty.Register("DayTitleTemplate", typeof(DataTemplate), typeof(CalendarItem),
            new FrameworkPropertyMetadata(null));

    /// <summary>Template for day-title headers (defaults from the theme scope).</summary>
    public DataTemplate? DayTitleTemplate
    {
        get => (DataTemplate?)GetValue(DayTitleTemplateProperty);
        set => SetValue(DayTitleTemplateProperty, value);
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        // Component-keyed day-title template: resolves from the inflated
        // template scope (which merges the style's ControlTemplate.Resources),
        // like WPF theme lookup for component resource keys.
        if (DayTitleTemplate is null && TryFindResource(DayTitleTemplateResourceKey, out var found) && found is DataTemplate template)
            DayTitleTemplate = template;
    }
}
