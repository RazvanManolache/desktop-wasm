namespace DesktopWasm.Wpf.Tests;

/// <summary>Slice-72 media tests: quaternion math, the Media3D remainder,
/// the media element, the shader contract and the manipulation family.</summary>
[ResetAmbientState]
public sealed class Slice72MediaTests
{
    private sealed class TestEffect : ShaderEffect
    {
        public TestEffect(PixelShader? shader) => SetShader(shader);

        public void SetShader(PixelShader? shader) => SetValue(PixelShaderProperty, shader);

        public PixelShader? GetShader() => GetValue(PixelShaderProperty) as PixelShader;

        public static DependencyProperty RegisterSampler(string name, int register) =>
            RegisterPixelShaderSamplerProperty(name, typeof(TestEffect), register);

        public void Touch(uint register) => UpdateShaderValue(register);
    }

    private sealed class ProbeManipulator : IManipulator
    {
        public int Id => 7;

        public Point GetPosition(IInputElement relativeTo) => new(3, 4);

        public void ManipulationEnded(bool cancel)
        {
        }
    }

    [Fact]
    public void Quaternion_axis_ctor_builds_unit_rotation()
    {
        var rotation = new Quaternion(new Vector3D(1, 0, 0), 90);
        var half = Math.Sqrt(2) / 2;
        Assert.Equal(half, rotation.X, 10);
        Assert.Equal(0, rotation.Y, 10);
        Assert.Equal(0, rotation.Z, 10);
        Assert.Equal(half, rotation.W, 10);
        Assert.True(rotation.IsNormalized);
        Assert.Equal(90, rotation.Angle, 6);
        var axis = rotation.Axis;
        Assert.Equal(1, axis.X, 6);
    }

    [Fact]
    public void Quaternion_multiply_identity_and_slerp_endpoints()
    {
        var rotation = new Quaternion(new Vector3D(0, 1, 0), 45);
        Assert.Equal(rotation, Quaternion.Multiply(Quaternion.Identity, rotation));
        Assert.Equal(rotation, rotation * Quaternion.Identity);
        Assert.Equal(new Quaternion(0, 0, 0, 0), rotation + new Quaternion(0, 0, 0, 0) - rotation);
        var halfway = Quaternion.Slerp(Quaternion.Identity, rotation, 0.5);
        Assert.Equal(Quaternion.Identity, Quaternion.Slerp(Quaternion.Identity, rotation, 0.0));
        Assert.Equal(rotation, Quaternion.Slerp(Quaternion.Identity, rotation, 1.0));
        Assert.True(halfway.IsNormalized);
        Assert.Equal(Quaternion.Parse("0,0,0,1"), Quaternion.Identity);
        Assert.NotEqual(Quaternion.Identity, rotation);
    }

    [Fact]
    public void QuaternionRotation3D_round_trips_and_clones()
    {
        var rotation = new QuaternionRotation3D(new Quaternion(new Vector3D(0, 0, 1), 30));
        Assert.Equal(new Quaternion(new Vector3D(0, 0, 1), 30), rotation.Quaternion);
        Assert.NotNull(QuaternionRotation3D.QuaternionProperty);
        var clone = rotation.Clone();
        Assert.Equal(rotation.Quaternion, clone.Quaternion);
        Assert.Equal(rotation.Quaternion, rotation.CloneCurrentValue().Quaternion);
    }

    [Fact]
    public void Transform3DCollection_add_clone_enumerate()
    {
        var transforms = new Transform3DCollection { new TranslateTransform3D() };
        transforms.Add(new ScaleTransform3D());
        Assert.Equal(2, transforms.Count);
        Assert.False(transforms.IsFixedSize);
        var clone = transforms.Clone();
        Assert.Equal(2, clone.Count);
        clone.RemoveAt(0);
        Assert.Equal(2, transforms.Count);
        var enumerator = transforms.GetEnumerator();
        var seen = 0;
        while (enumerator.MoveNext()) seen++;
        Assert.Equal(2, seen);
        Assert.Throws<ArgumentException>(() => ((System.Collections.IList)transforms).Add("nope"));
    }

    [Fact]
    public void Model3DGroup_clone_is_deep()
    {
        var group = new Model3DGroup();
        var child = new Model3DGroup();
        child.Children.Add(new GeometryModel3D());
        group.Children.Add(child);
        Assert.Equal(1, group.Children.Count);
        Assert.Equal(2, new Model3DCollection(new Model3D[] { child, new GeometryModel3D() }).Count);
        var clone = group.Clone();
        Assert.NotSame(group.Children, clone.Children);
        Assert.Single(clone.Children);
        var clonedChild = Assert.IsType<Model3DGroup>(clone.Children[0]);
        Assert.NotSame(child, clonedChild);
        Assert.Equal(1, clonedChild.Children.Count);
    }

