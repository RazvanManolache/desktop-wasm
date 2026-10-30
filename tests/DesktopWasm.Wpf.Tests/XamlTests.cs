using System.Text;

namespace DesktopWasm.Wpf.Tests;

[ResetAmbientState]
public sealed class XamlTests
{
    private const string SmokeXaml = """
        <Window Title="Demo" xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
          <StackPanel>
            <Button x:Name="Ok" Content="OK" Background="#FF0000" Width="200"/>
            <TextBlock Text="Hello" Foreground="Black"/>
          </StackPanel>
        </Window>
        """;

    [Fact]
    public void Parses_tree_attributes_and_namescope()
    {
        var result = XamlLoader.Load(SmokeXaml);

        var window = Assert.IsType<Window>(result.Root);
        Assert.Equal("Demo", window.Title);
        var panel = Assert.IsType<StackPanel>(window.Content);
        Assert.Equal(2, panel.LogicalChildren.Count);

        var button = Assert.IsType<Button>(panel.LogicalChildren[0]);
        Assert.Equal("OK", button.Content);
        Assert.Equal(0xFF0000FFu, button.BackgroundRgba);
        Assert.Equal(200, button.Width);
        var text = Assert.IsType<TextBlock>(panel.LogicalChildren[1]);
        Assert.Equal("Hello", text.Text);
        Assert.Equal(0x000000FFu, text.ForegroundRgba);

        Assert.Same(button, Assert.IsType<Button>(result.Names["Ok"]));
    }

    [Fact]
    public void Parses_attached_properties_and_colors()
    {
        var result = XamlLoader.Load("""
            <Grid Rows="2" Cols="2">
              <Button Grid.Row="1" Grid.Column="1" Content=" Corner " Background="#800000FF"/>
              <Border Canvas.Left="5" Thickness="2" CornerRadius="4"/>
            </Grid>
            """);

        var grid = Assert.IsType<Grid>(result.Root);
        Assert.Equal(2, grid.Rows);
        var button = Assert.IsType<Button>(grid.LogicalChildren[0]);
        Assert.Equal(1, button.GetAttached("Grid.Row"));
        Assert.Equal(1, button.GetAttached("Grid.Column"));
        Assert.Equal(0x0000FF80u, button.BackgroundRgba); // #AARRGGBB -> RRGGBBAA: half-transparent blue
        var border = Assert.IsType<Border>(grid.LogicalChildren[1]);
        Assert.Equal(5.0, border.GetAttached("Canvas.Left"));
        Assert.Equal(2.0, border.Thickness);
        Assert.Equal(new CornerRadius(4), border.CornerRadius);
    }

    [Fact]
    public void Property_element_and_inner_text_content()
    {
        var button = Assert.IsType<Button>(XamlLoader.Load("<Button><Button.Content>Go</Button.Content></Button>").Root);
        Assert.Equal("Go", button.Content);

        var inline = Assert.IsType<Button>(XamlLoader.Load("<Button>Press</Button>").Root);
        Assert.Equal("Press", inline.Content);
    }

    [Fact]
    public void Null_markup_and_plain_clr_fallback_parse()
    {
        // Gallery CheckBoxPage shape: IsChecked="{x:Null}" lands null on the
        // nullable toggle instead of failing the page load.
        var tristate = Assert.IsType<CheckBox>(XamlLoader.Load(
            "<CheckBox IsChecked=\"{x:Null}\" IsThreeState=\"True\" />").Root);
        Assert.Null(tristate.IsChecked);

        // Gallery ComboBoxPage shape: Control.IsTabStop has no per-type table
        // entry; the generic CLR fallback covers it like WPF.
        var combo = Assert.IsType<ComboBox>(XamlLoader.Load("<ComboBox IsTabStop=\"False\" />").Root);
        Assert.False(combo.IsTabStop);

        // Gallery RadioButton/TreeView/ToolTip pages: retained attached props.
        var panel = Assert.IsType<StackPanel>(XamlLoader.Load(
            "<StackPanel KeyboardNavigation.TabNavigation=\"Once\" " +
            "KeyboardNavigation.DirectionalNavigation=\"Cycle\" " +
            "ScrollViewer.CanContentScroll=\"True\" " +
            "ToolTipService.InitialShowDelay=\"500\" />").Root);
        Assert.Equal(KeyboardNavigationMode.Once, (KeyboardNavigationMode)panel.GetAttached("KeyboardNavigation.TabNavigation")!);
        Assert.Equal(KeyboardNavigationMode.Cycle, (KeyboardNavigationMode)panel.GetAttached("KeyboardNavigation.DirectionalNavigation")!);
        Assert.Equal(true, panel.GetAttached("ScrollViewer.CanContentScroll"));
        Assert.Equal(500, panel.GetAttached("ToolTipService.InitialShowDelay"));
    }

