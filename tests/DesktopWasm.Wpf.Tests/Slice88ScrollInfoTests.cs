namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Slice-88 AvalonEdit scrolling regression tests: the engine IScrollInfo
/// contract the converted TextArea/TextView explicit implementations bind
/// against (shapes verified with Mono.Cecil against the upstream
/// net10.0-windows ICSharpCode.AvalonEdit.dll call sites plus the 10.0.13
/// PresentationFramework ref pack: get/set scroll flags, get-only
/// extent/viewport/offset doubles, get/set ScrollViewer owner, the 12
/// line/page/wheel verbs, the two absolute setters, MakeVisible returning
/// Rect). The harness double below implements the interface explicitly in the
/// exact converted pattern (TextArea.ApplyScrollInfo writes owner/flags, the
/// verbs forward into SetVerticalOffset/SetHorizontalOffset); it pins that
/// the contract is implementable and routable, not engine scroll behavior.
/// No Application state is touched, so these stay outside the serialized
/// ApplicationState collection.
/// </summary>
[ResetAmbientState]
public sealed class Slice88ScrollInfoTests
{
    [Fact]
    public void Interface_shape_matches_wpf()
    {
        var face = typeof(IScrollInfo);
        Assert.True(face.IsInterface);
        Assert.Equal(typeof(bool), face.GetProperty("CanHorizontallyScroll")!.PropertyType);
        Assert.NotNull(face.GetProperty("CanHorizontallyScroll")!.GetSetMethod());
        Assert.Equal(typeof(bool), face.GetProperty("CanVerticallyScroll")!.PropertyType);
        Assert.NotNull(face.GetProperty("CanVerticallyScroll")!.GetSetMethod());
        Assert.Equal(typeof(double), face.GetProperty("ExtentWidth")!.PropertyType);
        Assert.Null(face.GetProperty("ExtentWidth")!.GetSetMethod());
        Assert.Equal(typeof(double), face.GetProperty("ExtentHeight")!.PropertyType);
        Assert.Null(face.GetProperty("ExtentHeight")!.GetSetMethod());
        Assert.Equal(typeof(double), face.GetProperty("HorizontalOffset")!.PropertyType);
        Assert.Null(face.GetProperty("HorizontalOffset")!.GetSetMethod());
        Assert.Equal(typeof(double), face.GetProperty("VerticalOffset")!.PropertyType);
        Assert.Null(face.GetProperty("VerticalOffset")!.GetSetMethod());
        Assert.Equal(typeof(double), face.GetProperty("ViewportWidth")!.PropertyType);
        Assert.Null(face.GetProperty("ViewportWidth")!.GetSetMethod());
        Assert.Equal(typeof(double), face.GetProperty("ViewportHeight")!.PropertyType);
        Assert.Null(face.GetProperty("ViewportHeight")!.GetSetMethod());
        Assert.Equal(typeof(ScrollViewer), face.GetProperty("ScrollOwner")!.PropertyType);
        Assert.NotNull(face.GetProperty("ScrollOwner")!.GetSetMethod());
    }

    [Fact]
    public void Verbs_and_setters_match_converted_call_shapes()
    {
        var face = typeof(IScrollInfo);
        foreach (var verb in new[]
        {
            "LineUp", "LineDown", "LineLeft", "LineRight",
            "PageUp", "PageDown", "PageLeft", "PageRight",
            "MouseWheelUp", "MouseWheelDown", "MouseWheelLeft", "MouseWheelRight",
        })
        {
            var method = face.GetMethod(verb, Type.EmptyTypes);
            Assert.NotNull(method);
            Assert.Equal(typeof(void), method!.ReturnType);
        }
        Assert.Equal(typeof(void),
            face.GetMethod("SetHorizontalOffset", [typeof(double)])!.ReturnType);
        Assert.Equal(typeof(void),
            face.GetMethod("SetVerticalOffset", [typeof(double)])!.ReturnType);
        Assert.Equal(typeof(Rect),
            face.GetMethod("MakeVisible", [typeof(Visual), typeof(Rect)])!.ReturnType);
    }

    [Fact]
    public void Explicit_text_area_pattern_routes_through_the_contract()
    {
        // Mirrors the converted TextArea: ApplyScrollInfo writes owner/flags,
        // the explicit verbs forward into the absolute setters, and
        // CompletionWindowBase-style readers observe offsets/viewports.
        var host = new TextAreaPattern();
        var face = (IScrollInfo)host;
        host.ApplyScrollInfo(owner: null, canHorizontallyScroll: true, canVerticallyScroll: true);
        Assert.True(face.CanHorizontallyScroll);
        Assert.True(face.CanVerticallyScroll);
        face.SetVerticalOffset(120);
        face.SetHorizontalOffset(40);
        Assert.Equal(120, face.VerticalOffset);
        Assert.Equal(40, face.HorizontalOffset);
        face.LineDown();
        Assert.Equal(136, face.VerticalOffset);
        face.LineUp();
        Assert.Equal(120, face.VerticalOffset);
        face.PageDown();
        Assert.Equal(220, face.VerticalOffset);
        face.MouseWheelUp();
        Assert.Equal(172, face.VerticalOffset);
        face.LineRight();
        Assert.Equal(48, face.HorizontalOffset);
        face.PageLeft();
        Assert.Equal(0, face.HorizontalOffset);
        Assert.Equal(1200, face.ExtentHeight);
        Assert.Equal(600, face.ViewportHeight);
        var visual = new ContainerVisual();
        var asked = new Rect(10, 200, 50, 16);
        Assert.Equal(asked, face.MakeVisible(visual, asked));
    }

