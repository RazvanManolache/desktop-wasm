using System.Reflection;

namespace DesktopWasm.Wpf;

// Slice-90 AvalonEdit font converters: FontStyleConverter + FontWeightConverter
// (the convertible font-converter pair in the handycontrol package: 2
// unmapped-type gaps on ICSharpCode.AvalonEdit). Member evidence via Mono.Cecil
// on the upstream net10.0-windows ICSharpCode.AvalonEdit.dll, attributed to
// the exact converted call sites: Highlighting.HighlightingColor..ctor,
// Highlighting.Xshd.XshdColor..ctor, and Highlighting.Xshd.V2Loader..cctor all
// new up the converters (each reference is a .ctor call; ConvertFrom/ConvertTo
// flow through the base TypeConverter virtuals, which the engine overrides
// with real behavior). Live shapes verified against the 10.0.13
// PresentationCore runtime pack (sealed classes over
// System.ComponentModel.TypeConverter with CanConvertFrom/CanConvertTo/
// ConvertFrom/ConvertTo overrides plus a public .ctor) plus a live
// net10.0-windows runtime probe pinning exact behavior: style names parse
// case-insensitively with no trimming ("italic" works, "  Italic  "/"Normal "
// throw FormatException, "" throws, null ConvertFrom throws
// NotSupportedException, typed FontStyle input throws ArgumentException,
// CanConvertFrom(string) true / (int) false, ConvertTo non-string throws
// NotSupportedException); weight names parse case-insensitively through the
// full FontWeights alias set ("bold"->Bold, UltraLight->ExtraLight,
// Heavy->Black, Regular->Normal, DemiBold->SemiBold), plain integers 1..999
// map to raw weights ("450" round-trips "450", "100" lands Thin),
// 0/-5 throw ArgumentOutOfRangeException (>= 1), 1000 throws
// ArgumentOutOfRangeException (<= 999), garbage/blank throw FormatException,
// typed FontWeight input throws ArgumentException. The pair rides the default
// System.Windows.* rule once the engine declares it, so no Overrides entry is
// needed. The XAML setter path (StyleValueConverter.ParseFontWeight /
// ParseFontStyle) is untouched.

/// <summary>Parses font-slant names (matches System.Windows.FontStyleConverter).</summary>
public sealed class FontStyleConverter : System.ComponentModel.TypeConverter
{
    /// <inheritdoc/>
    public override bool CanConvertFrom(System.ComponentModel.ITypeDescriptorContext? context, Type sourceType) =>
        sourceType == typeof(string);

    /// <inheritdoc/>
    public override bool CanConvertTo(System.ComponentModel.ITypeDescriptorContext? context, Type? destinationType) =>
        destinationType == typeof(string);

    /// <inheritdoc/>
    public override object ConvertFrom(System.ComponentModel.ITypeDescriptorContext? context, System.Globalization.CultureInfo? culture, object value)
    {
        if (value is null) throw new NotSupportedException("FontStyleConverter cannot convert null.");
        if (value is not string text) throw new ArgumentException("FontStyleConverter converts strings only.", nameof(value));
        if (string.Equals(text, "Normal", StringComparison.OrdinalIgnoreCase)) return FontStyles.Normal;
        if (string.Equals(text, "Italic", StringComparison.OrdinalIgnoreCase)) return FontStyles.Italic;
        if (string.Equals(text, "Oblique", StringComparison.OrdinalIgnoreCase)) return FontStyles.Oblique;
        throw new FormatException($"'{text}' is not a FontStyle (Normal/Italic/Oblique).");
    }

    /// <inheritdoc/>
    public override object? ConvertTo(System.ComponentModel.ITypeDescriptorContext? context, System.Globalization.CultureInfo? culture, object? value, Type destinationType)
    {
        if (destinationType == typeof(string) && value is FontStyle style) return style.ToString();
        throw new NotSupportedException("FontStyleConverter converts FontStyle to string only.");
    }
}

/// <summary>Parses font-weight names and OpenType values (matches System.Windows.FontWeightConverter).</summary>
public sealed class FontWeightConverter : System.ComponentModel.TypeConverter
{
    /// <inheritdoc/>
    public override bool CanConvertFrom(System.ComponentModel.ITypeDescriptorContext? context, Type sourceType) =>
        sourceType == typeof(string);

    /// <inheritdoc/>
    public override bool CanConvertTo(System.ComponentModel.ITypeDescriptorContext? context, Type? destinationType) =>
        destinationType == typeof(string);

    /// <inheritdoc/>
    public override object ConvertFrom(System.ComponentModel.ITypeDescriptorContext? context, System.Globalization.CultureInfo? culture, object value)
    {
        if (value is null) throw new NotSupportedException("FontWeightConverter cannot convert null.");
        if (value is not string text) throw new ArgumentException("FontWeightConverter converts strings only.", nameof(value));
        var prop = typeof(FontWeights).GetProperty(text,
            BindingFlags.Static | BindingFlags.Public | BindingFlags.IgnoreCase);
        if (prop?.GetValue(null) is FontWeight named) return named;
        if (int.TryParse(text, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var number))
        {
            if (number < 1) throw new ArgumentOutOfRangeException(nameof(value), $"weightValue ('{number}') must be greater than or equal to '1'.");
            if (number > 999) throw new ArgumentOutOfRangeException(nameof(value), $"weightValue ('{number}') must be less than or equal to '999'.");
            return FontWeight.FromRawWeight(number);
        }
        throw new FormatException($"'{text}' is not a FontWeight (Thin/ExtraLight/.../Black or 1-999).");
    }

    /// <inheritdoc/>
    public override object? ConvertTo(System.ComponentModel.ITypeDescriptorContext? context, System.Globalization.CultureInfo? culture, object? value, Type destinationType)
    {
        if (destinationType == typeof(string) && value is FontWeight weight) return weight.ToString();
        throw new NotSupportedException("FontWeightConverter converts FontWeight to string only.");
    }
}
