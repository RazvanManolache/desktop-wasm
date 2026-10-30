namespace DesktopWasm.Wpf;

// Slice-82 HandyControl lib markup trio: IAddChild (implemented explicitly by
// HandyControl.Media.Animation.GeometryAnimationUsingKeyFrames),
// StyleTypedPropertyAttribute (HandyControl.Controls.StepBar marks
// ItemContainerStyle/StepBarItem) and AutoToolTipPlacement (HandyControl.
// Controls.RangeSlider registers an AutoToolTipPlacement DP defaulting to 0
// plus AutoToolTipPrecision). All three ride the default System.Windows.*
// namespace rule once the engine declares them, so no Overrides entry is
// needed. Shapes verified against the live 10.0.13 WindowsDesktop reference
// assemblies plus the runtime IL (Slider DP defaults None/0 with the
// defined-enum and non-negative validation the setters below mirror).

/// <summary>
/// Content receiver (matches System.Windows.Markup.IAddChild, PresentationCore):
/// XAML content lands through AddChild (elements) and AddText (text). The
/// converted GeometryAnimationUsingKeyFrames implements this interface
/// explicitly; the engine declares the contract so the conversion maps.
/// </summary>
public interface IAddChild
{
    void AddChild(object value);

    void AddText(string text);
}

/// <summary>
/// Style property annotation (matches System.Windows.StyleTypedPropertyAttribute,
/// PresentationFramework): sealed attribute, Class-only, AllowMultiple, public
/// parameterless ctor, settable Property/StyleTargetType (both null by
/// default). Converted StepBar carries
/// [StyleTypedProperty(Property = "ItemContainerStyle",
/// StyleTargetType = typeof(StepBarItem))]; the converter keeps attributes
/// verbatim, so the engine carries the contract for the gap check.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class StyleTypedPropertyAttribute : Attribute
{
    public string? Property { get; set; }

    public Type? StyleTargetType { get; set; }
}

/// <summary>
/// Slider auto-tooltip placement (matches
/// System.Windows.Controls.Primitives.AutoToolTipPlacement): None 0,
/// TopLeft 1, BottomRight 2.
/// </summary>
public enum AutoToolTipPlacement
{
    None = 0,
    TopLeft = 1,
    BottomRight = 2,
}
