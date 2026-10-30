using System.Reflection;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Slice-55 regression: converted ScreenToGif.Util.SimpleKeyGesture carries
/// [System.Windows.Markup.ValueSerializerAttribute] (scoped to
/// System.ObjectModel on modern TFMs), so the engine owns a WPF-identical
/// marker and the converter redirects onto it. The annotation is carried
/// metadata only; no XAML serializer consumes it in the browser.
/// </summary>
[ResetAmbientState]
public sealed class ValueSerializerAttributeTests
{
    [Fact]
    public void Attribute_shape_matches_live_framework_contract()
    {
        var type = typeof(ValueSerializerAttribute);
        Assert.True(type.IsSealed);
        Assert.True(typeof(Attribute).IsAssignableFrom(type));
        var usage = type.GetCustomAttribute<AttributeUsageAttribute>();
        Assert.NotNull(usage);
        var expectedTargets = AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Enum |
            AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Interface;
        Assert.Equal(expectedTargets, usage.ValidOn);
        Assert.False(usage.AllowMultiple);
        Assert.True(usage.Inherited);
        Assert.NotNull(type.GetConstructor([typeof(Type)]));
        Assert.NotNull(type.GetConstructor([typeof(string)]));
        Assert.NotNull(type.GetProperty("ValueSerializerType"));
        Assert.NotNull(type.GetProperty("ValueSerializerTypeName"));
    }

    [Fact]
    public void Type_ctor_lands_type_and_name()
    {
        var attr = new ValueSerializerAttribute(typeof(string));
        Assert.Equal(typeof(string), attr.ValueSerializerType);
        Assert.Equal(typeof(string).AssemblyQualifiedName, attr.ValueSerializerTypeName);
        Assert.Throws<ArgumentNullException>(() => new ValueSerializerAttribute((Type)null!));
    }

    [Fact]
    public void String_ctor_lands_name()
    {
        var attr = new ValueSerializerAttribute("System.Windows.Input.KeyGestureValueSerializer");
        Assert.Equal("System.Windows.Input.KeyGestureValueSerializer", attr.ValueSerializerTypeName);
        Assert.Throws<ArgumentNullException>(() => new ValueSerializerAttribute((string)null!));
    }
}
