using System.Globalization;
using DesktopWasm.Wpf;

namespace DesktopWasm.BrowserHost;

// These are deliberately labelled engine-native apps. They exercise the engine;
// they are not substitutes for the upstream binaries in the external catalog.
public static partial class DemoCatalog
{
    private static StackPanel AppPanel(string title, string subtitle)
    {
        var panel = new StackPanel { Margin = new Thickness(28) };
        panel.AddChild(new TextBlock { Text = title, FontCss = "28px sans-serif", ForegroundRgba = 0x214C40FF, Margin = new Thickness(0, 0, 0, 8) });
        panel.AddChild(new TextBlock { Text = subtitle, FontCss = "13px sans-serif", ForegroundRgba = 0x7B8D82FF, Margin = new Thickness(0, 0, 0, 22) });
        return panel;
    }

    private static Button AppButton(string label, Action action, double width = 150)
        => new() { Content = label, Width = width, Height = 38, Margin = new Thickness(0, 0, 8, 8),
            BackgroundRgba = 0xEDF4ECFF, BorderRgba = 0xC4D8C8FF, ForegroundRgba = 0x285B45FF,
            Command = new RelayCommand(action) };

    private static Window BuildPocketCalculator()
    {
        var panel = AppPanel("Pocket calculator", "Arithmetic, decimal input, clear, backspace and memory. Engine-native demo.");
        var display = new TextBlock { Text = "0", FontCss = "36px monospace", Margin = new Thickness(16), ForegroundRgba = 0x234A3CFF };
        var displayBorder = new Border { BackgroundRgba = 0xEDF4EBFF, Width = 360, Height = 76, CornerRadius = new CornerRadius(10), Margin = new Thickness(0, 0, 0, 10) };
        displayBorder.AddChild(display); panel.AddChild(displayBorder);
        var model = new CalculatorModel();
        var keypad = new Grid { Width = 368, Height = 280 };
        for (var i = 0; i < 4; i++) keypad.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star() });
        for (var i = 0; i < 6; i++) keypad.RowDefinitions.Add(new RowDefinition { Height = GridLength.Star() });
        var keys = new[] { "MC", "MR", "M+", "C", "7", "8", "9", "÷", "4", "5", "6", "×", "1", "2", "3", "−", "±", "0", ".", "+", "⌫", "%", "=", "AC" };
        for (var i = 0; i < keys.Length; i++)
        {
            var key = keys[i];
            var button = AppButton(key, () => display.Text = model.Press(key), 82);
            button.SetAttached("Grid.Row", i / 4); button.SetAttached("Grid.Column", i % 4); keypad.AddChild(button);
        }
        panel.AddChild(keypad);
        return new Window { Title = "Pocket calculator", Content = panel };
    }

    private static Window BuildProjectPlanner()
    {
        var panel = AppPanel("Project planner", "A session-local task list. Add work, select a task and mark it done.");
        var summary = new TextBlock { FontCss = "17px sans-serif", Margin = new Thickness(0, 0, 0, 15) };
        var tasks = new ListBox { Height = 200, Width = 550 };
        var input = new TextBox { Text = "Review the next WPF sample", Width = 550, Margin = new Thickness(0, 12, 0, 12) };
        void Refresh() => summary.Text = $"{tasks.Items.Count} tasks · {tasks.Items.Count(t => t.Content.StartsWith("✓ ", StringComparison.Ordinal))} completed";
        foreach (var title in new[] { "Try the converted HelloWorld app", "Exercise keyboard navigation", "Inspect the compatibility boundaries", "Capture a live screenshot" })
            tasks.AddChild(new ListBoxItem { Content = title });
        tasks.SelectedIndex = 0; Refresh();
        panel.AddChild(summary); panel.AddChild(tasks); panel.AddChild(input);
        var actions = new Canvas { Width = 560, Height = 48 };
        actions.AddChild(AppButton("Add task", () => { if (string.IsNullOrWhiteSpace(input.Text)) return; tasks.AddChild(new ListBoxItem { Content = input.Text.Trim() }); tasks.SelectedIndex = tasks.Items.Count - 1; input.Text = ""; Refresh(); }));
        actions.AddChild(AppButton("Toggle done", () => { if (tasks.SelectedItem is not { } task) return; task.Content = task.Content.StartsWith("✓ ", StringComparison.Ordinal) ? task.Content[2..] : "✓ " + task.Content; Refresh(); }));
        actions.AddChild(AppButton("Remove", () => { if (tasks.SelectedItem is { } task) tasks.RemoveChild(task); Refresh(); }));
        ArrangeButtonRow(actions, 158);
        panel.AddChild(actions);
        return new Window { Title = "Project planner", Content = panel };
    }

    private static Window BuildAssetExplorer()
    {
        var panel = AppPanel("Asset explorer", "Browse sample project data. No access to your disk is requested.");
        var selection = new TextBlock { Text = "Select a file to inspect its description.", FontCss = "16px sans-serif", Margin = new Thickness(0, 16, 0, 8) };
        var description = new TextBlock { Text = "Tree expansion, selection and retained text repaint.", ForegroundRgba = 0x718876FF };
        var tree = new TreeView { Width = 590, Height = 230 };
        var descriptions = new Dictionary<TreeViewItem, string>();
        foreach (var (folder, files) in new[] {
            ("Project", new[] { ("README.md", "Project overview and local build instructions."), ("App.xaml", "Application resources and startup window."), ("MainWindow.xaml", "Main window layout and bindings.") }),
            ("Assets", new[] { ("Colors.xaml", "Shared palette resource dictionary."), ("Icons.xaml", "Vector icon resources for the interface.") }),
            ("Tests", new[] { ("LayoutTests.cs", "Measure, arrange and clipping regression tests."), ("InputTests.cs", "Pointer, focus and keyboard behavior checks.") }) })
        {
            var root = new TreeViewItem { Header = folder, IsExpanded = true };
            foreach (var (name, detail) in files) { var item = new TreeViewItem { Header = name }; root.AddChild(item); descriptions[item] = detail; }
            tree.AddChild(root);
        }
        tree.SelectionChanged += () => {
            selection.Text = (tree.SelectedItem as TreeViewItem)?.Header?.ToString() ?? "Nothing selected";
            description.Text = tree.SelectedItem as TreeViewItem is { } item && descriptions.TryGetValue(item, out var detail) ? detail : "A folder in the sample project.";
        };
        panel.AddChild(tree); panel.AddChild(selection); panel.AddChild(description);
        return new Window { Title = "Asset explorer", Content = panel };
    }

    private static Window BuildColorLab()
    {
        var panel = AppPanel("Color lab", "Choose a palette color and preview it on a retained surface.");
        var swatch = new Border { Width = 560, Height = 165, CornerRadius = new CornerRadius(16), BackgroundRgba = 0x156E61FF, Margin = new Thickness(0, 0, 0, 16) };
        swatch.AddChild(new TextBlock { Text = "A fresh place for familiar interfaces", FontCss = "22px sans-serif", ForegroundRgba = 0xFFFFFFFF, Margin = new Thickness(22, 25, 10, 0) });
        var value = new TextBlock { Text = "Forest · #156E61", FontCss = "18px monospace", Margin = new Thickness(0, 0, 0, 20) };
        panel.AddChild(swatch); panel.AddChild(value);
        foreach (var row in new[] { new[] { ("Forest", "#156E61"), ("Ocean", "#3169A0"), ("Iris", "#7964A5") }, new[] { ("Clay", "#AE6750"), ("Slate", "#4C6373"), ("Olive", "#71824B") } })
        {
            var buttons = new Canvas { Width = 570, Height = 48 };
            foreach (var (name, color) in row)
                buttons.AddChild(AppButton(name, () => { swatch.BackgroundRgba = XamlLoader.ParseColor(color); value.Text = $"{name} · {color}"; }, 180));
            ArrangeButtonRow(buttons, 188);
            panel.AddChild(buttons);
        }
        return new Window { Title = "Color lab", Content = panel };
    }

    private static void ArrangeButtonRow(Canvas row, double stride)
    {
        var i = 0;
        foreach (var button in row.LogicalChildren) { Canvas.SetLeft(button, i++ * stride); Canvas.SetTop(button, 0); }
    }
}

