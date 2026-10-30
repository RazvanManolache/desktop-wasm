using System.ComponentModel;
using DesktopWasm.Core;
using DesktopWasm.Graphics;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Gallery DatePicker slice (slice-38): the real converted WPFGallery
/// DateAndTime/DatePickerPage hosts a bare &lt;DatePicker/&gt;, which the XAML
/// loader rejected as an unknown element. Before the slice the engine had
/// no DatePicker type at all; the remaining gallery boundary after this slice
/// is Interaction.Triggers, FrameWindow Source and RichTextBox.
/// </summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class DatePickerTests
{
    private static readonly DateTime Oct1 = new(2026, 10, 1);
    private static readonly DateTime Oct15 = new(2026, 10, 15);
    private static readonly DateTime Oct20 = new(2026, 10, 20);

    private sealed class DateVm : INotifyPropertyChanged
    {
        private DateTime? _date;
        public DateTime? Date
        {
            get => _date;
            set
            {
                if (_date == value) return;
                _date = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Date)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

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

    private static void Key(WasmInputRouter router, string key, bool shift = false, bool ctrl = false) =>
        router.DispatchKeyboard(new WasmKeyArgs(new WasmWindowHandle(1), key, true, ctrl, shift, false));

    private static void Press(Harness h, UIElement element)
    {
        h.Rerender();
        var p = Center(element);
        Down(h.Router, p.X, p.Y);
        h.Rerender();
        p = Center(element);
        Up(h.Router, p.X, p.Y);
    }

    private static void Type(Harness h, string text)
    {
        foreach (var ch in text) Key(h.Router, ch.ToString());
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
    public void Picker_defaults_match_wpf()
    {
        var picker = new DatePicker();
        Assert.Null(picker.SelectedDate);
        Assert.Equal(DateTime.Today, picker.DisplayDate.Date);
        Assert.Equal(DayOfWeek.Sunday, picker.FirstDayOfWeek);
        Assert.True(picker.IsTodayHighlighted);
        Assert.False(picker.IsDropDownOpen);
        Assert.Equal(string.Empty, picker.Text);
        Assert.True(picker.Focusable);
        Assert.NotNull(FindVisual<TextBox>(picker));
        Assert.NotNull(FindVisual<Calendar>(picker));
    }

    [Fact]
    public void Code_set_updates_text_calendar_and_display()
    {
        var picker = new DatePicker();
        picker.SelectedDate = Oct15;
        Assert.Equal(Oct15, picker.SelectedDate);
        Assert.Equal(Oct15.ToString("d", System.Globalization.CultureInfo.CurrentCulture), picker.Text);
        Assert.Equal(Oct15, picker.DropDownCalendar.SelectedDate);
        Assert.Equal(Oct15, picker.DisplayDate);
        Assert.Equal(Oct15, picker.DropDownCalendar.DisplayDate);
    }

    [Fact]
    public void Null_clears_text_and_calendar_selection()
    {
        var picker = new DatePicker { SelectedDate = Oct15 };
        picker.SelectedDate = null;
        Assert.Null(picker.SelectedDate);
        Assert.Equal(string.Empty, picker.Text);
        Assert.Null(picker.DropDownCalendar.SelectedDate);
    }

    [Fact]
    public void Display_bounds_coerce_and_throw()
    {
        var picker = new DatePicker { DisplayDate = Oct20 };
        picker.DisplayDateStart = new DateTime(2026, 10, 10);
        picker.DisplayDateEnd = new DateTime(2026, 10, 25);
        picker.DisplayDate = new DateTime(2026, 10, 5);
        Assert.Equal(new DateTime(2026, 10, 10), picker.DisplayDate);
        Assert.Throws<ArgumentOutOfRangeException>(() => picker.SelectedDate = new DateTime(2026, 10, 9));
        Assert.Throws<ArgumentOutOfRangeException>(() => picker.DisplayDateStart = new DateTime(2026, 10, 26));
        Assert.Throws<ArgumentOutOfRangeException>(() => picker.DisplayDateEnd = new DateTime(2026, 10, 9));
    }

    [Fact]
    public void Real_pointer_toggle_opens_and_closes_with_events()
    {
        var picker = new DatePicker();
        var h = new Harness(picker);
        var opened = 0;
        var closed = 0;
        picker.CalendarOpened += (_, _) => opened++;
        picker.CalendarClosed += (_, _) => closed++;
        Press(h, picker.ToggleButton);
        Assert.True(picker.IsDropDownOpen);
        Assert.Equal(1, opened);
        Assert.Equal(0, closed);
        Press(h, picker.ToggleButton);
        Assert.False(picker.IsDropDownOpen);
        Assert.Equal(1, opened);
        Assert.Equal(1, closed);
    }

    [Fact]
    public void Typing_commits_on_enter_with_payloads()
    {
        var picker = new DatePicker { DisplayDate = Oct1 };
        var h = new Harness(picker);
        var changes = new List<SelectionChangedEventArgs>();
        picker.SelectedDateChanged += (_, e) => changes.Add(e);
        Press(h, picker.TextField);
        Assert.True(picker.TextField.IsFocused);
        Type(h, "10/15/2026");
        Assert.Null(picker.SelectedDate);
        Key(h.Router, "Enter");
        Assert.Equal(Oct15, picker.SelectedDate);
        Assert.Single(changes);
        Assert.Empty(changes[0].RemovedItems);
        Assert.Equal([Oct15], changes[0].AddedItems.Cast<DateTime>().ToList());
    }

    [Fact]
    public void Typing_second_date_reports_removed_and_added()
    {
        var picker = new DatePicker { DisplayDate = Oct1, SelectedDate = Oct15 };
        var h = new Harness(picker);
        var removed = new List<DateTime>();
        var added = new List<DateTime>();
        picker.SelectedDateChanged += (_, e) =>
        {
            removed.AddRange(e.RemovedItems.Cast<DateTime>());
            added.AddRange(e.AddedItems.Cast<DateTime>());
        };
        Press(h, picker.TextField);
        picker.TextField.SelectAll();
        Type(h, "10/20/2026");
        Key(h.Router, "Enter");
        Assert.Equal(Oct20, picker.SelectedDate);
        Assert.Equal([Oct15], removed);
        Assert.Equal([Oct20], added);
    }

    [Fact]
    public void Invalid_text_keeps_date_and_raises_validation_error()
    {
        var picker = new DatePicker { DisplayDate = Oct1, SelectedDate = Oct15 };
        var h = new Harness(picker);
        DatePickerDateValidationErrorEventArgs? error = null;
        picker.DateValidationError += (_, e) => error = e;
        var changes = 0;
        picker.SelectedDateChanged += (_, _) => changes++;
        Press(h, picker.TextField);
        picker.TextField.SelectAll();
        Type(h, "not-a-date");
        Key(h.Router, "Enter");
        Assert.Equal(Oct15, picker.SelectedDate);
        Assert.Equal(0, changes);
        Assert.NotNull(error);
        Assert.Equal("not-a-date", error!.Text);
        Assert.IsType<FormatException>(error.Exception);
        Assert.Equal("not-a-date", picker.Text);
    }

    [Fact]
    public void Empty_text_clears_selection()
    {
        var picker = new DatePicker { DisplayDate = Oct1, SelectedDate = Oct15 };
        var h = new Harness(picker);
        Press(h, picker.TextField);
        picker.TextField.SelectAll();
        Key(h.Router, "Backspace");
        Key(h.Router, "Enter");
        Assert.Null(picker.SelectedDate);
        Assert.Equal(string.Empty, picker.Text);
        Assert.Null(picker.DropDownCalendar.SelectedDate);
    }

    [Fact]
    public void Lost_focus_commits_text()
    {
        var picker = new DatePicker { DisplayDate = Oct1 };
        var other = new TextBox();
        var panel = new StackPanel();
        panel.AddChild(picker);
        panel.AddChild(other);
        var h = new Harness(panel);
        Press(h, picker.TextField);
        Type(h, "10/15/2026");
        Press(h, other);
        Assert.Equal(Oct15, picker.SelectedDate);
    }

    [Fact]
    public void Calendar_pick_sets_date_updates_text_and_closes()
    {
        // Hosted in a panel (like the staged gallery page), so the picker
        // sizes to content and the drop-down opens on-screen instead of
        // below a stretched full-window field.
        var picker = new DatePicker { DisplayDate = Oct1, Width = 220 };
        var panel = new StackPanel();
        panel.AddChild(picker);
        var h = new Harness(panel);
        var changes = new List<SelectionChangedEventArgs>();
        picker.SelectedDateChanged += (_, e) => changes.Add(e);
        picker.IsDropDownOpen = true;
        h.Rerender();
        var cell = FindVisuals<Button>(picker.DropDownCalendar).Single(b => b.Content?.ToString() == "15");
        Press(h, cell);
        Assert.Equal(Oct15, picker.SelectedDate);
        Assert.Equal(Oct15.ToString("d", System.Globalization.CultureInfo.CurrentCulture), picker.Text);
        Assert.False(picker.IsDropDownOpen);
        Assert.Single(changes);
        Assert.Equal([Oct15], changes[0].AddedItems.Cast<DateTime>().ToList());
    }

    [Fact]
    public void Selection_outside_visible_month_moves_calendar_display()
    {
        var picker = new DatePicker { DisplayDate = Oct1, Width = 220 };
        var panel = new StackPanel();
        panel.AddChild(picker);
        var h = new Harness(panel);
        picker.SelectedDate = new DateTime(2026, 11, 20);
        h.Rerender();
        Assert.Equal(new DateTime(2026, 11, 20), picker.DropDownCalendar.DisplayDate);
        Assert.True(picker.DropDownCalendar.TryGetDateRect(new DateTime(2026, 11, 20), out var rect));
        Assert.True(rect.Width > 0 && rect.Height > 0);
    }

    [Fact]
    public void SelectedDate_binds_two_way_by_default()
    {
        var picker = new DatePicker();
        var vm = new DateVm { Date = Oct15 };
        picker.SetBinding("SelectedDate", new Binding("Date") { Source = vm });
        Assert.Equal(Oct15, picker.SelectedDate);
        vm.Date = Oct20;
        Assert.Equal(Oct20, picker.SelectedDate);
        picker.SelectedDate = Oct1;
        Assert.Equal(Oct1, vm.Date);
    }

    [Fact]
    public void Picker_parses_from_xaml_and_rejects_precisely()
    {
        const string xaml = """
            <DatePicker SelectedDate="2026-10-15" DisplayDate="2026-10-01" FirstDayOfWeek="Monday" IsTodayHighlighted="False" IsDropDownOpen="False" Text="10/15/2026" xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" />
            """;
        var root = XamlLoader.Load(xaml).Root as DatePicker;
        Assert.NotNull(root);
        Assert.Equal(Oct15, root!.SelectedDate);
        Assert.Equal(Oct1, root.DisplayDate);
        Assert.Equal(DayOfWeek.Monday, root.FirstDayOfWeek);
        Assert.False(root.IsTodayHighlighted);
        const string badDate = """
            <DatePicker SelectedDate="not-a-date" xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" />
            """;
        Assert.Throws<XamlLoadException>(() => XamlLoader.Load(badDate));
        const string badDay = """
            <DatePicker FirstDayOfWeek="Funday" xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" />
            """;
        Assert.Throws<XamlLoadException>(() => XamlLoader.Load(badDay));
        // SelectedDateFormat parses (slice-73): Long renders the long
        // pattern instead of rejecting.
        const string longFormat = """
            <DatePicker SelectedDateFormat="Long" xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" />
            """;
        var formatted = XamlLoader.Load(longFormat).Root as DatePicker;
        Assert.NotNull(formatted);
        Assert.Equal(DatePickerFormat.Long, formatted!.SelectedDateFormat);
        const string badFormat = """
            <DatePicker SelectedDateFormat="Eternal" xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" />
            """;
        var formatError = Assert.Throws<XamlLoadException>(() => XamlLoader.Load(badFormat));
        Assert.Contains("SelectedDateFormat", formatError.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Picker_renders_text_and_toggle_chevron()
    {
        var picker = new DatePicker { SelectedDate = Oct15 };
        var window = new Window { Content = picker };
        var commands = WasmMilCore.RenderHeadless(window, new WpfSize(800, 600));
        var json = WasmMilCore.ExportFrameJson(commands);
        Assert.Contains(Oct15.Day.ToString(System.Globalization.CultureInfo.InvariantCulture), json, StringComparison.Ordinal);
    }
}
