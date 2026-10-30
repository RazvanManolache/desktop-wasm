using DesktopWasm.Wpf;

namespace DesktopWasm.BrowserHost;

/// <summary>One engine-native demo scene (cf. the winforms-wasm demo catalogs).</summary>
public sealed record WpfDemo(string Slug, string Label, string Description, string Group, string Icon, Func<Window> Build, Action<WasmWpfHost>? OnHostReady = null);

/// <summary>
/// Demo menu mirroring the WindowsForms host library: markup loading, controls,
/// layout, typography, input, and interop — each building a fresh Window so
/// selection never leaks state.
/// </summary>
public static partial class DemoCatalog
{
    public static IReadOnlyList<WpfDemo> All { get; } =
    [
        new("pocket-calculator", "Pocket calculator", "A working keypad with arithmetic and memory", "Mini apps", "▤", BuildPocketCalculator),
        new("project-planner", "Project planner", "Add tasks, mark work done, and track progress", "Mini apps", "✓", BuildProjectPlanner),
        new("asset-explorer", "Asset explorer", "Browse a sample project tree and inspect its files", "Mini apps", "◫", BuildAssetExplorer),
        new("color-lab", "Color lab", "Explore palettes and preview theme colors", "Mini apps", "◉", BuildColorLab),
        new("hello-xaml", "Hello XAML", "Markup loading to a visual tree", "Fundamentals", "◈", BuildHelloXaml),
        new("controls", "Controls showcase", "Buttons, text, borders", "Fundamentals", "▣", BuildControls),
        new("typography", "Text & typography", "Font sizes and ellipsis", "Fundamentals", "¶", BuildTypography),
        new("layout", "Layout gallery", "Grid cells, StackPanel, Canvas", "Layout", "▦", BuildLayout),
        new("input", "Input playground", "Click routing with a live counter", "Interaction", "⌁", BuildInput),
        new("interop", "Interop hosts", "Immediate content in a retained tree", "Interaction", "◫", BuildInterop),
        new("calc-keypad", "Calculator keypad", "Real-XAML grid: pixel rows, spans", "Layout", "▤", BuildKeypad),
        new("binding", "Data binding", "View-model behind XAML text", "Data", "⛁", BuildBinding),
        new("validation-links", "Validation & links", "Rule-checked input plus in-session link navigation", "Data", "⇄", BuildValidationLinks),
        new("scroll", "ScrollViewer", "Wheel, drag and keyboard scrolling", "Interaction", "↕", BuildScroll),
        new("slider", "Slider lab", "Range drag, snap, keyboard and live readout", "Interaction", "↔", BuildSlider),
        new("size-ellipse", "Size & ellipse", "Resize-driven tiles with avatar ellipses", "Layout", "◍", BuildSizeEllipse),
        new("menu", "Menus", "Dropdown menus with commands", "Interaction", "≣", BuildMenu),
        new("dialogs", "Dialogs & docking", "Docked layout with a modal dialog", "Interaction", "▢", BuildDialogs, OnHostReady: WireDialogs),
        new("files-clipboard", "Files & clipboard", "Pick files into session mounts; copy and paste with denial states", "Interaction", "⧉", BuildFilesClipboard, OnHostReady: WireFilesClipboard),
        new("a11y-events", "Announcements & prefs", "Screen-reader announcements plus theme-preference refresh", "Interaction", "◉", BuildA11yEvents),
        new("chrome-pack", "Chrome & pack", "Custom caption chrome plus pack-stream content", "Interaction", "◨", BuildChromePack),
        new("ink-doc", "Ink & document lab", "Pen-stroke capture plus FlowDocument release notes", "Interaction", "✎", BuildInkDoc),
        new("drag-drop", "Drag & drop", "File-drop targets, paste veto and session source", "Interaction", "⤓", BuildDragDrop, OnHostReady: WireDragDrop),
        new("primitives", "Primitives lab", "Thumb drag, hold-to-repeat and popup placement", "Interaction", "◈", BuildPrimitives, OnHostReady: WirePrimitives),
        new("tabs", "Tabs lab", "Tab selection, progress value and hover tooltips", "Interaction", "▤", BuildTabs, OnHostReady: WireTabs),
        new("hittest", "Hit-test lab", "Point picks, rubber-band selection and live theme resources", "Interaction", "◎", BuildHitTest, OnHostReady: WireHitTest),
        new("templates", "Templates lab", "Control/Data template inflation with bindings and triggers", "Interaction", "T", BuildTemplates, OnHostReady: WireTemplates),
        new("animation", "Animation lab", "Show/hide clocks: height, opacity, visibility and margin", "Interaction", "◉", BuildAnimation, OnHostReady: WireAnimation),
        new("text", "Text lab", "Typeface shaping, decorations and session DPI", "Interaction", "T", BuildText, OnHostReady: WireText),
        new("shell", "Shell lab", "Size-to-content, taskbar progress, grid splitting, screen placement and timer ticks", "Interaction", "◫", BuildShell, OnHostReady: WireShell),
        new("effects", "Effects lab", "Drop shadows, password masking and hosted pages", "Interaction", "◍", BuildEffects, OnHostReady: WireEffects),
        new("listbox", "List selection", "Click or arrow through selectable rows", "Data", "☰", BuildListBox),
        new("treeview", "Tree hierarchy", "Expand, collapse and pick nodes", "Data", "↳", BuildTreeView),
        new("datagrid", "Data table", "Row selection, cell lookup and Age sorting", "Data", "⊞", BuildGridRows, OnHostReady: WireGridRows),
        new("resources", "Resources", "Dictionaries, pack URIs, StaticResource", "Data", "◉", BuildResources),
        new("hello-real", "HelloWorld (real app)", "Converted WPF-Samples binary, booted via shim", "Real apps", "✦", BuildHelloWorldReal),
        new("wpf-calculator", "Calculator (real app)", "Converted CalculatorDemo binary, booted via shim", "Real apps", "▦", BuildCalculatorReal),
    ];