/// <summary>Calculator behavior is independent of the scene and covered by interaction tests.</summary>
public sealed class CalculatorModel
{
    private string _text = "0";
    private decimal? _left;
    private decimal _memory;
    private string? _operator;
    private bool _replace = true;
    public string Press(string key)
    {
        if (_text == "Error") { _text = "0"; _replace = true; }
        if (key.Length == 1 && char.IsAsciiDigit(key[0])) { _text = _replace || _text == "0" ? key : _text.Length < 20 ? _text + key : _text; _replace = false; }
        else if (key == ".") { if (_replace) _text = "0"; if (!_text.Contains('.')) _text += "."; _replace = false; }
        else if (key is "C" or "AC") { _text = "0"; _left = null; _operator = null; _replace = true; if (key == "AC") _memory = 0; }
        else if (key == "⌫") { _text = _text.Length > 1 ? _text[..^1] : "0"; _replace = false; }
        else if (key == "MC") _memory = 0;
        else if (key == "MR") { _text = Format(_memory); _replace = true; }
        else if (decimal.TryParse(_text, NumberStyles.Number, CultureInfo.InvariantCulture, out var right))
        {
            try
            {
                switch (key)
                {
                    case "M+": _memory += right; _replace = true; break;
                    case "±": _text = Format(-right); break;
                    case "%": _text = Format(right / 100); break;
                    case "+" or "−" or "×" or "÷" or "=":
                        if (_left is { } left && _operator is not null && !_replace)
                        {
                            right = _operator switch { "+" => left + right, "−" => left - right, "×" => left * right, "÷" => left / right, _ => right };
                            _text = Format(right);
                        }
                        _left = key == "=" ? null : right; _operator = key == "=" ? null : key; _replace = true;
                        break;
                }
            }
            catch (ArithmeticException) { _text = "Error"; _left = null; _operator = null; _replace = true; }
        }
        return _text;
    }
    private static string Format(decimal value) => value.ToString("G", CultureInfo.InvariantCulture);
}
