namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Slice-82 markup-trio regression tests: the HandyControl lib IAddChild /
/// StyleTypedPropertyAttribute / AutoToolTipPlacement rows ride the default
/// System.Windows.* rule once the engine declares the exact upstream shapes
/// (verified member-by-member against the upstream net10.0-windows
/// HandyControl.dll with Mono.Cecil plus the live 10.0.13 WindowsDesktop
/// reference assemblies and runtime IL). No Application state is touched, so
/// these stay outside the serialized ApplicationState collection.
/// </summary>
[ResetAmbientState]
public sealed class Slice82MarkupTests
{
    [Fact]
    public void IAddChild_is_public_interface_with_exact_members()
    {
        var type = typeof(IAddChild);
        Assert.True(type.IsInterface);
        Assert.True(type.IsPublic);
        var addChild = type.GetMethod("AddChild", [typeof(object)]);
        Assert.NotNull(addChild);
        Assert.Equal(typeof(void), addChild!.ReturnType);
        var addText = type.GetMethod("AddText", [typeof(string)]);
        Assert.NotNull(addText);
        Assert.Equal(typeof(void), addText!.ReturnType);
    }

    [Fact]
    public void IAddChild_routes_content_to_implementer()
    {
        IAddChild collector = new CollectingContent();
        collector.AddChild(42);
        collector.AddText("key");
        var concrete = (CollectingContent)collector;
        Assert.Equal([42], concrete.Values);
        Assert.Equal("key", concrete.Text);
    }

    [Fact]
    public void IAddChild_supports_explicit_implementation_like_keyframe_animations()
    {
        // The converted GeometryAnimationUsingKeyFrames implements IAddChild
        // explicitly (interface-mapped AddChild/AddText plus its own public
        // surface); the contract must dispatch through an interface cast.
        IAddChild animation = new ExplicitContent();
        animation.AddChild("frame");
        animation.AddText("tick");
        var concrete = (ExplicitContent)animation;
        Assert.Equal(["frame"], concrete.Values);
        Assert.Equal("tick", concrete.Text);
    }

    [Fact]
    public void StyleTypedPropertyAttribute_defaults_and_shape()
    {
        var attribute = new StyleTypedPropertyAttribute();
        Assert.Null(attribute.Property);
        Assert.Null(attribute.StyleTargetType);
        attribute.Property = "ItemContainerStyle";
        attribute.StyleTargetType = typeof(StepBarItemFixture);
        Assert.Equal("ItemContainerStyle", attribute.Property);
        Assert.Equal(typeof(StepBarItemFixture), attribute.StyleTargetType);
        var usage = (AttributeUsageAttribute)Attribute.GetCustomAttribute(
            typeof(StyleTypedPropertyAttribute), typeof(AttributeUsageAttribute))!;
        Assert.NotNull(usage);
        Assert.Equal(AttributeTargets.Class, usage!.ValidOn);
        Assert.True(usage.AllowMultiple);
    }

    [Fact]
    public void StyleTypedPropertyAttribute_matches_stepbar_usage()
    {
        // Mirrors the converted HandyControl StepBar annotation
        // ([StyleTypedProperty(Property = "ItemContainerStyle",
        // StyleTargetType = typeof(StepBarItem))]): named-arg landing plus
        // AllowMultiple so sibling annotations keep working.
        var seen = Attribute.GetCustomAttributes(typeof(StepBarFixture), typeof(StyleTypedPropertyAttribute));
        var typed = Assert.IsType<StyleTypedPropertyAttribute>(Assert.Single(seen));
        Assert.Equal("ItemContainerStyle", typed.Property);
        Assert.Equal(typeof(StepBarItemFixture), typed.StyleTargetType);
    }

    [Fact]
    public void AutoToolTipPlacement_values_match_wpf()
    {
        Assert.Equal(0, (int)AutoToolTipPlacement.None);
        Assert.Equal(1, (int)AutoToolTipPlacement.TopLeft);
        Assert.Equal(2, (int)AutoToolTipPlacement.BottomRight);
    }

    [Fact]
    public void Slider_auto_tooltip_knobs_default_like_wpf()
    {
        var slider = new Slider();
        Assert.Equal(AutoToolTipPlacement.None, slider.AutoToolTipPlacement);
        Assert.Equal(0, slider.AutoToolTipPrecision);
    }

    [Fact]
    public void Slider_auto_tooltip_knobs_accept_defined_values()
    {
        var slider = new Slider
        {
            AutoToolTipPlacement = AutoToolTipPlacement.BottomRight,
            AutoToolTipPrecision = 3,
        };
        Assert.Equal(AutoToolTipPlacement.BottomRight, slider.AutoToolTipPlacement);
        Assert.Equal(3, slider.AutoToolTipPrecision);
        slider.AutoToolTipPlacement = AutoToolTipPlacement.TopLeft;
        Assert.Equal(AutoToolTipPlacement.TopLeft, slider.AutoToolTipPlacement);
    }

    [Fact]
    public void Slider_auto_tooltip_knobs_reject_invalid_like_validate_callbacks()
    {
        // The framework registers both DPs with ValidateValueCallbacks
        // (defined-enum, non-negative); the engine CLR knobs throw
        // ArgumentException on the same boundary (runtime IL verified).
        var slider = new Slider();
        Assert.Throws<ArgumentException>(() => slider.AutoToolTipPlacement = (AutoToolTipPlacement)7);
        Assert.Throws<ArgumentException>(() => slider.AutoToolTipPrecision = -1);
        Assert.Equal(AutoToolTipPlacement.None, slider.AutoToolTipPlacement);
        Assert.Equal(0, slider.AutoToolTipPrecision);
    }

    private sealed class CollectingContent : IAddChild
    {
        public readonly List<object> Values = [];

        public string Text = string.Empty;

        public void AddChild(object value) => Values.Add(value);

        public void AddText(string text) => Text += text;
    }

    private sealed class ExplicitContent : IAddChild
    {
        public readonly List<object> Values = [];

        public string Text = string.Empty;

        void IAddChild.AddChild(object value) => Values.Add(value);

        void IAddChild.AddText(string text) => Text += text;
    }

    private sealed class StepBarItemFixture;

    [StyleTypedProperty(Property = "ItemContainerStyle", StyleTargetType = typeof(StepBarItemFixture))]
    private sealed class StepBarFixture;
}