    [Fact]
    public void Viewport2DVisual3D_stores_and_marks_material()
    {
        var host = new Viewport2DVisual3D();
        var material = new DiffuseMaterial();
        host.Material = material;
        Assert.Same(material, host.Material);
        Assert.False(Viewport2DVisual3D.GetIsVisualHostMaterial(material));
        Viewport2DVisual3D.SetIsVisualHostMaterial(material, true);
        Assert.True(Viewport2DVisual3D.GetIsVisualHostMaterial(material));
        Assert.NotNull(Viewport2DVisual3D.IsVisualHostMaterialProperty);
        Assert.Null(host.Geometry);
        Assert.Null(host.Visual);
        Assert.Null(host.CacheMode);
    }

    [Fact]
    public void CaptureMode_values_match_live_wpf()
    {
        Assert.Equal(0, (int)CaptureMode.None);
        Assert.Equal(1, (int)CaptureMode.Element);
        Assert.Equal(2, (int)CaptureMode.SubTree);
    }

    [Fact]
    public void MediaElement_defaults_match_live_wpf()
    {
        var media = new MediaElement();
        Assert.Equal(0.5, media.Volume);
        Assert.Equal(MediaState.Manual, media.LoadedBehavior);
        Assert.Equal(MediaState.Close, media.UnloadedBehavior);
        Assert.Equal(Stretch.Fill, media.Stretch);
        Assert.Equal(StretchDirection.Both, media.StretchDirection);
        Assert.Equal(1.0, media.SpeedRatio);
        Assert.False(media.IsMuted);
        Assert.Null(media.Source);
        Assert.False(media.IsPlaying);
        Assert.False(media.HasAudio);
        Assert.Equal(0, (int)MediaState.Manual);
        Assert.Equal(4, (int)MediaState.Stop);
        Assert.Equal(2, (int)StretchDirection.Both);
    }

    [Fact]
    public void MediaElement_playback_state_and_events()
    {
        var media = new MediaElement
        {
            Source = new Uri("https://example.test/clip.mp4"),
            Volume = 0.8,
        };
        Assert.Equal("https://example.test/clip.mp4", media.Source!.OriginalString);
        var ended = 0;
        var failed = 0;
        media.MediaEnded += (_, _) => ended++;
        media.MediaFailed += (_, e) => { failed++; Assert.NotNull(e.ErrorException); };
        media.Play();
        Assert.True(media.IsPlaying);
        media.Pause();
        Assert.False(media.IsPlaying);
        media.Play();
        media.RaiseMediaEnded();
        Assert.Equal(1, ended);
        Assert.False(media.IsPlaying);
        media.Play();
        media.RaiseMediaFailed(new InvalidOperationException("no pipeline"));
        Assert.Equal(1, failed);
        Assert.False(media.IsPlaying);
        media.Position = TimeSpan.FromSeconds(30);
        Assert.Equal(30, media.Position.TotalSeconds);
        media.Stop();
        Assert.Equal(TimeSpan.Zero, media.Position);
        media.Position = TimeSpan.FromTicks(-5);
        Assert.Equal(TimeSpan.Zero, media.Position);
        media.Close();
        Assert.False(media.IsPlaying);
    }

    [Fact]
    public void PixelShader_retains_source_and_mode()
    {
        var shader = new PixelShader
        {
            UriSource = new Uri("pack://application:,,,/effect.ps"),
            ShaderRenderMode = ShaderRenderMode.SoftwareOnly,
        };
        Assert.Equal("pack://application:,,,/effect.ps", shader.UriSource!.OriginalString);
        Assert.Equal(ShaderRenderMode.SoftwareOnly, shader.ShaderRenderMode);
        Assert.Equal(0, (int)ShaderRenderMode.Auto);
        Assert.Equal(2, (int)ShaderRenderMode.HardwareOnly);
        using var stream = new MemoryStream(new byte[] { 1, 2, 3 });
        shader.SetStreamSource(stream);
        Assert.Same(stream, shader.StreamSource);
        Assert.Throws<ArgumentNullException>(() => shader.SetStreamSource(null!));
        var clone = shader.Clone();
        Assert.Equal(shader.UriSource, clone.UriSource);
    }