    [Fact]
    public void Emphasis_inlines_fold_and_focus_wires_record()
    {
        // Gallery WhatsNew/TextBlock pages: Bold/Italic/Underline fold into
        // the run; GotKeyboardFocus wires record for code-behind binding.
        var result = XamlLoader.Load("""
            <TextBlock>See <Bold>NOTE</Bold> and <Italic>terms</Italic><Underline>here</Underline></TextBlock>
            """);
        var block = Assert.IsType<TextBlock>(result.Root);
        Assert.Contains("NOTE", block.Text);
        Assert.Contains("terms", block.Text);
        Assert.Contains("here", block.Text);
        Assert.Contains(block.Inlines, i => i is Bold);
        Assert.Contains(block.Inlines, i => i is Italic);
        Assert.Contains(block.Inlines, i => i is Underline);

        var wired = XamlLoader.Load("<RadioButton GotKeyboardFocus=\"OnFocus\" LostKeyboardFocus=\"OnBlur\" />");
        Assert.Equal(2, wired.EventWires.Count);
        Assert.Contains(wired.EventWires, w => w.EventName == "GotKeyboardFocus" && w.HandlerName == "OnFocus");

        var box = Assert.IsType<TextBox>(XamlLoader.Load("<TextBox AcceptsReturn=\"True\" />").Root);
        Assert.True(box.AcceptsReturn);

        var viewer = Assert.IsType<ScrollViewer>(XamlLoader.Load("<ScrollViewer IsTabStop=\"False\" />").Root);
        Assert.False(viewer.IsTabStop);
    }

    [Fact]
    public void Nested_static_resource_keys_resolve()
    {
        // Gallery WhatsNewPage shape: {DynamicResource {x:Static
        // SystemColors.AccentColorBrushKey}} normalizes the ResourceKey to
        // its name for the string-keyed lookup.
        var previous = Application.Current;
        Application.Current = new Application();
        try
        {
            Application.Current.Resources.Add("AccentColorBrushKey",
                new SolidColorBrush(Color.FromArgb(0xFF, 0x00, 0x78, 0xD4)));
            var border = Assert.IsType<Border>(XamlLoader.Load(
                "<Border Background=\"{DynamicResource {x:Static SystemColors.AccentColorBrushKey}}\" />").Root);
            Assert.Equal(0x0078D4FFu, Assert.IsType<SolidColorBrush>(border.Background).ToRgba());

            var direct = Assert.IsType<Border>(XamlLoader.Load(
                "<Border Background=\"{x:Static SystemColors.AccentBrush}\" />").Root);
            Assert.Equal(0x0078D4FFu, Assert.IsType<SolidColorBrush>(direct.Background).ToRgba());
        }
        finally
        {
            Application.Current = previous;
        }
    }

    [Fact]
    public void Expander_header_tolerates_trailing_whitespace()
    {
        // Gallery SettingsPage shape: the property reader must skip the
        // newline between </Grid> and </Expander.Header> instead of failing
        // the page with "one value only".
        var root = Assert.IsType<Expander>(XamlLoader.Load(
            "<Expander>\n  <Expander.Header>\n    <Grid>\n      <TextBlock Text=\"Hi\" />\n    </Grid>\n  </Expander.Header>\n  <Expander.Content>\n    <TextBlock Text=\"Body\" />\n  </Expander.Content>\n</Expander>").Root);
        var header = Assert.IsType<Grid>(root.Header);
        Assert.IsType<TextBlock>(Assert.Single(header.LogicalChildren));
        Assert.IsType<TextBlock>(root.Content);
    }

