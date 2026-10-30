using DesktopWasm.Graphics;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Slice-78 HandyControl lib Media3D x5 cluster: the five remaining
/// CoverFlow/CoverFlowItem/FlipNumber rows from the slice-77 report
/// (ContainerUIElement3D, Vector3DCollection, ProjectionCamera,
/// Point3DAnimation, RayMeshGeometry3DHitTestResult; live manifest
/// conversion-failed/4/83, split 0/23/60, all images sha256-verified),
/// each reproduced member-by-member with Mono.Cecil against the upstream
/// net10.0-windows HandyControl.dll:
/// CoverFlow._point3DAnimation = new Point3DAnimation(camera-relative
/// Point3D, Duration) driven through Animatable.BeginAnimation on
/// ProjectionCamera.PositionProperty (OnApplyTemplate/UpdateIndex);
/// CoverFlow.Viewport3D_MouseLeftButtonDown keeps only
/// VisualTreeHelper.HitTest(Visual, Point) results that are
/// RayMeshGeometry3DHitTestResult and reads MeshHit (via
/// CoverFlowItem.HitTest(MeshGeometry3D)); FlipNumber builds a
/// ContainerUIElement3D and fills Children (ctor/get_Children);
/// CoverFlowItem fills Vector3DCollection normals (ctor/Add).
/// Supporting surface, all real behavior: UIElement3D base,
/// ProjectionCamera with a Position DP (+LookDirection/UpDirection),
/// Point3D clock evaluation through the deterministic pump, 3D ray-cast
/// hit-testing inside the 2D point pick on Viewport3D (double-sided
/// Moller-Trumbore over world-space mesh triangles, nearest wins,
/// misses stay null), and Transform3D/Camera rebases onto Animatable
/// with real DPs (TranslateTransform3D XYZ + (x,y,z) ctor,
/// RotateTransform3D Rotation/Center + (Rotation3D) ctor,
/// ScaleTransform3D Scale/Center, AxisAngleRotation3D Axis/Angle).
/// MeshHit is the world-space intersection (the forward-only engine
/// transforms carry no inverses for mesh-local reporting).
/// </summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class Media3DSlice78Tests : IDisposable
{
    public Media3DSlice78Tests() => WpfAnimationClock.ResetForTests();

    public void Dispose() => WpfAnimationClock.ResetForTests();

    private sealed class TestCamera : ProjectionCamera
    {
    }

    [Fact]
    public void Vector3DCollection_parses_and_adds()
    {
        var parsed = Vector3DCollection.Parse("1,2,3 4,5,6");
        Assert.Equal([new Vector3D(1, 2, 3), new Vector3D(4, 5, 6)], parsed);
        var collection = new Vector3DCollection();
        collection.Add(new Vector3D(0, 0, 1));
        Assert.Equal([new Vector3D(0, 0, 1)], collection);
        Assert.Throws<FormatException>(() => Vector3DCollection.Parse("1,2"));
        Assert.Throws<FormatException>(() => Vector3DCollection.Parse("1,2,three"));
    }

    [Fact]
    public void ContainerUIElement3D_hosts_visual_children()
    {
        var container = new ContainerUIElement3D();
        Assert.IsAssignableFrom<UIElement3D>(container);
        Assert.IsAssignableFrom<Visual3D>(container);
        var child = new ModelVisual3D();
        container.Children.Add(child);
        Assert.Same(child, Assert.Single(container.Children));
        container.Children.Remove(child);
        Assert.Empty(container.Children);
    }

    [Fact]
    public void ProjectionCamera_Position_DP_with_engine_default_view()
    {
        // Real WPF defaults Position to the origin; the engine keeps its
        // documented (0,0,5) default view on PerspectiveCamera only.
        Assert.Equal(new Point3D(0, 0, 0), new TestCamera().Position);
        Assert.Equal(new Vector3D(0, 0, -1), new TestCamera().LookDirection);
        Assert.Equal(new Vector3D(0, 1, 0), new TestCamera().UpDirection);
        var camera = new PerspectiveCamera();
        Assert.Equal(new Point3D(0, 0, 5), camera.Position);
        camera.SetValue(ProjectionCamera.PositionProperty, new Point3D(1, 2, 3));
        Assert.Equal(new Point3D(1, 2, 3), camera.Position);
        Assert.Equal(new Point3D(1, 2, 3), camera.GetValue(ProjectionCamera.PositionProperty));
        Assert.Same(ProjectionCamera.PositionProperty,
            DependencyProperty.FromName(typeof(PerspectiveCamera), "Position"));
    }

    [Fact]
    public void TranslateTransform3D_DP_round_trip()
    {
        var moved = new TranslateTransform3D(1, 2, 3);
        Assert.Equal(1.0, moved.OffsetX);
        Assert.Equal(new Point3D(2, 3, 4), moved.Transform(new Point3D(1, 1, 1)));
        moved.SetValue(TranslateTransform3D.OffsetXProperty, 4.0);
        moved.SetValue(TranslateTransform3D.OffsetZProperty, 9.0);
        Assert.Equal(4.0, moved.OffsetX);
        Assert.Equal(9.0, moved.OffsetZ);
        Assert.Equal(new Point3D(5, 3, 10), moved.Transform(new Point3D(1, 1, 1)));
    }

    [Fact]
    public void AxisAngleRotation3D_DP_and_RotateTransform3D_ctor()
    {
        var rotation = new AxisAngleRotation3D { Axis = new Vector3D(0, 1, 0) };
        rotation.SetValue(AxisAngleRotation3D.AngleProperty, 90.0);
        Assert.Equal(90.0, rotation.Angle);
        var transform = new RotateTransform3D(rotation);
        Assert.Same(rotation, transform.Rotation);
        var turned = transform.Transform(new Point3D(1, 0, 0));
        Assert.Equal(0.0, turned.X, precision: 9);
        Assert.Equal(0.0, turned.Y, precision: 9);
        Assert.Equal(-1.0, turned.Z, precision: 9);
    }

    [Fact]
    public void Point3DAnimation_ctor_shape_and_clock_evaluation()
    {
        // Exact HandyControl CoverFlow call shape.
        var animation = new Point3DAnimation(
            new Point3D(0, 0, 0), new Duration(TimeSpan.FromSeconds(1)));
        Assert.Equal(new Point3D(0, 0, 0), animation.To);
        Assert.Equal(TimeSpan.FromSeconds(1), animation.Duration.TimeSpan);
        Assert.Equal(typeof(Point3D), animation.TargetPropertyType);

        var camera = new PerspectiveCamera { Position = new Point3D(0, 0, 5) };
        camera.BeginAnimation(ProjectionCamera.PositionProperty, animation);
        WpfAnimationClock.Advance(TimeSpan.FromSeconds(0.5));
        Assert.Equal(new Point3D(0, 0, 2.5), camera.Position);
        WpfAnimationClock.Advance(TimeSpan.FromSeconds(0.5));
        Assert.Equal(new Point3D(0, 0, 0), camera.Position);
    }

    [Fact]
    public void Point3DAnimation_From_unset_falls_back_to_base_value()
    {
        var camera = new PerspectiveCamera { Position = new Point3D(4, 0, 0) };
        var animation = new Point3DAnimation(new Point3D(10, 0, 0), new Duration(TimeSpan.FromSeconds(2)));
        camera.BeginAnimation(ProjectionCamera.PositionProperty, animation);
        WpfAnimationClock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(new Point3D(7, 0, 0), camera.Position);
    }

    private static Viewport3D CenterTriangleScene(
        out ModelVisual3D near, out ModelVisual3D far, Vector3D? offset = null)
    {
        var viewport = new Viewport3D
        {
            Width = 400,
            Height = 300,
            Camera = new PerspectiveCamera(),
        };
        var mesh = new MeshGeometry3D
        {
            Positions = Point3DCollection.Parse("-1,-1,0 1,-1,0 0,1,0"),
            TriangleIndices = new Int32Collection { 0, 1, 2 },
        };
        near = new ModelVisual3D { Content = new GeometryModel3D(mesh, null) };
        viewport.Children.Add(near);
        var back = new MeshGeometry3D
        {
            Positions = Point3DCollection.Parse("-1,-1,-2 1,-1,-2 0,1,-2"),
            TriangleIndices = new Int32Collection { 0, 1, 2 },
        };
        var backGeometry = new GeometryModel3D(back, null);
        if (offset.HasValue)
            backGeometry.Transform = new TranslateTransform3D(offset.Value.X, offset.Value.Y, offset.Value.Z);
        far = new ModelVisual3D { Content = backGeometry };
        viewport.Children.Add(far);
        WasmMilCore.RenderHeadless(
            new Window { Content = viewport }, new WpfSize(400, 300));
        return viewport;
    }

    [Fact]
    public void Ray_cast_hits_center_reports_MeshHit_and_nearest_wins()
    {
        var viewport = CenterTriangleScene(out var near, out _);
        // Exact CoverFlow call shape: 2D point pick, keep the mesh result.
        var result = VisualTreeHelper.HitTest(viewport, new Point(200, 150));
        var mesh = Assert.IsType<RayMeshGeometry3DHitTestResult>(result);
        Assert.Same(near, mesh.VisualHit);
        Assert.Equal(0.0, mesh.MeshHit.X, precision: 6);
        Assert.Equal(0.0, mesh.MeshHit.Y, precision: 6);
        Assert.Equal(0.0, mesh.MeshHit.Z, precision: 6);
        Assert.Equal(1.0, mesh.VertexWeight1 + mesh.VertexWeight2 + mesh.VertexWeight3, precision: 9);
        Assert.IsAssignableFrom<RayHitTestResult>(mesh);
        Assert.IsAssignableFrom<HitTestResult>(mesh);
    }

    [Fact]
    public void Ray_cast_misses_stay_null()
    {
        var viewport = CenterTriangleScene(out _, out _);
        Assert.Null(VisualTreeHelper.HitTest(viewport, new Point(5, 5)));
    }

    [Fact]
    public void Ray_cast_follows_model_transforms_and_nearest_wins()
    {
        // The back mesh rides (0,0,3) up to z=1: the center ray strikes both
        // planes, and the translated far host wins at distance 4 over the
        // near mesh at distance 5.
        var viewport = CenterTriangleScene(out _, out var far, new Vector3D(0, 0, 3));
        var result = VisualTreeHelper.HitTest(viewport, new Point(200, 150));
        var mesh = Assert.IsType<RayMeshGeometry3DHitTestResult>(result);
        Assert.Same(far, mesh.VisualHit);
        Assert.Equal(1.0, mesh.MeshHit.Z, precision: 6);
    }
}