    [Fact]
    public void ScrollOwner_round_trips_like_apply_scroll_info()
    {
        var host = new TextAreaPattern();
        var face = (IScrollInfo)host;
        Assert.Null(face.ScrollOwner);
        var owner = new ScrollViewer();
        host.ApplyScrollInfo(owner, canHorizontallyScroll: false, canVerticallyScroll: true);
        Assert.Same(owner, face.ScrollOwner);
        Assert.False(face.CanHorizontallyScroll);
        Assert.True(face.CanVerticallyScroll);
    }

    [Fact]
    public void Engine_scroller_keeps_pixel_scrolling_without_delegation()
    {
        // Boundary pin: the engine ScrollViewer does not implement IScrollInfo
        // (no CanContentScroll delegation; converted TextArea/TextView carry
        // their own implementations).
        Assert.False(typeof(IScrollInfo).IsAssignableFrom(typeof(ScrollViewer)));
    }

    /// <summary>
    /// Harness double in the converted TextArea shape: explicit IScrollInfo
    /// implementation over plain offset/flag backings with fixed line/page/
    /// wheel steps. Test-only routing; not engine scroll behavior.
    /// </summary>
    private sealed class TextAreaPattern : IScrollInfo
    {
        private const double LineStep = 16;
        private const double PageStep = 100;
        private const double WheelStep = 48;

        private double _horizontalOffset;
        private double _verticalOffset;

        public void ApplyScrollInfo(ScrollViewer? owner, bool canHorizontallyScroll, bool canVerticallyScroll)
        {
            ((IScrollInfo)this).ScrollOwner = owner!;
            ((IScrollInfo)this).CanHorizontallyScroll = canHorizontallyScroll;
            ((IScrollInfo)this).CanVerticallyScroll = canVerticallyScroll;
        }

        bool IScrollInfo.CanHorizontallyScroll { get; set; }

        bool IScrollInfo.CanVerticallyScroll { get; set; }

        double IScrollInfo.ExtentWidth => 800;

        double IScrollInfo.ExtentHeight => 1200;

        double IScrollInfo.HorizontalOffset => _horizontalOffset;

        ScrollViewer IScrollInfo.ScrollOwner { get; set; } = null!;

        double IScrollInfo.VerticalOffset => _verticalOffset;

        double IScrollInfo.ViewportWidth => 400;

        double IScrollInfo.ViewportHeight => 600;

        void IScrollInfo.LineUp() => ((IScrollInfo)this).SetVerticalOffset(_verticalOffset - LineStep);

        void IScrollInfo.LineDown() => ((IScrollInfo)this).SetVerticalOffset(_verticalOffset + LineStep);

        void IScrollInfo.LineLeft() => ((IScrollInfo)this).SetHorizontalOffset(_horizontalOffset - 8);

        void IScrollInfo.LineRight() => ((IScrollInfo)this).SetHorizontalOffset(_horizontalOffset + 8);

        void IScrollInfo.PageUp() => ((IScrollInfo)this).SetVerticalOffset(_verticalOffset - PageStep);

        void IScrollInfo.PageDown() => ((IScrollInfo)this).SetVerticalOffset(_verticalOffset + PageStep);

        void IScrollInfo.PageLeft() => ((IScrollInfo)this).SetHorizontalOffset(_horizontalOffset - 400);

        void IScrollInfo.PageRight() => ((IScrollInfo)this).SetHorizontalOffset(_horizontalOffset + 400);

        void IScrollInfo.MouseWheelUp() => ((IScrollInfo)this).SetVerticalOffset(_verticalOffset - WheelStep);

        void IScrollInfo.MouseWheelDown() => ((IScrollInfo)this).SetVerticalOffset(_verticalOffset + WheelStep);

        void IScrollInfo.MouseWheelLeft() => ((IScrollInfo)this).SetHorizontalOffset(_horizontalOffset - WheelStep);

        void IScrollInfo.MouseWheelRight() => ((IScrollInfo)this).SetHorizontalOffset(_horizontalOffset + WheelStep);

        void IScrollInfo.SetHorizontalOffset(double offset) =>
            _horizontalOffset = Math.Clamp(offset, 0, 800 - 400);

        void IScrollInfo.SetVerticalOffset(double offset) =>
            _verticalOffset = Math.Clamp(offset, 0, 1200 - 600);

        Rect IScrollInfo.MakeVisible(Visual visual, Rect rectangle) => rectangle;
    }
}
