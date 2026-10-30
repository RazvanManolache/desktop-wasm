using DesktopWasm.Core;
using DesktopWasm.Wpf;

namespace DesktopWasm.BrowserHost;

// Grid-rows lab (engine-native ScreenToGif DataGrid row/cell/sort slice):
// an employee table selects rows through the real pointer path (the
// KeyStrokes CurrentCell shape), a sort button reorders the rendered view
// through Items.SortDescriptions (the DataGridHelper.Sort shape), and the
// readouts mirror container lookups (row GetIndex, cell GetCellContent,
// details-presenter discovery) into the noter channel so the browser proof
// asserts post-input state, not just repaint hashes.
public sealed class GridRowsLab
{
    public static GridRowsLab? Current { get; private set; }

    public Window Window { get; } = new() { Title = "Grid" };

    public DataGrid Grid { get; } = new() { Height = 220 };

    public TextBlock Header { get; } = new()
    {
        Text = "Pick an employee:",
        Margin = new Thickness(8, 8, 8, 4),
    };

    public Button SortButton { get; } = new() { Content = "Sort: none", Width = 200, Margin = new Thickness(8, 4, 8, 4) };

    public TextBlock FirstReadout { get; } = new() { Text = "First: Ada, 36", Margin = new Thickness(8, 4, 8, 4) };

    public TextBlock RowReadout { get; } = new() { Text = "Row: -/3", Margin = new Thickness(8, 4, 8, 4) };

    public DataGridTextColumn NameColumn { get; } = new() { Header = "Name", BindingPath = "Name", Width = GridLength.Star() };

    public DataGridTextColumn RoleColumn { get; } = new() { Header = "Role", BindingPath = "Role" };

    public DataGridTextColumn AgeColumn { get; } = new() { Header = "Age", BindingPath = "Age", Width = GridLength.Pixel(60) };

    private int _sortStep;

    /// <summary>Automation channel mirroring each readout (the browser file replays it for state assertions).</summary>
    public Action<string, string>? Noter { get; set; }

    /// <summary>Latest status per key (the browser file replays these after attaching its noter).</summary>
    public IReadOnlyDictionary<string, string> Notes => _notes;

    private readonly Dictionary<string, string> _notes = new();
    private WasmWpfHost? _host;

    public sealed class Employee
    {
        public string Name { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public int Age { get; set; }
    }

    public static Window Build()
    {
        var lab = new GridRowsLab();
        Current = lab;
        lab.Compose();
        return lab.Window;
    }

    public void Compose()
    {
        var panel = new StackPanel { Margin = new Thickness(8) };
        panel.AddChild(Header);
        Grid.AddColumn(NameColumn);
        Grid.AddColumn(RoleColumn);
        Grid.AddColumn(AgeColumn);
        Grid.Items.Add(new Employee { Name = "Ada", Role = "Engineer", Age = 36 });
        Grid.Items.Add(new Employee { Name = "Grace", Role = "Admiral", Age = 85 });
        Grid.Items.Add(new Employee { Name = "Katherine", Role = "Mathematician", Age = 101 });
        Grid.SelectionChanged += () =>
        {
            var picked = Grid.SelectedItem as Employee;
            SetStatus("selected", Header, picked is null ? "Pick an employee:" : $"Picked: {picked.Name}, {picked.Age}");
            RefreshRowNotes();
        };
        Grid.SelectedIndex = 1;
        panel.AddChild(Grid);
        SortButton.Click += (_, _) => CycleSort();
        panel.AddChild(SortButton);
        panel.AddChild(FirstReadout);
        panel.AddChild(RowReadout);
        SetStatus("sort", SortButton, "Sort: none");
        RefreshRowNotes();
        Window.Content = panel;
    }

    private void CycleSort()
    {
        _sortStep = (_sortStep + 1) % 3;
        Grid.Items.SortDescriptions.Clear();
        AgeColumn.SortDirection = null;
        AgeColumn.SortMemberPath = string.Empty;
        var label = "none";
        if (_sortStep == 1)
        {
            Grid.Items.SortDescriptions.Add(new SortDescription("Age", ListSortDirection.Ascending));
            AgeColumn.SortDirection = ListSortDirection.Ascending;
            AgeColumn.SortMemberPath = "Age";
            label = "age up";
        }
        else if (_sortStep == 2)
        {
            Grid.Items.SortDescriptions.Add(new SortDescription("Age", ListSortDirection.Descending));
            AgeColumn.SortDirection = ListSortDirection.Descending;
            AgeColumn.SortMemberPath = "Age";
            label = "age down";
        }
        SetStatus("sort", SortButton, $"Sort: {label}");
        RefreshRowNotes();
    }

    private void RefreshRowNotes()
    {
        // Public-API mirror of KeyStrokes/DataGridHelper: first view row
        // through the generator, cell text through GetCellContent, index
        // through GetIndex, details-presenter discovery through the
        // visual tree.
        var firstRow = Grid.ItemContainerGenerator.ContainerFromIndex(0) as DataGridRow;
        var first = firstRow?.Item as Employee;
        var firstText = first is null ? "First: -" : $"First: {first.Name}, {first.Age}";
        var cellText = first is null ? "-" : (AgeColumn.GetCellContent(first) as TextBlock)?.Text ?? "-";
        SetStatus("first", FirstReadout, $"{firstText} · Cell: {cellText}");
        var index = firstRow?.GetIndex() ?? -1;
        var details = "no";
        if (firstRow is not null)
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(firstRow); i++)
                if (VisualTreeHelper.GetChild(firstRow, i) is DataGridDetailsPresenter) details = "yes";
        }
        SetStatus("row", RowReadout, $"Row: {index}/{Grid.Items.Count} · Details: {details}");
    }

    public void Wire(WasmWpfHost host)
    {
        _host = host;
        WasmMilCore.RenderHeadless(Window, new WpfSize(800, 600));
        PublishLayoutNotes();
    }

    /// <summary>Forwards every status so far (browser attach replays after Wire ran headless).</summary>
    public void PublishNotes()
    {
        foreach (var (key, value) in _notes)
            Note(key, value);
        PublishLayoutNotes();
    }

    internal void SetStatus(string key, TextBlock readout, string text)
    {
        readout.Text = text;
        _notes[key] = text;
        Note(key, text);
        _host?.Invalidate();
    }

    internal void SetStatus(string key, Button button, string text)
    {
        button.Content = text;
        _notes[key] = text;
        Note(key, text);
        _host?.Invalidate();
    }

    private void Note(string key, string value)
    {
        try { Noter?.Invoke(key, value); }
        catch { }
    }

    private void PublishLayoutNotes()
    {
        var rows = string.Join(";", Grid.RowRects.Select(r => $"{r.X:0},{r.Y:0},{r.Width:0},{r.Height:0}"));
        var rects = new Dictionary<string, string>
        {
            ["grid"] = Rect(Grid),
            ["sort"] = Rect(SortButton),
            ["rows"] = rows,
        };
        var json = "{" + string.Join(",", rects.Select(kv => $"\"{kv.Key}\":\"{kv.Value}\"")) + "}";
        _notes["layout"] = json;
        Note("layout", json);
    }

    private static string Rect(UIElement? element)
    {
        if (element is null) return "0,0,0,0";
        var r = element.RenderRect;
        return $"{r.X:0},{r.Y:0},{r.Width:0},{r.Height:0}";
    }
}