    [Fact]
    public void CornerRadius_accepts_space_separators()
    {
        // Gallery WhatsNewPage shape: CornerRadius="2 0 0 2".
        var uniform = Assert.IsType<Border>(XamlLoader.Load("<Border CornerRadius=\"8\" />").Root);
        Assert.Equal(new CornerRadius(8), uniform.CornerRadius);
        var split = Assert.IsType<Border>(XamlLoader.Load("<Border CornerRadius=\"2 0 0 2\" />").Root);
        Assert.Equal(new CornerRadius(2, 0, 0, 2), split.CornerRadius);
    }

    [Fact]
    public void Grid_shorthand_and_nested_spans_parse()
    {
        // Gallery WhatsNewPage shape: RowDefinitions/ColumnDefinitions
        // shorthand attributes (comma- and space-separated).
        var grid = Assert.IsType<Grid>(XamlLoader.Load(
            "<Grid RowDefinitions=\"Auto,Auto,Auto\" ColumnDefinitions=\"Auto Auto *\" />").Root);
        Assert.Equal(3, grid.RowDefinitions.Count);
        Assert.Equal(GridUnitType.Auto, grid.RowDefinitions[0].Height.Unit);
        Assert.Equal(3, grid.ColumnDefinitions.Count);
        Assert.Equal(GridUnitType.Star, grid.ColumnDefinitions[2].Width.Unit);

        // Gallery TextBlockPage shape: styled inlines nested in a Span fold.
        var block = Assert.IsType<TextBlock>(XamlLoader.Load(
            "<TextBlock><Span>Text can be <Bold>bold</Bold>, <Italic>italic</Italic>.</Span></TextBlock>").Root);
        Assert.Contains("bold", block.Text);
        Assert.Contains("italic", block.Text);

        // Gallery RadioButton/FileDialogs shapes: retained direction/scroll policies.
        var radio = Assert.IsType<RadioButton>(XamlLoader.Load("<RadioButton FlowDirection=\"RightToLeft\" />").Root);
        Assert.Equal(FlowDirection.RightToLeft, radio.FlowDirection);
        var text = Assert.IsType<TextBox>(XamlLoader.Load("<TextBox VerticalScrollBarVisibility=\"Auto\" />").Root);
        Assert.Equal(ScrollBarVisibility.Auto, text.VerticalScrollBarVisibility);
    }

    [Fact]
    public void Unknown_element_attribute_and_text_fail_with_line_info()
    {
        // Slider used to be the unknown-element probe; it is a real control now
        // (SliderTests), then InkCanvas (InkTests), then Decorator
        // (AdornerBindingOpsTests), then PasswordBox (EffectsHostsTests),
        // then MediaPlayer (MediaPowerTests), so the probe moves to
        // MediaElement, which stays unmapped.
        var ex = Assert.Throws<XamlLoadException>(() => XamlLoader.Load("<MediaElement/>"));
        Assert.Contains("Unknown element", ex.Message);

        var attr = Assert.Throws<XamlLoadException>(() => XamlLoader.Load("<Button Nope=\"1\"/>"));
        Assert.Contains("not valid", attr.Message);
        Assert.True(attr.Line > 0);

        var text = Assert.Throws<XamlLoadException>(() => XamlLoader.Load("<Grid>hello</Grid>"));
        Assert.Contains("inner text", text.Message);
    }

    [Fact]
    public void Horizontal_stackpanel_parses()
    {
        // Step 43: Orientation="Horizontal" stacks side by side.
        var panel = Assert.IsType<StackPanel>(XamlLoader.Load("<StackPanel Orientation=\"Horizontal\"/>").Root);
        Assert.Equal(Orientation.Horizontal, panel.Orientation);
        var ex = Assert.Throws<XamlLoadException>(() => XamlLoader.Load("<StackPanel Orientation=\"Diagonal\"/>"));
        Assert.Contains("Orientation", ex.Message);
    }

