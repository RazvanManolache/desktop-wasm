namespace DesktopWasm.Wpf.Tests;

/// <summary>App-level resource dictionary tests (Geometry slice): grid
/// DrawingBrush entries, gradient entries, brush-valued StaticResource
/// assignment, ControlTemplate retention (visual root, namescope, deferred
/// template bindings, triggers), live EventTrigger attach, text inlines,
/// 2D geometry parsing and the deferred VisualBrush visual binding.</summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class GeometryAppResourceTests
{
    private const string Ns =
        "xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
        "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"";

    private static UIElement LoadRoot(string body)
    {
        var loaded = XamlLoader.Load($"<Window {Ns}>{body}</Window>");
        return loaded.Root;
    }

    private static T Res<T>(ResourceDictionary dictionary, object key)
    {
        Assert.True(dictionary.TryGetValue(key, out var value), $"Resource '{key}' was not found.");
        return Assert.IsType<T>(value);
    }

    [Fact]
    public void Geometry_brush_resource_parses_grid_pattern()
    {
        var previous = Application.Current;
        _ = new Application();
        try
        {
            var root = (Window)LoadRoot(
                "<Window.Resources>" +
                "<DrawingBrush x:Key=\"Grid\" Viewport=\"0,0,10,10\" ViewportUnits=\"Absolute\" TileMode=\"Tile\">" +
                "<DrawingBrush.Drawing><DrawingGroup><DrawingGroup.Children>" +
                "<GeometryDrawing Brush=\"White\"><GeometryDrawing.Geometry>" +
                "<RectangleGeometry Rect=\"0,0,1,1\" /></GeometryDrawing.Geometry></GeometryDrawing>" +
                "<GeometryDrawing Brush=\"Black\" Geometry=\"M0,0 L1,0 1,0.1, 0,0.1Z\" />" +
                "</DrawingGroup.Children></DrawingGroup></DrawingBrush.Drawing></DrawingBrush>" +
                "</Window.Resources>");
            var brush = Res<DrawingBrush>(root.Resources, "Grid");
            Assert.Equal(new Rect(0, 0, 10, 10), brush.Viewport);
            Assert.Equal(BrushMappingMode.Absolute, brush.ViewportUnits);
            Assert.Equal(TileMode.Tile, brush.TileMode);
            var group = Assert.IsType<DrawingGroup>(brush.Drawing);
            Assert.Equal(2, group.Children.Count);
            var base_ = Assert.IsType<GeometryDrawing>(group.Children[0]);
            Assert.Equal(new Rect(0, 0, 1, 1), Assert.IsType<RectangleGeometry>(base_.Geometry).Rect);
            Assert.Equal(0xFFFFFFFF, ((SolidColorBrush)base_.Brush!).ToRgba());
            // The tiled brush paints its full-bleed base fill.
            Assert.Equal(0xFFFFFFFF, BrushRender.ResolveFill(brush));
        }
        finally
        {
            Application.Current = previous;
        }
    }

    [Fact]
    public void Geometry_gradient_entry_resolves_static_stops()
    {
        var previous = Application.Current;
        _ = new Application();
        try
        {
            var root = (Window)LoadRoot(
                "<Window.Resources>" +
                "<GradientStopCollection x:Key=\"Stops\">" +
                "<GradientStop Color=\"White\" Offset=\"0.0\" />" +
                "<GradientStop Color=\"Black\" Offset=\"1.0\" />" +
                "</GradientStopCollection>" +
                "<LinearGradientBrush x:Key=\"Glass\" StartPoint=\"0,0\" EndPoint=\"1,1\" Opacity=\"0.75\" " +
                "GradientStops=\"{StaticResource Stops}\" />" +
                "</Window.Resources>");
            var stops = Res<GradientStopCollection>(root.Resources, "Stops");
            Assert.Equal(2, stops.Count);
            var brush = Res<LinearGradientBrush>(root.Resources, "Glass");
            Assert.Equal(new Point(0, 0), brush.StartPoint);
            Assert.Equal(new Point(1, 1), brush.EndPoint);
            Assert.Equal(0.75, brush.Opacity);
            Assert.Same(stops, brush.GradientStops);
            Assert.Equal(0xFFFFFFFF, BrushRender.ResolveFill(brush));
        }
        finally
        {
            Application.Current = previous;
        }
    }

    [Fact]
    public void Geometry_template_setter_retains_visual_tree_triggers_and_bindings()
    {
        var previous = Application.Current;
        _ = new Application();
        try
        {
            var root = (Window)LoadRoot(
                "<Window.Resources>" +
                "<Style TargetType=\"{x:Type RadioButton}\"><Setter Property=\"Background\" Value=\"White\" />" +
                "<Setter Property=\"Template\"><Setter.Value>" +
                "<ControlTemplate TargetType=\"{x:Type RadioButton}\">" +
                "<Grid><Rectangle x:Name=\"glass\" Fill=\"Transparent\" RadiusX=\"4\" " +
                "Opacity=\"{TemplateBinding Opacity}\" /><ContentPresenter Content=\"{TemplateBinding Content}\" />" +
                "</Grid><ControlTemplate.Triggers>" +
                "<Trigger Property=\"IsFocused\" Value=\"true\">" +
                "<Setter Property=\"Rectangle.Opacity\" Value=\"1\" TargetName=\"glass\" /></Trigger>" +
                "<EventTrigger RoutedEvent=\"RadioButton.Checked\"><BeginStoryboard>" +
                "<Storyboard><DoubleAnimation Storyboard.TargetName=\"glass\" " +
                "Storyboard.TargetProperty=\"Opacity\" From=\"0\" To=\"1\" Duration=\"0:0:1\" /></Storyboard>" +
                "</BeginStoryboard></EventTrigger>" +
                "</ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>" +
                "</Window.Resources><RadioButton Content=\"Pick\" />");
            var style = Res<Style>(root.Resources, typeof(RadioButton));
            var template = Assert.IsType<ControlTemplate>(Assert.IsType<Setter>(style.Setters[1]).Value);
            var grid = Assert.IsType<Grid>(template.VisualTree);
            Assert.Equal(2, grid.LogicalChildren.Count);
            Assert.True(template.TemplateNames.ContainsKey("glass"));
            Assert.Contains(template.TemplateBindings, b => b.Property == "Opacity");
            Assert.Contains(template.TemplateBindings, b => b.Property == "Content");
            Assert.Equal(2, template.Triggers.Count);
            var property = Assert.IsType<Trigger>(template.Triggers[0]);
            Assert.Equal("IsFocused", property.Property);
            var single = Assert.IsType<Setter>(Assert.Single(property.Setters));
            Assert.Equal("glass", single.TargetName);
            Assert.Equal("RadioButton.Checked", Assert.IsType<EventTrigger>(template.Triggers[1]).RoutedEvent);
            // The implicit style applies live: background plus the retained template.
            var radio = Assert.IsType<RadioButton>(((Window)root).Content);
            Assert.Equal(0xFFFFFFFF, ((SolidColorBrush)radio.Background!).ToRgba());
            Assert.Same(template, radio.Template);
        }
        finally
        {
            Application.Current = previous;
        }
    }

    [Fact]
    public void Template_grid_definitions_and_converter_bindings_parse()
    {
        // WPFGallery ControlExample shape: definition rows plus a
        // TemplateBinding with a converter in the visual root.
        var previous = Application.Current;
        var app = new Application();
        app.Resources.Add("FoundBrush", new SolidColorBrush(Colors.Blue));
        try
        {
            var root = (Window)LoadRoot(
                "<Window.Resources>" +
                "<Style TargetType=\"{x:Type Button}\"><Setter Property=\"Template\"><Setter.Value>" +
                "<ControlTemplate TargetType=\"{x:Type Button}\">" +
                "<Grid><Grid.RowDefinitions><RowDefinition Height=\"Auto\" /><RowDefinition Height=\"*\" MinHeight=\"30\" /></Grid.RowDefinitions>" +
                "<TextBlock Foreground=\"{DynamicResource FoundBrush}\" " +
                "Visibility=\"{TemplateBinding Content, Converter={StaticResource NullToVisibilityConverter}}\" />" +
                "<TextBlock Foreground=\"{DynamicResource MissingBrush}\" />" +
                "</Grid></ControlTemplate></Setter.Value></Setter></Style>" +
                "</Window.Resources><Button Content=\"Go\" />");
            var template = Assert.IsType<ControlTemplate>(
                Assert.IsType<Setter>(Res<Style>(root.Resources, typeof(Button)).Setters[0]).Value);
            var grid = Assert.IsType<Grid>(template.VisualTree);
            Assert.Equal([GridLength.Auto, GridLength.Star()], grid.RowDefinitions.Select(d => d.Height));
            Assert.Equal(30.0, grid.RowDefinitions[1].MinHeight);
            Assert.Equal(0x0000FFFFu, ((SolidColorBrush)Assert.IsType<TextBlock>(grid.LogicalChildren[0]).Foreground!).ToRgba());
            Assert.Null(Assert.IsType<TextBlock>(grid.LogicalChildren[1]).Foreground);
            Assert.Contains(template.TemplateBindings, b => b.Property == "Content");
            Assert.Contains(template.TemplateBindings, b => b.Property == "Converter:NullToVisibilityConverter");
        }
        finally
        {
            Application.Current = previous;
        }
    }

    [Fact]
    public void Template_expander_content_and_explicit_bindings_parse()
    {
        // WPFGallery ControlExample shape: direct StackPanel content in a
        // template Expander plus an explicit binding with an attached path.
        var previous = Application.Current;
        _ = new Application();
        try
        {
            var root = (Window)LoadRoot(
                "<Window.Resources>" +
                "<Style TargetType=\"{x:Type Button}\"><Setter Property=\"Template\"><Setter.Value>" +
                "<ControlTemplate TargetType=\"{x:Type Button}\">" +
                "<Expander Header=\"Source code\" IsExpanded=\"True\" AutomationProperties.Name=\"{Binding Path=(AutomationProperties.Name)}\">" +
                "<StackPanel><TextBlock Text=\"Hi\" /><Button Command=\"ApplicationCommands.Copy\" Content=\"Go\" /></StackPanel>" +
                "</Expander></ControlTemplate></Setter.Value></Setter></Style>" +
                "</Window.Resources><Button Content=\"Go\" />");
            var template = Assert.IsType<ControlTemplate>(
                Assert.IsType<Setter>(Res<Style>(root.Resources, typeof(Button)).Setters[0]).Value);
            var expander = Assert.IsType<Expander>(template.VisualTree);
            Assert.Equal("Source code", expander.Header);
            var host = Assert.IsType<StackPanel>(Assert.Single(expander.LogicalChildren));
            var content = Assert.IsType<StackPanel>(host.LogicalChildren[1]);
            Assert.Same(ApplicationCommands.Copy, Assert.IsType<Button>(content.LogicalChildren[1]).Command);
            Assert.Contains(template.TemplateBindings, b => b.Property == "(AutomationProperties.Name)");
        }
        finally
        {
            Application.Current = previous;
        }
    }

    [Fact]
    public void Template_button_content_and_triggers_parse()
    {
        // WPFGallery ControlExample shape: a template Button with Grid
        // content plus an EventTrigger storyboard on its glyphs.
        var previous = Application.Current;
        _ = new Application();
        try
        {
            var root = (Window)LoadRoot(
                "<Window.Resources>" +
                "<Style TargetType=\"{x:Type Button}\"><Setter Property=\"Template\"><Setter.Value>" +
                "<ControlTemplate TargetType=\"{x:Type Button}\">" +
                "<Button Command=\"ApplicationCommands.Copy\">" +
                "<Grid><TextBlock x:Name=\"Glyph\" Text=\"Hi\" /></Grid>" +
                "<Button.Triggers><EventTrigger RoutedEvent=\"Button.Click\">" +
                "<EventTrigger.Actions><BeginStoryboard><Storyboard>" +
                "<DoubleAnimation Storyboard.TargetName=\"Glyph\" Storyboard.TargetProperty=\"Opacity\" To=\"0\" Duration=\"0:0:0.333\" />" +
                "</Storyboard></BeginStoryboard></EventTrigger.Actions>" +
                "</EventTrigger></Button.Triggers>" +
                "</Button></ControlTemplate></Setter.Value></Setter></Style>" +
                "</Window.Resources><Button Content=\"Go\" />");
            var template = Assert.IsType<ControlTemplate>(
                Assert.IsType<Setter>(Res<Style>(root.Resources, typeof(Button)).Setters[0]).Value);
            var button = Assert.IsType<Button>(template.VisualTree);
            Assert.Same(ApplicationCommands.Copy, button.Command);
            Assert.IsType<Grid>(button.Content);
            Assert.Single(template.Triggers);
            Assert.True(template.TemplateNames.ContainsKey("Glyph"));
        }
        finally
        {
            Application.Current = previous;
        }
    }

    [Fact]
    public void Geometry_live_event_trigger_runs_its_storyboard()
    {
        var previous = Application.Current;
        _ = new Application();
        try
        {
            var root = (Window)LoadRoot(
                "<StackPanel><StackPanel.Triggers>" +
                "<EventTrigger RoutedEvent=\"RadioButton.Checked\"><BeginStoryboard><Storyboard>" +
                "<DoubleAnimation Storyboard.TargetName=\"dot\" Storyboard.TargetProperty=\"Opacity\" " +
                "From=\"1\" To=\"0.5\" Duration=\"0:0:2\" />" +
                "</Storyboard></BeginStoryboard></EventTrigger></StackPanel.Triggers>" +
                "<RadioButton Content=\"A\" /><Rectangle x:Name=\"dot\" Fill=\"Black\" Opacity=\"1\" />" +
                "</StackPanel>");
            var panel = Assert.IsType<StackPanel>(root.Content);
            var radio = panel.LogicalChildren.OfType<RadioButton>().First();
            var dot = Assert.IsType<Rectangle>(root.FindName("dot"));
            radio.IsChecked = true;
            WpfAnimationClock.Advance(TimeSpan.FromSeconds(1));
            Assert.Equal(0.75, dot.Opacity, precision: 6);
            WpfAnimationClock.Advance(TimeSpan.FromSeconds(5));
            Assert.Equal(0.5, dot.Opacity, precision: 6);
        }
        finally
        {
            Application.Current = previous;
        }
    }

    [Fact]
    public void Geometry_brush_static_resource_binds_typed_properties()
    {
        var previous = Application.Current;
        _ = new Application();
        try
        {
            var root = (Window)LoadRoot(
                "<Window.Resources>" +
                "<DrawingBrush x:Key=\"Grid\"><DrawingBrush.Drawing><GeometryDrawing Brush=\"Black\" " +
                "Geometry=\"M0,0 L1,0 1,1, 0,1Z\" /></DrawingBrush.Drawing></DrawingBrush>" +
                "</Window.Resources>" +
                "<StackPanel><Border Background=\"{StaticResource Grid}\" />" +
                "<Rectangle Fill=\"{StaticResource Grid}\" /></StackPanel>");
            var panel = Assert.IsType<StackPanel>(root.Content);
            var border = Assert.IsType<Border>(panel.LogicalChildren[0]);
            var rectangle = Assert.IsType<Rectangle>(panel.LogicalChildren[1]);
            var brush = Assert.IsType<DrawingBrush>(border.Background);
            Assert.Same(brush, rectangle.Fill);
            Assert.Equal(0x000000FFu, BrushRender.ResolveFill(brush));
        }
        finally
        {
            Application.Current = previous;
        }
    }

    [Fact]
    public void Geometry_textblock_folds_inlines_and_linebreaks()
    {
        var root = (Window)LoadRoot(
            "<TextBlock><Span>Composite Geometries</Span><LineBreak />purposes.</TextBlock>");
        var text = Assert.IsType<TextBlock>(root.Content);
        Assert.Equal("Composite Geometries\npurposes.", text.Text);
    }

    [Fact]
    public void Geometry_path_minilanguage_parses_shapes_and_arcs()
    {
        var geometry = Geometry.Parse("M 10,50 L 200,70");
        Assert.Contains("L 200 70", geometry.ToPathData());
        var relative = Geometry.Parse("m 10,10 l 5,0 v 5 h -5 z");
        Assert.Contains("Z", relative.ToPathData());
        var arc = Geometry.Parse("M 10,100 A 100,50 45 1 0 200,100");
        Assert.Contains("A 100 50 45 1 0 200 100", arc.ToPathData());
        var smooth = Geometry.Parse("M 10,100 C35,0 135,0 160,100 S285,200 310,100");
        Assert.Contains("C ", smooth.ToPathData());
        var quad = Geometry.Parse("M 10,100 Q 200,200 300,100 T 400,100");
        Assert.Contains("Q ", quad.ToPathData());
        var loaded = XamlLoader.Load($"<Window {Ns}><Window.Resources><GeometryGroup x:Key=\"G\" FillRule=\"Nonzero\">" +
            "<LineGeometry StartPoint=\"10,10\" EndPoint=\"20,30\" />" +
            "<EllipseGeometry Center=\"100, 100\" RadiusX=\"50\" RadiusY=\"50\" />" +
            "</GeometryGroup></Window.Resources></Window>");
        Assert.True(((Window)loaded.Root).Resources.TryGetValue("G", out var raw), "Resource 'G' was not found.");
        var group = Assert.IsType<GeometryGroup>(raw);
        Assert.Equal(FillRule.Nonzero, group.FillRule);
        Assert.Equal(2, group.Children.Count);
        Assert.ThrowsAny<Exception>(() => Geometry.Parse("L 10,10"));
    }

    [Fact]
    public void Geometry_combined_geometry_retains_mode_and_inputs()
    {
        var loaded = XamlLoader.Load($"<Window {Ns}><Border><Border.Background>" +
            "<DrawingBrush><DrawingBrush.Drawing><GeometryDrawing Brush=\"Black\">" +
            "<GeometryDrawing.Geometry><CombinedGeometry GeometryCombineMode=\"Exclude\">" +
            "<CombinedGeometry.Geometry1><EllipseGeometry RadiusX=\"50\" RadiusY=\"50\" Center=\"75,75\" />" +
            "</CombinedGeometry.Geometry1><CombinedGeometry.Geometry2>" +
            "<EllipseGeometry RadiusX=\"50\" RadiusY=\"50\" Center=\"125,75\" />" +
            "</CombinedGeometry.Geometry2></CombinedGeometry></GeometryDrawing.Geometry>" +
            "</GeometryDrawing></DrawingBrush.Drawing></DrawingBrush></Border.Background></Border></Window>");
        var border = Assert.IsType<Border>(((Window)loaded.Root).Content);
        var drawing = Assert.IsType<GeometryDrawing>(
            Assert.IsType<DrawingBrush>(border.Background).Drawing);
        var combined = Assert.IsType<CombinedGeometry>(drawing.Geometry);
        Assert.Equal(GeometryCombineMode.Exclude, combined.GeometryCombineMode);
        Assert.IsType<EllipseGeometry>(combined.Geometry1);
        Assert.IsType<EllipseGeometry>(combined.Geometry2);
        Assert.Contains("A 50 50", combined.ToPathData());
    }

    [Fact]
    public void Geometry_visual_brush_binds_forward_element_name()
    {
        var loaded = XamlLoader.Load($"<Window {Ns}><StackPanel><Viewport3D>" +
            "<Viewport3D.Camera><PerspectiveCamera /></Viewport3D.Camera>" +
            "<ModelVisual3D><ModelVisual3D.Content><GeometryModel3D>" +
            "<GeometryModel3D.Material><DiffuseMaterial><DiffuseMaterial.Brush>" +
            "<VisualBrush Visual=\"{Binding ElementName=late}\" />" +
            "</DiffuseMaterial.Brush></DiffuseMaterial></GeometryModel3D.Material>" +
            "</GeometryModel3D></ModelVisual3D.Content></ModelVisual3D>" +
            "</Viewport3D><ScrollViewer Name=\"late\" /></StackPanel></Window>");
        var panel = Assert.IsType<StackPanel>(((Window)loaded.Root).Content);
        var viewport = Assert.IsType<Viewport3D>(panel.LogicalChildren[0]);
        var model = Assert.IsType<GeometryModel3D>(
            Assert.IsType<ModelVisual3D>(viewport.Children[0]).Content);
        var brush = Assert.IsType<VisualBrush>(Assert.IsType<DiffuseMaterial>(model.Material).Brush);
        Assert.Same(viewport.FindName("late"), brush.Visual);
    }

    [Fact]
    public void Geometry_drawing_binds_brush_pen_geometry_ctor()
    {
        // Converted icon factories bind new GeometryDrawing(brush, pen,
        // geometry) at run (pack-icon geometry); the triple retains its
        // inputs like the property setters.
        var brush = new SolidColorBrush(Colors.Red);
        var pen = new Pen(brush, 2.0);
        var geometry = new RectangleGeometry { Rect = new Rect(0, 0, 1, 1) };
        var drawing = new GeometryDrawing(brush, pen, geometry);
        Assert.Same(brush, drawing.Brush);
        Assert.Same(pen, drawing.Pen);
        Assert.Same(geometry, drawing.Geometry);
    }

    [Fact]
    public void Geometry_drawing_brush_binds_drawing_ctor()
    {
        // Converted icon factories bind new DrawingBrush(drawing) at run;
        // the drawing retains like the property setter.
        var drawing = new GeometryDrawing(new SolidColorBrush(Colors.Red), null, null);
        Assert.Same(drawing, new DrawingBrush(drawing).Drawing);
    }
}