    [Fact]
    public void ShaderEffect_sampler_registration_and_shader_slot()
    {
        var shader = new PixelShader();
        var effect = new TestEffect(shader);
        Assert.Same(shader, effect.GetShader());
        Assert.Same(shader, effect.CurrentPixelShader);
        var sampler = TestEffect.RegisterSampler("Sampler", 0);
        Assert.Equal("Sampler", sampler.Name);
        Assert.Equal(typeof(Brush), sampler.PropertyType);
        effect.Touch(0);
        var copy = effect.CloneCurrentValue();
        Assert.IsType<TestEffect>(copy);
    }

    [Fact]
    public void Manipulation_modes_compose_like_flags()
    {
        Assert.Equal(3, (int)ManipulationModes.Translate);
        Assert.Equal(15, (int)ManipulationModes.All);
        Assert.True(ManipulationModes.All.HasFlag(ManipulationModes.Rotate));
        Assert.Equal(new Point(3, 4), new ProbeManipulator().GetPosition(new TextBlock()));
        Assert.Equal(7, new ProbeManipulator().Id);
    }

    [Fact]
    public void ManipulationStarting_args_carry_mode_and_cancel()
    {
        var element = new TextBlock();
        var args = ManipulationStartingEventArgs.Create(element, ManipulationModes.Translate | ManipulationModes.Scale);
        Assert.Same(element, args.ManipulationContainer);
        Assert.Equal(ManipulationModes.Translate | ManipulationModes.Scale, args.Mode);
        Assert.Empty(args.Manipulators);
        Assert.False(args.Handled);
        Assert.True(args.Cancel());
        Assert.True(args.Handled);
        args.Mode = ManipulationModes.Rotate;
        args.IsSingleTouchEnabled = true;
        args.Pivot = new ManipulationPivot(new Point(1, 2), 5);
        Assert.Equal(new Point(1, 2), args.Pivot.Center);
    }

    [Fact]
    public void ManipulationInertiaStarting_args_carry_velocities_and_behaviors()
    {
        var element = new TextBlock();
        var velocities = new ManipulationVelocities(new Vector(10, 0), 5.0, new Vector(1, 1));
        var args = ManipulationInertiaStartingEventArgs.Create(element, velocities, new Point(9, 9));
        Assert.Same(velocities, args.InitialVelocities);
        Assert.Equal(new Vector(10, 0), args.InitialVelocities.LinearVelocity);
        Assert.Equal(5.0, args.InitialVelocities.AngularVelocity);
        Assert.Same(element, args.ManipulationContainer);
        args.TranslationBehavior = new InertiaTranslationBehavior { DesiredDeceleration = 2.0, InitialVelocity = new Vector(9, 0) };
        args.RotationBehavior = new InertiaRotationBehavior { DesiredRotation = 90, InitialVelocity = 4.0 };
        args.ExpansionBehavior = new InertiaExpansionBehavior { InitialRadius = 3.0 };
        Assert.Equal(2.0, args.TranslationBehavior.DesiredDeceleration);
        Assert.Equal(90, args.RotationBehavior.DesiredRotation);
        Assert.Equal(3.0, args.ExpansionBehavior.InitialRadius);
        Assert.True(args.Cancel());
    }

    [Fact]
    public void UiElement_manipulation_starting_events_dispatch()
    {
        var element = new ProbeElement();
        ManipulationStartingEventArgs? starting = null;
        ManipulationInertiaStartingEventArgs? inertia = null;
        element.ManipulationStarting += (_, e) => starting = e;
        element.ManipulationInertiaStarting += (_, e) => inertia = e;
        element.RaiseStarting(ManipulationModes.All);
        element.RaiseInertia(new ManipulationVelocities(new Vector(1, 0), 0, new Vector(0, 0)));
        Assert.NotNull(starting);
        Assert.Equal(ManipulationModes.All, starting.Mode);
        Assert.NotNull(inertia);
        Assert.True(inertia.Handled);
    }

    private sealed class ProbeElement : FrameworkElement
    {
        public void RaiseStarting(ManipulationModes mode) =>
            OnManipulationStarting(ManipulationStartingEventArgs.Create(this, mode));

        public void RaiseInertia(ManipulationVelocities velocities)
        {
            var args = ManipulationInertiaStartingEventArgs.Create(this, velocities, new Point());
            OnManipulationInertiaStarting(args);
            args.Cancel();
        }
    }
}
