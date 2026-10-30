using DesktopWasm.Graphics;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Media3D shim (Step 35): 3D math, wireframe projection, Viewport3D XAML
/// loading, timeline Completed/FillBehavior surface and rotation animation
/// through the namescope fallback. The clock only moves through
/// WpfAnimationClock.Advance, so no test depends on wall time.
/// </summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class Media3DTests : IDisposable
{
    public Media3DTests() => WpfAnimationClock.ResetForTests();

    public void Dispose() => WpfAnimationClock.ResetForTests();

    private const string Ns =
        "xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
        "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"";

    [Fact]
    public void Point_vector_and_color_parse_round_trip()
    {
        Assert.Equal(new Point3D(1, -2, 3.5), Point3D.Parse("1,-2,3.5"));
        Assert.Equal(new Vector3D(0, 1, 0), Vector3D.Parse("0, 1, 0"));
        Assert.Equal(Color.FromRgb(0x40, 0x40, 0x40), Color.Parse("#FF404040"));
        Assert.Equal(new Point3D(1, 2, 3), Assert.Single(Point3DCollection.Parse("1,2,3").Take(1)));
    }

    [Fact]
    public void Bad_math_text_fails_precisely()
    {
        Assert.Throws<FormatException>(() => Point3D.Parse("1,2"));
        Assert.Throws<FormatException>(() => Point3D.Parse("1,2,three"));
        Assert.Throws<FormatException>(() => Vector3D.Parse("0,0"));
        Assert.Throws<FormatException>(() => Color.Parse("red"));
        Assert.Throws<FormatException>(() => Point3DCollection.Parse("1,2"));
        Assert.Throws<FormatException>(() => Int32Collection.Parse("0,x,2"));
    }

    [Fact]
    public void Point3d_collection_copies_enumerable()
    {
        // Converted MaterialDesign Plane3D wraps its mesh points this way
        // (new Point3DCollection(Mesh)); the 2D sibling already carries the
        // matching constructor.
        var source = new[] { new Point3D(1, 2, 3), new Point3D(4, 5, 6) };
        var copy = new Point3DCollection(source);
        Assert.Equal(source, copy);
    }

    [Fact]
    public void Int32_collection_copies_enumerable()
    {
        // Converted MaterialDesign Plane3D wraps its triangle indices this
        // way (new Int32Collection(Indices)).
        var source = new[] { 0, 1, 2, 0, 2, 3 };
        var copy = new Int32Collection(source);
        Assert.Equal(source, copy);
    }

    [Fact]
    public void Directional_light_packs_color_and_direction()
    {
        // Converted MaterialDesign Plane3D builds its key light this way
        // (new DirectionalLight(Colors.White, new Vector3D(...))).
        var light = new DirectionalLight(Color.FromRgb(0xFF, 0xFF, 0xFF), new Vector3D(1, -2, -3));
        Assert.Equal(Color.FromRgb(0xFF, 0xFF, 0xFF), light.Color);
        Assert.Equal(new Vector3D(1, -2, -3), light.Direction);
    }

    [Fact]
    public void Transform3d_group_children_match_wpf_collection_type()
    {
        // Converted MaterialDesign Plane3D reads Transform3DGroup.Children
        // as Transform3DCollection (MissingMethod on List<Transform3D>).
        var group = new Transform3DGroup();
        var children = Assert.IsType<Transform3DCollection>(group.Children);
        children.Add(new TranslateTransform3D());
        Assert.Single(group.Children);
    }

    [Fact]
    public void Axis_angle_rotation_turns_points()
    {
        var rotation = new AxisAngleRotation3D { Axis = new Vector3D(0, 1, 0), Angle = 90 };
        var transform = new RotateTransform3D { Rotation = rotation };
        var turned = transform.Transform(new Point3D(1, 0, 0));
        Assert.Equal(0.0, turned.X, precision: 9);
        Assert.Equal(0.0, turned.Y, precision: 9);
        Assert.Equal(-1.0, turned.Z, precision: 9);
    }

    [Fact]
    public void Translate_scale_and_group_chain()
    {
        var moved = new TranslateTransform3D { OffsetX = 1, OffsetY = 2, OffsetZ = 3 };
        Assert.Equal(new Point3D(2, 3, 4), moved.Transform(new Point3D(1, 1, 1)));
        var scaled = new ScaleTransform3D { ScaleX = 2 };
        Assert.Equal(new Point3D(4, 1, 1), scaled.Transform(new Point3D(2, 1, 1)));
        var group = new Transform3DGroup();
        group.Children.Add(moved);
        group.Children.Add(scaled);
        Assert.Equal(new Point3D(4, 3, 4), group.Transform(new Point3D(1, 1, 1)));
    }

    [Fact]
    public void Projection_centers_origin_and_rejects_behind_near_plane()
    {
        var projection = new WireProjection(new PerspectiveCamera(), new WpfRect(0, 0, 400, 300));
        Assert.Equal(new WpfPoint(200, 150), projection.Project(new Point3D(0, 0, 0)));
        Assert.Null(projection.Project(new Point3D(0, 0, 10)));
    }

    [Fact]
    public void Single_triangle_renders_three_edge_lines()
    {
        var viewport = new Viewport3D { Width = 400, Height = 300, Camera = new PerspectiveCamera() };
        var mesh = new MeshGeometry3D
        {
            Positions = Point3DCollection.Parse("-1,-1,0 1,-1,0 0,1,0"),
            TriangleIndices = new Int32Collection { 0, 1, 2 },
        };
        var visual = new ModelVisual3D { Content = new GeometryModel3D { Geometry = mesh } };
        viewport.Children.Add(visual);
        var commands = WasmMilCore.RenderHeadless(
            new Window { Content = viewport }, new WpfSize(400, 300));
        var lines = commands.OfType<DrawCommand.DrawLine>().ToList();
        Assert.Equal(3, lines.Count);
        Assert.All(lines, line => Assert.Equal(viewport.StrokeRgba, line.Rgba));
    }

    private const string SceneXaml =
        "<Page " + Ns + ">" +
        "<Page.Resources><Storyboard x:Key=\"Spin\">" +
        "<DoubleAnimation Storyboard.TargetName=\"axis\" Storyboard.TargetProperty=\"Angle\" From=\"0\" To=\"90\" Duration=\"0:0:1\" />" +
        "</Storyboard></Page.Resources>" +
        "<Viewport3D>" +
        "<Viewport3D.Camera><PerspectiveCamera Position=\"0,0,5\" LookDirection=\"0,0,-1\" UpDirection=\"0,1,0\" FieldOfView=\"60\" /></Viewport3D.Camera>" +
        "<ModelVisual3D>" +
        "<ModelVisual3D.Transform><RotateTransform3D CenterX=\"0\" CenterY=\"0\" CenterZ=\"0\">" +
        "<RotateTransform3D.Rotation><AxisAngleRotation3D x:Name=\"axis\" Axis=\"0,1,0\" Angle=\"0\" /></RotateTransform3D.Rotation>" +
        "</RotateTransform3D></ModelVisual3D.Transform>" +
        "<ModelVisual3D.Content><GeometryModel3D>" +
        "<GeometryModel3D.Geometry><MeshGeometry3D Positions=\"-1,-1,0 1,-1,0 0,1,0\" TriangleIndices=\"0 1 2\" /></GeometryModel3D.Geometry>" +
        "<GeometryModel3D.Material><DiffuseMaterial Color=\"#FF404040\" /></GeometryModel3D.Material>" +
        "</GeometryModel3D></ModelVisual3D.Content>" +
        "</ModelVisual3D>" +
        "<ModelVisual3D><ModelVisual3D.Content>" +
        "<DirectionalLight Color=\"#FFFFFFFF\" Direction=\"0,0,-1\" />" +
        "</ModelVisual3D.Content></ModelVisual3D>" +
        "</Viewport3D></Page>";

    [Fact]
    public void Xaml_loads_viewport_scene_with_camera_mesh_light_and_names()
    {
        var result = XamlLoader.Load(SceneXaml);
        var page = Assert.IsType<Page>(result.Root);
        var viewport = Assert.IsType<Viewport3D>(page.Content);
        var camera = Assert.IsType<PerspectiveCamera>(viewport.Camera);
        Assert.Equal(new Point3D(0, 0, 5), camera.Position);
        Assert.Equal(new Vector3D(0, 0, -1), camera.LookDirection);
        Assert.Equal(2, viewport.Children.Count);
        var rotation = Assert.IsType<AxisAngleRotation3D>(result.Names["axis"]);
        Assert.Equal(new Vector3D(0, 1, 0), rotation.Axis);
        var first = Assert.IsType<ModelVisual3D>(viewport.Children[0]);
        var model = Assert.IsType<GeometryModel3D>(first.Content);
        var mesh = Assert.IsType<MeshGeometry3D>(model.Geometry);
        Assert.Equal(3, mesh.Positions.Count);
        Assert.Equal([0, 1, 2], mesh.TriangleIndices);
        var material = Assert.IsType<DiffuseMaterial>(model.Material);
        Assert.Equal(Color.FromRgb(0x40, 0x40, 0x40), material.Color);
        var light = Assert.IsType<DirectionalLight>(
            Assert.IsType<ModelVisual3D>(viewport.Children[1]).Content);
        Assert.Equal(new Vector3D(0, 0, -1), light.Direction);
        Assert.True(page.Resources.TryGetValue("Spin", out var spinValue));
        var board = Assert.IsType<Storyboard>(spinValue);
        Assert.Equal("axis", Storyboard.GetTargetName(Assert.Single(board.Children)));
    }

    [Fact]
    public void Rotation_animates_through_direct_and_namescope_targets()
    {
        var rotation = new AxisAngleRotation3D { Axis = new Vector3D(0, 1, 0), Angle = 0 };
        var animation = new DoubleAnimation { Duration = TimeSpan.FromSeconds(1), From = 0, To = 90 };
        Storyboard.SetTarget(animation, rotation);
        Storyboard.SetTargetProperty((object)animation, "Angle");
        var holder = new StackPanel();
        var board = new Storyboard();
        board.Children.Add(animation);
        board.Begin(holder);
        WpfAnimationClock.Advance(TimeSpan.FromSeconds(0.5));
        Assert.Equal(45.0, rotation.Angle, precision: 9);

        var result = XamlLoader.Load(SceneXaml);
        var page = Assert.IsType<Page>(result.Root);
        WpfAppBoot.NameScopes.Remove(page);
        WpfAppBoot.NameScopes.Add(page, result.Names);
        Assert.True(page.Resources.TryGetValue("Spin", out var spinValue));
        var scoped = Assert.IsType<Storyboard>(spinValue);
        scoped.Begin(page);
        WpfAnimationClock.Advance(TimeSpan.FromSeconds(0.5));
        Assert.Equal(45.0, Assert.IsType<AxisAngleRotation3D>(result.Names["axis"]).Angle, precision: 9);
    }

    [Fact]
    public void Completed_wires_and_fill_easing_attrs_parse()
    {
        var result = XamlLoader.Load(
            "<StackPanel " + Ns + "><StackPanel.Resources>" +
            "<Storyboard x:Key=\"S\" Completed=\"OnDone\" FillBehavior=\"Stop\" AccelerationRatio=\"0.2\" DecelerationRatio=\"0.3\">" +
            "<DoubleAnimation Completed=\"OnChild\" From=\"0\" To=\"1\" Duration=\"0:0:1\" />" +
            "</Storyboard></StackPanel.Resources></StackPanel>");
        var panel = Assert.IsType<StackPanel>(result.Root);
        Assert.True(panel.Resources.TryGetValue("S", out var boardValue));
        var board = Assert.IsType<Storyboard>(boardValue);
        Assert.Equal(FillBehavior.Stop, board.FillBehavior);
        Assert.Equal(0.2, board.AccelerationRatio, precision: 9);
        Assert.Equal(0.3, board.DecelerationRatio, precision: 9);
        Assert.Equal(2, result.EventWires.Count);
        Assert.Contains(result.EventWires,
            wire => wire.EventName == "Completed" && wire.HandlerName == "OnDone" &&
                ReferenceEquals(wire.Element, board));
        Assert.Contains(result.EventWires,
            wire => wire.EventName == "Completed" && wire.HandlerName == "OnChild" &&
                ReferenceEquals(wire.Element, Assert.Single(board.Children)));
    }

    [Fact]
    public void Bad_timeline_and_3d_markup_fail_precisely()
    {
        var ex = Assert.Throws<XamlLoadException>(() =>
            XamlLoader.Load("<StackPanel " + Ns + "><StackPanel.Resources>" +
                "<Storyboard x:Key=\"S\" FillBehavior=\"Bounce\" /></StackPanel.Resources></StackPanel>"));
        Assert.Contains("FillBehavior", ex.Message);
        ex = Assert.Throws<XamlLoadException>(() =>
            XamlLoader.Load("<StackPanel " + Ns + "><StackPanel.Resources>" +
                "<Storyboard x:Key=\"S\" AccelerationRatio=\"2\" /></StackPanel.Resources></StackPanel>"));
        Assert.Contains("AccelerationRatio", ex.Message);
        ex = Assert.Throws<XamlLoadException>(() =>
            XamlLoader.Load("<Grid " + Ns + "><Viewport3D><Viewport3D.Camera>" +
                "<PerspectiveCamera Position=\"0,0\" /></Viewport3D.Camera></Viewport3D></Grid>"));
        Assert.Contains("3D point", ex.Message);
        ex = Assert.Throws<XamlLoadException>(() =>
            XamlLoader.Load("<Grid " + Ns + "><Viewport3D><Viewport3D.Camera>" +
                "<OrthographicCamera />" +
                "</Viewport3D.Camera></Viewport3D></Grid>"));
        Assert.Contains("3D node", ex.Message);
        ex = Assert.Throws<XamlLoadException>(() =>
            XamlLoader.Load("<Grid " + Ns + "><Viewport3D>" +
                "<DirectionalLight Color=\"#FFFFFFFF\" Direction=\"0,0,-1\" />" +
                "</Viewport3D></Grid>"));
        Assert.Contains("ModelVisual3D", ex.Message);
        ex = Assert.Throws<XamlLoadException>(() =>
            XamlLoader.Load("<Grid " + Ns + "><Viewport3D><ModelVisual3D><ModelVisual3D.Content>" +
                "<GeometryModel3D><GeometryModel3D.Material>" +
                "<DiffuseMaterial><DiffuseMaterial.Brush><ImageBrush /></DiffuseMaterial.Brush></DiffuseMaterial>" +
                "</GeometryModel3D.Material></GeometryModel3D>" +
                "</ModelVisual3D.Content></ModelVisual3D></Viewport3D></Grid>"));
        Assert.Contains("ImageBrush", ex.Message);
        // Standalone dictionaries have no code-behind, so Completed= is precise there.
        ex = Assert.Throws<XamlLoadException>(() =>
            XamlLoader.LoadResourceDictionary(
                "<ResourceDictionary xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
                "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
                "<Storyboard x:Key=\"S\" Completed=\"OnDone\" /></ResourceDictionary>"));
        Assert.Contains("code-behind", ex.Message);
    }

    [Fact]
    public void VisualBrush_material_now_loads()
    {
        var result = XamlLoader.Load("<Grid " + Ns + "><Viewport3D><ModelVisual3D><ModelVisual3D.Content>" +
            "<GeometryModel3D><GeometryModel3D.Material>" +
            "<DiffuseMaterial><DiffuseMaterial.Brush><VisualBrush /></DiffuseMaterial.Brush></DiffuseMaterial>" +
            "</GeometryModel3D.Material></GeometryModel3D>" +
            "</ModelVisual3D.Content></ModelVisual3D></Viewport3D></Grid>");
        var root = Assert.IsType<Grid>(result.Root);
        var viewport = Assert.IsType<Viewport3D>(Assert.Single(root.LogicalChildren));
        var visual = Assert.IsType<ModelVisual3D>(Assert.Single(viewport.Children));
        var model = Assert.IsType<GeometryModel3D>(visual.Content);
        Assert.IsType<DiffuseMaterial>(model.Material);
    }
}