    [Fact]
    public void LoadComponent_requires_xml_and_matching_root_type()
    {
        using var good = new MemoryStream(Encoding.UTF8.GetBytes(SmokeXaml));
        var window = XamlLoader.LoadComponent<Window>(good);
        Assert.Equal("Demo", window.Title);

        using var baml = new MemoryStream(new byte[] { 0x4D, 0x53, 0x42, 0x41, 0x4D, 0x4C });
        var ex = Assert.Throws<XamlLoadException>(() => XamlLoader.LoadComponent<Window>(baml));
        Assert.Contains("BAML", ex.Message);

        using var wrong = new MemoryStream(Encoding.UTF8.GetBytes("<Button/>"));
        Assert.Throws<XamlLoadException>(() => XamlLoader.LoadComponent<Window>(wrong));
    }

    [Fact]
    public void LoadComponent_accepts_bom_loose_xaml()
    {
        var bytes = Encoding.UTF8.GetBytes(SmokeXaml);
        var bom = new byte[bytes.Length + 3];
        bom[0] = 0xEF; bom[1] = 0xBB; bom[2] = 0xBF;
        Buffer.BlockCopy(bytes, 0, bom, 3, bytes.Length);
        using var stream = new MemoryStream(bom);
        var window = XamlLoader.LoadComponent<Window>(stream);
        Assert.Equal("Demo", window.Title);
    }

    [Fact]
    public void Auto_extents_parse_to_NaN_like_WPF_LengthConverter()
    {
        // WPFGallery WhatsNewPage shape: <Grid Height="Auto"> crashed the
        // browser circuit ("'Auto' is not a number"). LengthConverter maps
        // Auto to double.NaN, the unset/auto sentinel layout already uses.
        var result = XamlLoader.Load("""
            <Grid Height="Auto" Width="Auto" MinWidth="Auto" MaxWidth="Auto" MinHeight="Auto" MaxHeight="Auto">
              <Button Content="x" Width="200"/>
            </Grid>
            """);
        var grid = Assert.IsType<Grid>(result.Root);
        Assert.True(double.IsNaN(grid.Height));
        Assert.True(double.IsNaN(grid.Width));
        Assert.True(double.IsNaN(grid.MinWidth));
        Assert.True(double.IsNaN(grid.MaxWidth));
        Assert.True(double.IsNaN(grid.MinHeight));
        Assert.True(double.IsNaN(grid.MaxHeight));

        var sized = Assert.IsType<Grid>(XamlLoader.Load("<Grid Width=\"120\" Height=\"40\"/>").Root);
        Assert.Equal(120, sized.Width);
        Assert.Equal(40, sized.Height);

        // Non-extent doubles stay strict: Auto is invalid on StrokeThickness.
        var ex = Assert.Throws<XamlLoadException>(() =>
            XamlLoader.Load("<Rectangle StrokeThickness=\"Auto\"/>"));
        Assert.Contains("Auto", ex.Message);
    }

    [Fact]
    public void Expander_parses_header_content_and_slots()
    {
        const string ns = "xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"";
        var result = XamlLoader.Load(
            "<Window " + ns + ">" +
            "<Expander Header=\"H\" IsExpanded=\"True\"><Expander.Content><TextBlock Text=\"C\" /></Expander.Content></Expander>" +
            "</Window>");
        var expander = Assert.IsType<Expander>(Assert.IsType<Window>(result.Root).Content);
        Assert.Equal("H", expander.Header);
        Assert.True(expander.IsExpanded);
        Assert.Equal("C", Assert.IsType<TextBlock>(expander.Content).Text);
        var slotted = XamlLoader.Load(
            "<Window " + ns + ">" +
            "<Expander><Expander.Header><TextBlock Text=\"T\" /></Expander.Header><StackPanel /></Expander>" +
            "</Window>");
        var slottedExpander = Assert.IsType<Expander>(Assert.IsType<Window>(slotted.Root).Content);
        Assert.Equal("T", Assert.IsType<TextBlock>(slottedExpander.Header).Text);
        Assert.IsType<StackPanel>(slottedExpander.Content);
    }
}