    public static IReadOnlyList<string> Groups { get; } =
        ["Mini apps", "Fundamentals", "Layout", "Interaction", "Data", "Real apps"];

    public static WpfDemo Find(string? slug) =>
        All.FirstOrDefault(d => string.Equals(d.Slug, slug, StringComparison.OrdinalIgnoreCase)) ?? All[0];

    private static Window BuildHelloXaml()
    {
        using var stream = new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(DemoContent.Xaml));
        return XamlLoader.LoadComponent<Window>(stream);
    }

    /// <summary>
    /// Real converted application: the WPF-Samples HelloWorld binary,
    /// rewritten by DesktopWasm.Convert (original untouched, see the staged
    /// receipt), booted through the Application.LoadComponent shim.
    /// </summary>
    private static Window BuildHelloWorldReal()
    {
        throw new InvalidOperationException("HelloWorld requires on-demand app download; use the browser runtime loader.");
    }

    private static Window BuildCalculatorReal()
    {
        throw new InvalidOperationException("CalculatorDemo requires on-demand app download; use the browser runtime loader.");
    }

    public static Window BootConverted(byte[] image, string receipt)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(receipt);
        var app = doc.RootElement.TryGetProperty("App", out var name) ? name.GetString() : "Converted app";
        if (!doc.RootElement.TryGetProperty("MapVersion", out var map) || map.GetString() != WpfAppBoot.MapVersion)
            throw new InvalidOperationException(
                $"Converted {app} was built for a different map (want {WpfAppBoot.MapVersion}): reconvert it.");
        return WpfAppBoot.Boot(image);
    }

    private static Window BuildKeypad()
    {
        using var stream = new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(DemoContent.KeypadXaml));
        return XamlLoader.LoadComponent<Window>(stream);
    }

    private static Window BuildBinding()
    {
        var result = XamlLoader.Load(DemoContent.BindingXaml);
        var window = (Window)result.Root;
        var vm = new PartyViewModel();
        window.DataContext = vm;
        return window;
    }

    /// <summary>
    /// Gallery TextBoxPage/HyperlinkPage slice as an engine-native lab: an
    /// alphabetic rule (gallery helper shape) blocks bad source pushes and
    /// paints the error border, while a hyperlink RequestNavigate swaps the
    /// session Frame instead of launching externally.
    /// </summary>
    private sealed class DemoPerson : System.ComponentModel.INotifyPropertyChanged
    {
        private string _name = string.Empty;
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        public string Name
        {
            get => _name;
            set { if (_name == value) return; _name = value; PropertyChanged?.Invoke(this, new(nameof(Name))); }
        }
    }

    private sealed class DemoAlphaRule : ValidationRule
    {
        public override ValidationResult Validate(object? value, System.Globalization.CultureInfo culture)
        {
            var input = value as string;
            if (string.IsNullOrEmpty(input)) return ValidationResult.ValidResult;
            return System.Text.RegularExpressions.Regex.IsMatch(input, "^[a-zA-Z]+$")
                ? ValidationResult.ValidResult
                : new ValidationResult(false, "Only English alphabetic characters (a-z, A-Z) are allowed.");
        }
    }

    private static Window BuildValidationLinks()
    {
        var result = XamlLoader.Load("""
            <Window Title="Validation and links lab" xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <StackPanel>
                <TextBlock Margin="8,8,8,4">Type a name (letters only): the rule blocks bad input and paints the error border.</TextBlock>
                <TextBox x:Name="NameEditor" Width="320" Margin="8,4,8,4" />
                <TextBlock x:Name="NameReadout" Margin="8,4,8,4" />
                <TextBlock x:Name="ErrorReadout" Margin="8,4,8,12" />
                <TextBlock Margin="8,4,8,4"><Hyperlink x:Name="DetailsLink" NavigateUri="demo:details" TargetName="details">Open the details page</Hyperlink></TextBlock>
                <Frame x:Name="Viewer" Margin="8,4,8,4" />
                <TextBlock x:Name="NavReadout" Margin="8,4,8,4" />
                <Button x:Name="BackHome" Width="150" Margin="8,4,8,4">Back home</Button>
              </StackPanel>
            </Window>
            """);
        var window = (Window)result.Root;
        var editor = (TextBox)result.Names["NameEditor"];
        var nameReadout = (TextBlock)result.Names["NameReadout"];
        var errorReadout = (TextBlock)result.Names["ErrorReadout"];
        var link = (Hyperlink)result.Names["DetailsLink"];
        var viewer = (Frame)result.Names["Viewer"];
        var navReadout = (TextBlock)result.Names["NavReadout"];
        var back = (Button)result.Names["BackHome"];

        var person = new DemoPerson { Name = "Ada" };
        var binding = new Binding("Name")
        {
            Source = person,
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
            NotifyOnValidationError = true,
        };
        binding.ValidationRules.Add(new DemoAlphaRule());
        editor.SetBinding("Text", binding);

        void RefreshName() => nameReadout.Text = Validation.GetHasError(editor)
            ? $"Name: invalid ({Validation.GetErrors(editor)[0].ErrorContent})"
            : $"Name: \"{person.Name}\" (valid)";
        editor.TextChanged += (_, _) => RefreshName();
        Validation.AddErrorHandler(editor, (_, e) =>
        {
            var ve = (ValidationErrorEventArgs)e;
            errorReadout.Text = $"Error event: {ve.Action} ({ve.Error.ErrorContent})";
        });
        RefreshName();

        var home = new Page { Title = "Home" };
        home.Content = new TextBlock { Text = "Home page: follow the link above to navigate in-session." };
        var details = new Page { Title = "Details" };
        details.Content = new TextBlock { Text = "Details page: RequestNavigate swapped this Frame, no external launch." };
        // Gallery MainWindow pattern: the host learns about navigation only
        // through Frame.Navigated (RootContentFrame_Navigated refreshes
        // back-navigation state); nothing here tracks navigation privately.
        viewer.Navigated += (_, e) =>
        {
            navReadout.Text = $"Viewing: {(e.Content as Page)?.Title ?? "?"}";
        };
        viewer.Navigate(home);
        link.RequestNavigate += (_, e) =>
        {
            viewer.Navigate(details);
            e.Handled = true;
        };
        back.Click += (_, _) => viewer.Navigate(home);
        return window;
    }

    /// <summary>Demo view-model: INotifyPropertyChanged pushes through bindings.</summary>
    private sealed class PartyViewModel : System.ComponentModel.INotifyPropertyChanged
    {
        private string _name = "Ada";
        private int _age = 36;

        public PartyViewModel()
        {
            // Parties end at 40: the Birthday button greys out by itself.
            BirthdayCommand = new RelayCommand(_ => Age++, _ => Age < 40);
            PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(Age)) BirthdayCommand.RaiseCanExecuteChanged();
            };
        }

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

        public string Name
        {
            get => _name;
            set { if (_name == value) return; _name = value; PropertyChanged?.Invoke(this, new(nameof(Name))); }
        }

        public int Age
        {
            get => _age;
            set { if (_age == value) return; _age = value; PropertyChanged?.Invoke(this, new(nameof(Age))); }
        }

        public RelayCommand BirthdayCommand { get; }
    }

    private static Window BuildControls()
    {
        var window = new Window { Title = "Controls" };
        var panel = new StackPanel { Margin = new Thickness(8) };
        panel.AddChild(new TextBlock { Text = "Pick an action", FontCss = "18px sans-serif", Margin = new Thickness(8, 8, 8, 4) });
        panel.AddChild(new Button { Content = "OK", BackgroundRgba = 0xDDFFDDFF, Width = 200, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 4) });
        panel.AddChild(new Button { Content = "Cancel", Width = 200, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 4) });
        var card = new Border { Thickness = 2, CornerRadius = new CornerRadius(8), BackgroundRgba = 0xEEEEEEFF };
        card.AddChild(new TextBlock { Text = "Bordered note", ForegroundRgba = 0x333333FF });
        panel.AddChild(card);
        window.Content = panel;
        return window;
    }

    private static Window BuildLayout()
    {
        var window = new Window { Title = "Layout" };
        var grid = new Grid { Rows = 2, Cols = 2 };
        var palette = new[] { "Red", "Green", "Blue", "Yellow" };
        for (var i = 0; i < 4; i++)
        {
            var cell = new Button { Content = palette[i], BackgroundRgba = XamlLoader.ParseColor(palette[i]) };
            cell.SetAttached("Grid.Row", i / 2);
            cell.SetAttached("Grid.Column", i % 2);
            grid.AddChild(cell);
        }
        window.Content = grid;
        return window;
    }

    private static Window BuildTypography()
    {
        var window = new Window { Title = "Typography" };
        var panel = new StackPanel { Margin = new Thickness(8) };
        panel.AddChild(new TextBlock { Text = "Large heading", FontCss = "32px sans-serif" });
        panel.AddChild(new TextBlock { Text = "Body copy at default size." });
        panel.AddChild(new TextBlock { Text = "Small caption", FontCss = "11px sans-serif", ForegroundRgba = 0x666666FF });
        var clipped = new FormattedText("This very long line gets an ellipsis.", System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface(new FontFamily("sans-serif"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
            14, Brushes.Black)
        { MaxTextWidth = 140 };
        panel.AddChild(new TextBlock { Text = clipped.DisplayText });
        window.Content = panel;
        return window;
    }

    private static Window BuildInput()
    {
        var window = new Window { Title = "Input" };
        var panel = new StackPanel { Margin = new Thickness(8) };
        var counter = new TextBlock { Text = "Clicks: 0", FontCss = "18px sans-serif", Margin = new Thickness(8, 8, 8, 4) };
        var button = new Button { Content = "Click me", Width = 200, HorizontalAlignment = HorizontalAlignment.Center };
        var clicks = 0;
        button.Click += (_, _) => counter.Text = $"Clicks: {++clicks}";
        panel.AddChild(counter);
        panel.AddChild(button);
        window.Content = panel;
        return window;
    }

    private static Window BuildScroll()
    {
        var window = new Window { Title = "Scroll" };
        var panel = new StackPanel { Margin = new Thickness(8) };
        var header = new TextBlock { Text = "Wheel, thumb-drag, or focus + arrows scroll 20 rows.", Margin = new Thickness(8, 8, 8, 4) };
        panel.AddChild(header);
        // Explicit height: inside a StackPanel an auto-height viewer would size
        // to its full extent (like real WPF) and never need to scroll.
        var view = new ScrollViewer { Height = 220 };
        var readout = new TextBlock { Text = "Offset: 0", Margin = new Thickness(8, 4, 8, 4) };
        view.ScrollChanged += (_, e) => readout.Text = $"Offset: {e.VerticalOffset:0} (Δ {e.VerticalChange:0})";
        var rows = new StackPanel();
        for (var i = 0; i < 20; i++)
        {
            var n = i;
            var row = new Button { Content = $"Row {n}" };
            row.Click += (_, _) => header.Text = $"Selected: Row {n}";
            rows.AddChild(row);
        }
        view.Content = rows;
        panel.AddChild(view);
        panel.AddChild(readout);
        window.Content = panel;
        return window;
    }

    private static Window BuildSlider()
    {
        var window = new Window { Title = "Slider lab" };
        var panel = new StackPanel { Margin = new Thickness(8) };
        var header = new TextBlock { Text = "Drag a thumb, or focus + arrows; readout follows the value.", Margin = new Thickness(8, 8, 8, 4) };
        panel.AddChild(header);
        var readout = new TextBlock { Text = "Volume: 25", Margin = new Thickness(8, 4, 8, 4) };
        var volume = new Slider
        {
            Minimum = 0, Maximum = 100, Value = 25, Width = 320, Height = 34,
            TickFrequency = 10, TickPlacement = TickPlacement.Both, IsSnapToTickEnabled = true,
            Margin = new Thickness(8, 4, 8, 4),
        };
        volume.ValueChanged += (_, e) => readout.Text = $"Volume: {e.NewValue}";
        panel.AddChild(volume);
        var balanceReadout = new TextBlock { Text = "Balance: 0", Margin = new Thickness(8, 4, 8, 4) };
        var balance = new Slider
        {
            Minimum = -50, Maximum = 50, Value = 0, Width = 320, Height = 34,
            SmallChange = 5, LargeChange = 25, Margin = new Thickness(8, 4, 8, 4),
        };
        balance.ValueChanged += (_, e) => balanceReadout.Text = $"Balance: {e.NewValue}";
        panel.AddChild(balance);
        panel.AddChild(balanceReadout);
        panel.AddChild(readout);
        window.Content = panel;
        return window;
    }

    private static Window BuildSizeEllipse()
    {
        var window = new Window { Title = "Size & ellipse lab" };
        var panel = new StackPanel { Margin = new Thickness(8) };
        panel.AddChild(new TextBlock { Text = "Grow the tiles: the readout follows SizeChanged; avatars are true ellipses.", Margin = new Thickness(8, 8, 8, 4) });
        var readout = new TextBlock { Text = "Tiles: 220x120", Margin = new Thickness(8, 4, 8, 4) };
        panel.AddChild(readout);
        var tiles = new Border { Width = 220, Height = 120, Margin = new Thickness(8, 4, 8, 4) };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var fill in new[] { Brushes.Blue, Brushes.Red, Brushes.Yellow })
            row.AddChild(new Ellipse { Width = 64, Height = 64, Fill = fill, Stroke = Brushes.White, StrokeThickness = 4, Margin = new Thickness(8) });
        tiles.AddChild(row);
        tiles.SizeChanged += (_, e) => readout.Text = $"Tiles: {e.NewSize.Width}x{e.NewSize.Height}";
        panel.AddChild(tiles);
        var grow = new Button { Content = "Grow tiles", Margin = new Thickness(8, 4, 8, 4) };
        var grown = false;
        grow.Click += (_, _) =>
        {
            grown = !grown;
            tiles.Width = grown ? 340 : 220;
            tiles.Height = grown ? 160 : 120;
        };
        panel.AddChild(grow);
        window.Content = panel;
        return window;
    }

    /// <summary>
    /// ScreenToGif ink/document slice as an engine-native lab: an InkCanvas
    /// collects pointer drags into real strokes (readout follows
    /// StrokeCollected) while a FlowDocumentScrollViewer hosts release-note
    /// paragraphs with a working link.
    /// </summary>
    private static Window BuildInkDoc()
    {
        var window = new Window { Title = "Ink & document lab" };
        var panel = new StackPanel { Margin = new Thickness(8) };
        panel.AddChild(new TextBlock { Text = "Drag on the pad: strokes collect below; the notes are a live FlowDocument.", Margin = new Thickness(8, 8, 8, 4) });
        var readout = new TextBlock { Text = "Strokes: 0", Margin = new Thickness(8, 4, 8, 4) };
        panel.AddChild(readout);
        var pad = new InkCanvas { Width = 400, Height = 200, Background = Brushes.White };
        pad.StrokeCollected += (_, _) => readout.Text = $"Strokes: {pad.Strokes.Count}";
        var frame = new Border { Background = Brushes.White, BorderRgba = 0x808080FF, Thickness = 1, Margin = new Thickness(8, 4, 8, 4) };
        frame.AddChild(pad);
        panel.AddChild(frame);
        var viewer = new FlowDocumentScrollViewer { Margin = new Thickness(8, 4, 8, 4) };
        var notes = new FlowDocument { FontSize = 13 };
        notes.Blocks.Add(new Paragraph(new Run("Release notes")) { FontSize = 20 });
        var detail = new Paragraph();
        detail.Inlines.Add(new Run("Ink and documents now convert. "));
        var link = new Hyperlink(new Run("details")) { NavigateUri = new Uri("https://example.com/notes") };
        link.RequestNavigate += (_, _) => readout.Text = "Link: details opened";
        detail.Inlines.Add(link);
        notes.Blocks.Add(detail);
        viewer.Document = notes;
        panel.AddChild(viewer);
        window.Content = panel;
        return window;
    }

    private static Window BuildListBox()
    {
        var window = new Window { Title = "List" };
        var panel = new StackPanel { Margin = new Thickness(8) };
        var header = new TextBlock { Text = "Pick a fruit:", Margin = new Thickness(8, 8, 8, 4) };
        panel.AddChild(header);
        // Explicit height smaller than the rows: v1 has no internal scroller,
        // so the list clips (nest in a ScrollViewer to scroll).
        var list = new ListBox { Height = 180 };
        foreach (var fruit in new[] { "Apple", "Banana", "Cherry", "Date", "Fig", "Grape" })
            list.AddChild(new ListBoxItem { Content = fruit });
        list.SelectionChanged += () =>
            header.Text = list.SelectedItem is { } picked ? $"Picked: {picked.Content}" : "Pick a fruit:";
        list.SelectedIndex = 2;
        panel.AddChild(list);
        window.Content = panel;
        return window;
    }

    private static Window BuildTreeView()
    {
        var window = new Window { Title = "Tree" };
        var panel = new StackPanel { Margin = new Thickness(8) };
        var header = new TextBlock { Text = "Pick a node:", Margin = new Thickness(8, 8, 8, 4) };
        panel.AddChild(header);
        // Explicit height smaller than the rows: v1 has no internal scroller,
        // so the tree clips (nest in a ScrollViewer to scroll).
        var tree = new TreeView { Height = 220 };
        var docs = new TreeViewItem { Header = "docs", IsExpanded = true };
        docs.AddChild(new TreeViewItem { Header = "tutorial.md" });
        docs.AddChild(new TreeViewItem { Header = "faq.md" });
        var src = new TreeViewItem { Header = "src" };
        src.AddChild(new TreeViewItem { Header = "engine.cs" });
        tree.AddChild(docs);
        tree.AddChild(src);
        tree.AddChild(new TreeViewItem { Header = "readme.md" });
        tree.SelectionChanged += () =>
            header.Text = tree.SelectedItem is TreeViewItem picked ? $"Picked: {picked.Header}" : "Pick a node:";
        tree.SelectedItem = docs.Items[1];
        panel.AddChild(tree);
        window.Content = panel;
        return window;
    }

    private static Window BuildMenu()
    {
        var window = new Window { Title = "Menus" };
        var panel = new StackPanel { Margin = new Thickness(8) };
        var status = new TextBlock { Text = "Pick a menu entry…", Margin = new Thickness(8, 8, 8, 4) };
        void Say(string s) => status.Text = s;

        var menu = new Menu();
        var file = new MenuItem { Header = "File" };
        var fresh = new MenuItem { Header = "New" };
        fresh.Click += (_, _) => Say("New document");
        var open = new MenuItem { Header = "Open…" };
        open.Click += (_, _) => Say("Open chosen");
        var recent = new MenuItem { Header = "Recent" };
        var doc1 = new MenuItem { Header = "Quarterly report" };
        doc1.Click += (_, _) => Say("Opened Quarterly report");
        var doc2 = new MenuItem { Header = "Holiday photos" };
        doc2.Click += (_, _) => Say("Opened Holiday photos");
        recent.AddChild(doc1);
        recent.AddChild(doc2);
        // Greyed by command gate (CanExecute false), like a real Save with
        // nothing to save yet; the click never fires.
        var save = new MenuItem
        {
            Header = "Save",
            Command = new RelayCommand(_ => Say("Saved"), _ => false),
        };
        file.AddChild(fresh);
        file.AddChild(open);
        file.AddChild(recent);
        file.AddChild(save);

        var edit = new MenuItem { Header = "Edit" };
        var undo = new MenuItem { Header = "Undo" };
        undo.Click += (_, _) => Say("Undone");
        var redo = new MenuItem { Header = "Redo", IsEnabled = false };
        redo.Click += (_, _) => Say("Redone");
        edit.AddChild(undo);
        edit.AddChild(redo);

        var help = new MenuItem { Header = "Help" };
        var about = new MenuItem { Header = "About" };
        about.Click += (_, _) => Say("wpf-wasm menu demo (Step 24)");
        help.AddChild(about);

        menu.AddChild(file);
        menu.AddChild(edit);
        menu.AddChild(help);
        panel.AddChild(menu);
        panel.AddChild(status);
        panel.AddChild(new TextBlock
        {
            Text = "Click a header to drop down; hover swaps menus, arrows move, Enter invokes, Esc closes.",
            Margin = new Thickness(8, 4, 8, 8),
        });
        window.Content = panel;
        return window;
    }

    private static Window BuildFilesClipboard() => FilesClipboardLab.Build();

    private static Window BuildDragDrop() => DragDropLab.Build();

    /// <summary>
    /// ScreenToGif primitives slice as an engine-native lab: a Thumb grip
    /// reports started/delta/completed (the crop-adorner drag shape), a
    /// RepeatButton holds to repeat (the spinner shape), and a toggle opens
    /// a Bottom-anchored Popup with light dismiss (the split-button shape).
    /// </summary>
    private static Window BuildPrimitives() => PrimitivesLab.Build();

    private static void WirePrimitives(WasmWpfHost host) => PrimitivesLab.Current?.Wire(host);

    /// <summary>
    /// ScreenToGif tab/progress/tooltip slice as an engine-native lab: a
    /// three-page TabControl swaps header selection with content, a bump
    /// button drives a ProgressBar value, and a hovered button opens its
    /// ToolTipService hint. Pure engine surface; the browser proof asserts
    /// the mirrored readout text after real pointer input.
    /// </summary>
    private static Window BuildTabs() => TabsLab.Build();

    private static void WireTabs(WasmWpfHost host) => TabsLab.Current?.Wire(host);

    /// <summary>
    /// DynamicResource + hit-test slice as an engine-native lab: three
    /// framed shapes pick through VisualTreeHelper.HitTest, drags
    /// rubber-band through a GeometryHitTestParameters callback walk, and a
    /// button swaps the live AccentBrush resource behind the frames.
    /// </summary>
    private static Window BuildHitTest() => HitTestLab.Build();

    private static void WireHitTest(WasmWpfHost host) => HitTestLab.Current?.Wire(host);

    /// <summary>
    /// Template cluster slice as an engine-native lab: a gallery PageHeader-shape
    /// control inflates Title/Description bindings with a TargetName trigger, a
    /// ScreenToGif StatusControl-shape control resolves its UniformGrid part
    /// through Template.FindName, and a Button paints BorderlessButton-shape
    /// chrome through a BasedOn style chain. Pure engine surface; the browser
    /// proof asserts mirrored readout text and layout rects after real input.
    /// </summary>
    private static Window BuildTemplates() => TemplatesLab.Build();

    private static void WireTemplates(WasmWpfHost host) => TemplatesLab.Current?.Wire(host);

    /// <summary>
    /// ScreenToGif animation-clock slice as an engine-native lab: Show/Hide
    /// run the HideableTabControl shapes (height/opacity DoubleAnimation with
    /// PowerEase, visibility ObjectAnimationUsingKeyFrames, margin
    /// ThicknessAnimation) with live readouts. Pure engine surface; the
    /// browser proof asserts interpolated post-click values.
    /// </summary>
    private static Window BuildAnimation() => AnimationLab.Build();

    private static void WireAnimation(WasmWpfHost host) => AnimationLab.Current?.Wire(host);

    /// <summary>
    /// ScreenToGif text/font/DPI slice as an engine-native lab: a decorated
    /// caption cycles Underline/Strikethrough/None through TextDecorations,
    /// a FormattedText outline renders as retained geometry (TextPath shape),
    /// and the session DPI scale reads out with a host-sync zoom. Pure
    /// engine surface; the browser proof asserts readout text plus
    /// decoration-band pixels after real pointer input.
    /// </summary>
    private static Window BuildText() => TextLab.Build();

    private static void WireText(WasmWpfHost host) => TextLab.Current?.Wire(host);

    /// <summary>
    /// ScreenToGif shell/windowing slice as an engine-native lab: the window
    /// sizes to content, the taskbar slot carries progress plus thumbnail
    /// buttons sharing commands with the visible buttons, and a GridSplitter
    /// redistributes the content columns. Pure engine surface; the browser
    /// proof asserts readout text after real pointer input.
    /// </summary>
    private static Window BuildShell() => ShellLab.Build();

    private static void WireShell(WasmWpfHost host) => ShellLab.Current?.Wire(host);

    /// <summary>
    /// ScreenToGif effects/host cluster as an engine-native lab: a shadowed
    /// box with toggle/blur controls, a masked PasswordBox with live masked
    /// readouts, a WebBrowser journal, and a ContainerVisual replay check.
    /// Pure engine surface; the browser proof asserts readout text after
    /// real pointer/keyboard input.
    /// </summary>
    private static Window BuildEffects() => EffectsLab.Build();

    private static void WireEffects(WasmWpfHost host) => EffectsLab.Current?.Wire(host);

    /// <summary>
    /// ScreenToGif DataGrid row/cell/sort slice as an engine-native lab: an
    /// employee table selects rows through the real pointer path, a sort
    /// button reorders the rendered view through Items.SortDescriptions,
    /// and readouts mirror container lookups (row GetIndex, cell
    /// GetCellContent, details-presenter discovery) for post-input state
    /// assertions. Pure engine surface.
    /// </summary>
    private static Window BuildGridRows() => GridRowsLab.Build();

    private static void WireGridRows(WasmWpfHost host) => GridRowsLab.Current?.Wire(host);

    /// <summary>
    /// Gallery automation slice as an engine-native lab: copy buttons
    /// announce through automation peers (the ColorTile pattern) and theme
    /// changes refresh through SystemEvents (the MainWindow/HeaderTile
    /// pattern). Pure engine surface; the browser mirrors announcements
    /// into a DOM aria-live region.
    /// </summary>
    private static Window BuildA11yEvents() => A11yEventsLab.Build();

    private static void WireFilesClipboard(WasmWpfHost host) => FilesClipboardLab.Current?.Wire(host);

    /// <summary>
    /// ScreenToGif drag-drop/data-object + HwndSource slice as an
    /// engine-native lab: the zone runs the editor's enter/drop decisions,
    /// the paste box runs the numeric-box veto, and DPI reads the session
    /// source. Pure engine surface; the browser HTML5 drop bridge calls the
    /// same AcceptFiles core through <see cref="DragDropLab.BrowserDrop"/>.
    /// </summary>
    private static void WireDragDrop(WasmWpfHost host) => DragDropLab.Current?.Wire(host);

    /// <summary>
    /// Gallery MainWindow slice as an engine-native lab: a custom WindowChrome
    /// (caption band, glass sheet, resize edges) where only marked controls
    /// stay clickable in the caption, plus page text loaded through
    /// Application.GetResourceStream (the ControlExample.LoadResource shape).
    /// </summary>
    private static Window BuildChromePack()
    {
        // Mirrors application startup: the pack entry is registered before
        // the window loads so GetResourceStream resolves (idempotent across
        // navigation, like the resources demo theme pack).
        var app = Application.Current ?? new Application();
        Application.Current = app;
        const string notesUri = "pack://application:,,,/ChromePack/Notes.txt";
        app.RegisterPackResource(notesUri,
            "ControlExample reads page XAML through Application.GetResourceStream.");
        var window = new Window { Title = "Chrome and pack lab" };
        WindowChrome.SetWindowChrome(window, new WindowChrome
        {
            CaptionHeight = 50,
            CornerRadius = new CornerRadius(12),
            GlassFrameThickness = new Thickness(-1),
            ResizeBorderThickness = new Thickness(4),
            UseAeroCaptionButtons = true,
            NonClientFrameEdges = NonClientFrameEdges.Left | NonClientFrameEdges.Right | NonClientFrameEdges.Bottom,
        });
        var panel = new StackPanel();
        var ping = new Button { Content = "Ping", Height = 28 };
        WindowChrome.SetIsHitTestVisibleInChrome(ping, true);
        var pings = new TextBlock { Text = "Pings: 0" };
        var count = 0;
        ping.Click += (_, _) => pings.Text = $"Pings: {++count}";
        var title = new TextBlock { Text = "Custom chrome caption (engine-painted band)" };
        var pack = new TextBlock { Text = LoadPackText(notesUri) };
        var client = new TextBlock { Text = "Client area below the caption." };
        var poke = new Button { Content = "Client OK" };
        var pokes = new TextBlock { Text = "Pokes: 0" };
        var poked = 0;
        poke.Click += (_, _) => pokes.Text = $"Pokes: {++poked}";
        panel.AddChild(ping);
        panel.AddChild(title);
        panel.AddChild(pack);
        panel.AddChild(client);
        panel.AddChild(poke);
        panel.AddChild(pings);
        panel.AddChild(pokes);
        window.Content = panel;
        return window;
    }

    private static string LoadPackText(string uri)
    {
        // Gallery ControlExample.LoadResource shape: absent resources read
        // as a precise placeholder instead of throwing at load.
        if (Application.GetResourceStream(new Uri(uri)) is not { } info || info.Stream is null)
            return "pack resource missing";
        using var reader = new StreamReader(info.Stream, System.Text.Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <summary>Live scene for the dialogs demo (rebuilt per navigation; the host hook wires it).</summary>
    private static DialogScene? _dialogScene;

    private static Window BuildDialogs()
    {
        var scene = new DialogScene();
        _dialogScene = scene;

        var dock = new DockPanel();
        var toolbar = new Border { Height = 48, BackgroundRgba = 0xE8E8E8FF };
        DockPanel.SetDock(toolbar, Dock.Top);
        toolbar.AddChild(scene.OpenButton);
        var statusBar = new Border { Height = 32, BackgroundRgba = 0xE8E8E8FF };
        DockPanel.SetDock(statusBar, Dock.Bottom);
        statusBar.AddChild(scene.Status);
        scene.Instructions.Margin = new Thickness(8);
        dock.AddChild(toolbar);
        dock.AddChild(statusBar);
        dock.AddChild(scene.Instructions);

        scene.Main.Content = dock;

        var dim = new Border { Width = 800, Height = 600, BackgroundRgba = 0x00000080 };
        // No explicit Height: Canvas arranges at content size, so the panel hugs the message + buttons.
        var panel = new Border { Width = 320, BackgroundRgba = 0xFFFFFFFF, Thickness = 1 };
        Canvas.SetLeft(panel, 240);
        Canvas.SetTop(panel, 190);
        var stack = new StackPanel { Margin = new Thickness(12) };
        stack.AddChild(scene.Message);
        stack.AddChild(scene.OkButton);
        stack.AddChild(scene.CancelButton);
        panel.AddChild(stack);
        var overlay = new Canvas();
        overlay.AddChild(dim);
        overlay.AddChild(panel);
        scene.Dialog.Content = overlay;
        return scene.Main;
    }

    private static void WireDialogs(WasmWpfHost host)
    {
        var scene = _dialogScene;
        if (scene is null) return;
        var size = new WpfSize(800, 600);
        scene.OpenButton.Click += (_, _) => host.ShowDialog(scene.Dialog, size);
        scene.OkButton.Click += (_, _) => { host.CloseDialog(scene.Dialog, true); scene.Status.Text = "Dialog result: OK"; };
        scene.CancelButton.Click += (_, _) => { host.CloseDialog(scene.Dialog, false); scene.Status.Text = "Dialog result: Cancel"; };
    }

    private sealed class DialogScene
    {
        public Window Main { get; } = new() { Title = "Dialogs" };
        public Window Dialog { get; } = new() { Title = "Confirm" };
        public Button OpenButton { get; } = new() { Content = "Open dialog…" };
        public TextBlock Status { get; } = new() { Text = "Dialog result: —" };
        public TextBlock Instructions { get; } = new()
        {
            Text = "Toolbar docked top, status bottom, this fills the rest. " +
                "The button opens a modal dialog that blocks the main window.",
        };
        public TextBlock Message { get; } = new() { Text = "Delete the quarterly report?" };
        public Button OkButton { get; } = new() { Content = "OK" };
        public Button CancelButton { get; } = new() { Content = "Cancel" };
    }

    private static Window BuildResources()
    {
        // Mirrors application startup: the theme pack is registered before
        // the window loads so Source= resolves (idempotent across navigation).
        var app = Application.Current ?? new Application();
        Application.Current = app;
        app.RegisterPackResource("Theme.xaml", DemoContent.ThemeXaml);
        return (Window)XamlLoader.Load(DemoContent.ResourcesXaml).Root;
    }

    private static Window BuildInterop()
    {
        var window = new Window { Title = "Interop" };
        var panel = new StackPanel { Margin = new Thickness(8) };
        panel.AddChild(new TextBlock { Text = "Immediate content inside a retained tree:", Margin = new Thickness(8, 8, 8, 4) });
        panel.AddChild(new WindowsFormsHost { Child = new BarChartPaintable() });
        window.Content = panel;
        return window;
    }

    /// <summary>Immediate-mode bar chart: stands in for a winforms-wasm control.</summary>
    private sealed class BarChartPaintable : IImmediatePaintable
    {
        private static readonly uint[] Palette =
            [0xE74C3CFF, 0xF39C12FF, 0x27AE60FF, 0x2980B9FF, 0x8E44ADFF];

        public WpfSize Measure(WpfSize available) => new(
            Math.Min(available.Width, 300), Math.Min(available.Height, 120));

        public void Paint(DrawingContext dc, WpfRect bounds)
        {
            var values = new[] { 0.4, 0.7, 0.55, 0.9, 0.65 };
            var bw = bounds.Width / values.Length;
            for (var i = 0; i < values.Length; i++)
            {
                var h = bounds.Height * values[i];
                dc.DrawRectangle(
                    (float)(bounds.X + i * bw + 2), (float)(bounds.Y + bounds.Height - h),
                    (float)(bw - 4), (float)h, Palette[i % Palette.Length]);
            }
        }
    }
}
