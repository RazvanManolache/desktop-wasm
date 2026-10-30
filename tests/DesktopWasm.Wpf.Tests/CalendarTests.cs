using DesktopWasm.Core;
using DesktopWasm.Graphics;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Gallery Calendar slice (slice-37): the real converted WPFGallery
/// DateAndTime/CalendarPage hosts a bare &lt;Calendar/&gt;, which the XAML
/// loader rejected as an unknown element. Before the slice the engine had
/// no Calendar type at all; the remaining gallery boundary after this slice
/// is Interaction.Triggers, FrameWindow Source and RichTextBox.
/// </summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class CalendarTests
{
    private static readonly DateTime Oct1 = new(2026, 10, 1);
    private static readonly DateTime Oct15 = new(2026, 10, 15);

    private sealed class Harness
    {
        public Harness(UIElement content)
        {
            Root = new Window { Content = content };
            Manager = new WpfInputManager(Router);
            Manager.AddRoot(Root);
            Rerender();
        }

        public Window Root { get; }
        public WasmInputRouter Router { get; } = new();
        public WpfInputManager Manager { get; }

        public void Rerender()
        {
            var mil = new WasmMilCore(new SkiaSurface(800, 600));
            mil.Render(Root, new WpfSize(800, 600), new WpfRect(0, 0, 800, 600));
        }
    }

    private static WpfPoint Center(UIElement e)
    {
        var r = e.ArrangedRect;
        return new WpfPoint(r.X + r.Width / 2, r.Y + r.Height / 2);
    }

    private static void Down(WasmInputRouter router, double x, double y) =>
        router.DispatchPointer(new WasmPointerArgs(new WasmWindowHandle(1), x, y, WasmPointerButton.Left, true));

    private static void Up(WasmInputRouter router, double x, double y) =>
        router.DispatchPointer(new WasmPointerArgs(new WasmWindowHandle(1), x, y, WasmPointerButton.Left, false));

    private static void Key(WasmInputRouter router, string key, bool shift = false) =>
        router.DispatchKeyboard(new WasmKeyArgs(new WasmWindowHandle(1), key, true, false, shift, false));

    private static void Press(Harness h, Button button)
    {
        h.Rerender();
        var p = Center(button);
        Down(h.Router, p.X, p.Y);
        h.Rerender();
        p = Center(button);
        Up(h.Router, p.X, p.Y);
    }

    private static T? FindVisual<T>(FrameworkElement? root) where T : FrameworkElement
    {
        if (root is null) return null;
        if (root is T match) return match;
        foreach (var child in root.LogicalChildren)
        {
            var found = FindVisual<T>(child as FrameworkElement);
            if (found is not null) return found;
        }
        return null;
    }

    private static List<T> FindVisuals<T>(FrameworkElement? root) where T : FrameworkElement
    {
        var found = new List<T>();
        Collect(root, found);
        return found;
    }

    private static void Collect<T>(FrameworkElement? root, List<T> found) where T : FrameworkElement
    {
        if (root is null) return;
        if (root is T match) found.Add(match);
        foreach (var child in root.LogicalChildren) Collect(child as FrameworkElement, found);
    }

    [Fact]
    public void Calendar_defaults_match_wpf()
    {
        var calendar = new Calendar();
        Assert.Null(calendar.SelectedDate);
        Assert.Equal(DateTime.Today, calendar.DisplayDate.Date);
        Assert.Equal(CalendarSelectionMode.SingleDate, calendar.SelectionMode);
        Assert.Equal(CalendarMode.Month, calendar.DisplayMode);
        Assert.True(calendar.IsTodayHighlighted);
        Assert.Equal(DayOfWeek.Sunday, calendar.FirstDayOfWeek);
        Assert.Empty(calendar.SelectedDates);
        Assert.True(calendar.Focusable);
    }

    [Fact]
    public void Calendar_renders_month_header_and_42_day_cells()
    {
        var calendar = new Calendar { DisplayDate = Oct1 };
        var window = new Window { Content = calendar };
        var commands = WasmMilCore.RenderHeadless(window, new WpfSize(800, 600));
        var json = WasmMilCore.ExportFrameJson(commands);
        Assert.Contains("October 2026", json, StringComparison.Ordinal);
        Assert.Contains("15", json, StringComparison.Ordinal);
        var header = FindVisuals<Button>(calendar).FirstOrDefault(b => b.Content?.ToString() == "October 2026");
        Assert.NotNull(header);
        var buttons = FindVisuals<Button>(calendar);
        Assert.Equal(45, buttons.Count);
        Assert.Equal(42, buttons.Count(b => int.TryParse(b.Content?.ToString(), out _)));
    }

    [Fact]
    public void Real_pointer_click_selects_day_with_events()
    {
        var calendar = new Calendar { DisplayDate = Oct1 };
        var h = new Harness(calendar);
        var cell = FindVisuals<Button>(calendar).Single(b => b.Content?.ToString() == "15");
        var dateChanged = new List<SelectionChangedEventArgs>();
        var datesChanged = new List<SelectionChangedEventArgs>();
        calendar.SelectedDateChanged += (_, e) => dateChanged.Add(e);
        calendar.SelectedDatesChanged += (_, e) => datesChanged.Add(e);
        Press(h, cell);
        Assert.Equal(Oct15, calendar.SelectedDate);
        Assert.Equal([Oct15], calendar.SelectedDates.ToList());
        Assert.Single(dateChanged);
        Assert.Equal([Oct15], dateChanged[0].AddedItems.Cast<DateTime>().ToList());
        Assert.Single(datesChanged);
        Assert.True(calendar.TryGetDateRect(Oct15, out var rect));
        Assert.True(rect.Width > 0 && rect.Height > 0);
    }

    [Fact]
    public void Click_moves_selection_with_removed_and_added()
    {
        var calendar = new Calendar { DisplayDate = Oct1 };
        var h = new Harness(calendar);
        Press(h, FindVisuals<Button>(calendar).Single(b => b.Content?.ToString() == "15"));
        var added = new List<DateTime>();
        var removed = new List<DateTime>();
        calendar.SelectedDateChanged += (_, e) =>
        {
            added.AddRange(e.AddedItems.Cast<DateTime>());
            removed.AddRange(e.RemovedItems.Cast<DateTime>());
        };
        Press(h, FindVisuals<Button>(calendar).Single(b => b.Content?.ToString() == "20"));
        Assert.Equal(new DateTime(2026, 10, 20), calendar.SelectedDate);
        Assert.Equal([new DateTime(2026, 10, 20)], calendar.SelectedDates.ToList());
        Assert.Equal([Oct15], removed);
        Assert.Equal([new DateTime(2026, 10, 20)], added);
    }

    [Fact]
    public void Blackout_disables_cell_click_and_add()
    {
        var calendar = new Calendar { DisplayDate = Oct1 };
        calendar.BlackoutDates.Add(new CalendarDateRange(Oct15));
        var h = new Harness(calendar);
        var cell = FindVisuals<Button>(calendar).Single(b => b.Content?.ToString() == "15");
        Assert.False(cell.IsEnabled);
        Assert.False(calendar.IsSelectable(Oct15));
        Assert.True(calendar.IsSelectable(new DateTime(2026, 10, 16)));
        Press(h, cell);
        Assert.Null(calendar.SelectedDate);
        Assert.Throws<ArgumentOutOfRangeException>(() => calendar.SelectedDates.Add(Oct15));
        Assert.Throws<ArgumentOutOfRangeException>(() => calendar.SelectedDate = Oct15);
    }

    [Fact]
    public void Display_bounds_coerce_and_disable()
    {
        var calendar = new Calendar { DisplayDate = new DateTime(2026, 10, 20) };
        calendar.DisplayDateStart = new DateTime(2026, 10, 10);
        calendar.DisplayDateEnd = new DateTime(2026, 10, 25);
        CalendarDateChangedEventArgs? moved = null;
        calendar.DisplayDateChanged += (_, e) => moved = e;
        calendar.DisplayDate = new DateTime(2026, 10, 5);
        Assert.Equal(new DateTime(2026, 10, 10), calendar.DisplayDate);
        Assert.NotNull(moved);
        Assert.Equal(new DateTime(2026, 10, 20), moved!.RemovedDate);
        Assert.Equal(new DateTime(2026, 10, 10), moved.AddedDate);
        calendar.DisplayDate = new DateTime(2026, 10, 10);
        var h = new Harness(calendar);
        var early = FindVisuals<Button>(calendar).Single(b => b.Content?.ToString() == "9");
        Assert.False(early.IsEnabled);
        Assert.Throws<ArgumentOutOfRangeException>(() => calendar.DisplayDateStart = new DateTime(2026, 10, 26));
        Assert.Throws<ArgumentOutOfRangeException>(() => calendar.DisplayDateEnd = new DateTime(2026, 10, 9));
    }

    [Fact]
    public void Prev_next_buttons_move_month()
    {
        var calendar = new Calendar { DisplayDate = Oct1 };
        var h = new Harness(calendar);
        Button Nav(string content) =>
            FindVisuals<Button>(calendar).Single(b => b.Content?.ToString() == content);
        var moves = new List<CalendarDateChangedEventArgs>();
        calendar.DisplayDateChanged += (_, e) => moves.Add(e);
        Nav(">").RaiseClick();
        Assert.Equal(new DateTime(2026, 11, 1), calendar.DisplayDate);
        Nav("<").RaiseClick();
        Nav("<").RaiseClick();
        Assert.Equal(new DateTime(2026, 9, 1), calendar.DisplayDate);
        Assert.Equal(3, moves.Count);
    }

    [Fact]
    public void Keyboard_arrows_and_page_keys_move()
    {
        var calendar = new Calendar { DisplayDate = Oct15, SelectedDate = Oct15 };
        var h = new Harness(calendar);
        Assert.True(calendar.Focus());
        Key(h.Router, "ArrowRight");
        Assert.Equal(new DateTime(2026, 10, 16), calendar.SelectedDate);
        Key(h.Router, "ArrowDown");
        Assert.Equal(new DateTime(2026, 10, 23), calendar.SelectedDate);
        Key(h.Router, "PageDown");
        Assert.Equal(new DateTime(2026, 11, 15), calendar.DisplayDate);
        Key(h.Router, "PageUp");
        Assert.Equal(new DateTime(2026, 10, 15), calendar.DisplayDate);
    }

    [Fact]
    public void FirstDayOfWeek_Monday_reorders_headers()
    {
        var calendar = new Calendar { DisplayDate = Oct1, FirstDayOfWeek = DayOfWeek.Monday };
        var window = new Window { Content = calendar };
        window.Measure(new WpfSize(800, 600));
        window.Arrange(new WpfRect(0, 0, 800, 600));
        var labels = FindVisuals<TextBlock>(calendar)
            .Where(t => t.Text.Length == 3 && t.Text != "Oct")
            .Select(t => t.Text)
            .ToList();
        Assert.Equal(["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"], labels);
    }

    [Fact]
    public void Year_mode_pick_month_returns_to_month()
    {
        var calendar = new Calendar { DisplayDate = Oct1, DisplayMode = CalendarMode.Year };
        CalendarModeChangedEventArgs? mode = null;
        calendar.DisplayModeChanged += (_, e) => mode = e;
        var h = new Harness(calendar);
        var march = FindVisuals<Button>(calendar).Single(b => b.Content?.ToString() == "Mar");
        var header = FindVisuals<Button>(calendar).FirstOrDefault(b => b.Content?.ToString() == "2026");
        Assert.NotNull(header);
        march.RaiseClick();
        Assert.Equal(CalendarMode.Month, calendar.DisplayMode);
        Assert.Equal(new DateTime(2026, 3, 1), calendar.DisplayDate);
        Assert.NotNull(mode);
        Assert.Equal(CalendarMode.Year, mode!.OldMode);
        Assert.Equal(CalendarMode.Month, mode.NewMode);
    }

    [Fact]
    public void SingleRange_shift_arrow_extends_from_anchor()
    {
        var calendar = new Calendar { DisplayDate = Oct1, SelectionMode = CalendarSelectionMode.SingleRange };
        calendar.SelectedDate = new DateTime(2026, 10, 10);
        var h = new Harness(calendar);
        Assert.True(calendar.Focus());
        Key(h.Router, "ArrowRight", shift: true);
        Assert.Equal([new DateTime(2026, 10, 10), new DateTime(2026, 10, 11)], calendar.SelectedDates.ToList());
        Assert.Equal(new DateTime(2026, 10, 11), calendar.SelectedDate);
    }

    [Fact]
    public void SelectionMode_switch_clears_and_notifies()
    {
        var calendar = new Calendar { DisplayDate = Oct1, SelectedDate = Oct15 };
        var notified = 0;
        calendar.SelectionModeChanged += (_, _) => notified++;
        calendar.SelectionMode = CalendarSelectionMode.MultipleRange;
        Assert.Null(calendar.SelectedDate);
        Assert.Empty(calendar.SelectedDates);
        Assert.Equal(1, notified);
    }

    [Fact]
    public void Calendar_parses_from_xaml_and_rejects_precisely()
    {
        const string xaml = """
            <Calendar SelectionMode="MultipleRange" DisplayMode="Month" FirstDayOfWeek="Monday" IsTodayHighlighted="False" DisplayDate="2026-10-01" SelectedDate="2026-10-15" xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" />
            """;
        var root = XamlLoader.Load(xaml).Root as Calendar;
        Assert.NotNull(root);
        Assert.Equal(Oct15, root!.SelectedDate);
        Assert.Equal(Oct1, root.DisplayDate);
        Assert.Equal(CalendarSelectionMode.MultipleRange, root.SelectionMode);
        Assert.Equal(DayOfWeek.Monday, root.FirstDayOfWeek);
        Assert.False(root.IsTodayHighlighted);
        Assert.Equal([Oct15], root.SelectedDates.ToList());
        const string badMode = """
            <Calendar SelectionMode="Fortnight" xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" />
            """;
        Assert.Throws<XamlLoadException>(() => XamlLoader.Load(badMode));
        const string badDate = """
            <Calendar SelectedDate="not-a-date" xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" />
            """;
        Assert.Throws<XamlLoadException>(() => XamlLoader.Load(badDate));
    }
}
