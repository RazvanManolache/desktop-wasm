namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Slice-90 AvalonEdit IME/font-converter regression tests: the InputMethod
/// attached contract (IsInputMethodEnabled/IsInputMethodSuspended flags the
/// converted ImeSupport suspend/resume loop writes) plus the FontStyleConverter
/// / FontWeightConverter pair (the converters converted HighlightingColor,
/// XshdColor, and V2Loader new up). Member shapes verified with Mono.Cecil
/// against the 10.0.13 PresentationCore runtime pack plus a live
/// net10.0-windows runtime probe (Enabled defaults true, Suspended defaults
/// false, set round-trips; style names case-insensitive with no trimming;
/// weight names case-insensitive through the FontWeights alias set; numerics
/// 1..999 land raw; out-of-range numerics throw ArgumentOutOfRangeException;
/// null ConvertFrom throws NotSupportedException; typed input throws
/// ArgumentException; ConvertTo non-string throws NotSupportedException).
/// No Application state is touched, so these stay outside the serialized
/// ApplicationState collection.
/// </summary>
[ResetAmbientState]
public sealed class Slice90InputMethodTests
{
    [Fact]
    public void Attached_defaults_match_live_probe()
    {
        // Live probe: Enabled defaults true, Suspended defaults false.
        var target = new ContentControl();
        Assert.True(InputMethod.GetIsInputMethodEnabled(target));
        Assert.False(InputMethod.GetIsInputMethodSuspended(target));
    }

    [Fact]
    public void Suspended_set_round_trips_like_converted_ime_support()
    {
        // Mirrors converted ImeSupport..ctor/TextAreaOptionChanged: suspend
        // while the editor owns IME, resume otherwise, read back exactly.
        var target = new ContentControl();
        InputMethod.SetIsInputMethodSuspended(target, true);
        Assert.True(InputMethod.GetIsInputMethodSuspended(target));
        InputMethod.SetIsInputMethodSuspended(target, false);
        Assert.False(InputMethod.GetIsInputMethodSuspended(target));
    }

    [Fact]
    public void Enabled_set_round_trips()
    {
        var target = new ContentControl();
        InputMethod.SetIsInputMethodEnabled(target, false);
        Assert.False(InputMethod.GetIsInputMethodEnabled(target));
        InputMethod.SetIsInputMethodEnabled(target, true);
        Assert.True(InputMethod.GetIsInputMethodEnabled(target));
    }

    [Fact]
    public void Flags_are_per_element()
    {
        var first = new ContentControl();
        var second = new ContentControl();
        InputMethod.SetIsInputMethodSuspended(first, true);
        Assert.True(InputMethod.GetIsInputMethodSuspended(first));
        Assert.False(InputMethod.GetIsInputMethodSuspended(second));
    }

    [Fact]
    public void Accessors_reject_null()
    {
        Assert.Throws<ArgumentNullException>(() => InputMethod.GetIsInputMethodEnabled(null!));
        Assert.Throws<ArgumentNullException>(() => InputMethod.SetIsInputMethodEnabled(null!, true));
        Assert.Throws<ArgumentNullException>(() => InputMethod.GetIsInputMethodSuspended(null!));
        Assert.Throws<ArgumentNullException>(() => InputMethod.SetIsInputMethodSuspended(null!, true));
    }

    [Fact]
    public void Attached_properties_carry_live_identities()
    {
        Assert.Equal("IsInputMethodEnabled", InputMethod.IsInputMethodEnabledProperty.Name);
        Assert.Equal(typeof(bool), InputMethod.IsInputMethodEnabledProperty.PropertyType);
        Assert.Equal(typeof(InputMethod), InputMethod.IsInputMethodEnabledProperty.OwnerType);
        Assert.Equal("IsInputMethodSuspended", InputMethod.IsInputMethodSuspendedProperty.Name);
        Assert.Equal(typeof(bool), InputMethod.IsInputMethodSuspendedProperty.PropertyType);
        Assert.Equal(typeof(InputMethod), InputMethod.IsInputMethodSuspendedProperty.OwnerType);
    }

    [Fact]
    public void Os_ime_surface_stays_follow_up()
    {
        // Boundary pin: no OS text-services context behind the flags (no
        // composition loop, no IME state machine); Current, the PreferredIme*
        // /ImeState/SpeechMode surface, StateChanged, and InputScope stay
        // follow-ups rather than dead shims.
        Assert.Null(typeof(InputMethod).GetProperty("Current"));
        Assert.Null(typeof(InputMethod).GetEvent("StateChanged"));
        foreach (var name in new[]
        {
            "GetPreferredImeState", "GetPreferredImeConversionMode",
            "GetPreferredImeSentenceMode", "GetImeState", "ShowConfigureUI",
        })
            Assert.Null(typeof(InputMethod).GetMethod(name));
        Assert.Null(typeof(InputMethod).GetField("PreferredImeStateProperty"));
        Assert.Null(typeof(InputMethod).GetField("CurrentProperty"));
    }

    [Fact]
    public void Style_names_round_trip_case_insensitively()
    {
        var converter = new FontStyleConverter();
        Assert.Equal(FontStyles.Normal, converter.ConvertFromString("Normal"));
        Assert.Equal(FontStyles.Italic, converter.ConvertFromString("Italic"));
        Assert.Equal(FontStyles.Oblique, converter.ConvertFromString("Oblique"));
        Assert.Equal(FontStyles.Italic, converter.ConvertFromString("italic"));
        Assert.Equal("Italic", converter.ConvertToString(FontStyles.Italic));
        Assert.Equal("Normal", converter.ConvertToString(FontStyles.Normal));
        Assert.Equal("Oblique", converter.ConvertToString(FontStyles.Oblique));
    }

    [Fact]
    public void Style_rejects_live_matched_inputs()
    {
        var converter = new FontStyleConverter();
        // No trimming (live "  Italic  " and "Normal " throw), no weight
        // names, no blanks.
        foreach (var bad in new[] { "  Italic  ", "Normal ", "", "BOLD", "Garbage" })
            Assert.Throws<FormatException>(() => converter.ConvertFromString(bad));
        Assert.Throws<NotSupportedException>(() => converter.ConvertFrom(null!));
        Assert.Throws<ArgumentException>(() => converter.ConvertFrom(FontStyles.Italic));
        Assert.Throws<NotSupportedException>(() => converter.ConvertTo(FontStyles.Italic, typeof(int)));
        Assert.True(converter.CanConvertFrom(typeof(string)));
        Assert.False(converter.CanConvertFrom(typeof(int)));
        Assert.True(converter.CanConvertTo(typeof(string)));
        Assert.False(converter.CanConvertTo(typeof(int)));
    }

    [Fact]
    public void Weight_names_cover_the_live_alias_set()
    {
        var converter = new FontWeightConverter();
        Assert.Equal(FontWeights.Bold, converter.ConvertFromString("Bold"));
        Assert.Equal(FontWeights.Bold, converter.ConvertFromString("bold"));
        Assert.Equal(FontWeights.ExtraLight, converter.ConvertFromString("UltraLight"));
        Assert.Equal(FontWeights.Black, converter.ConvertFromString("Heavy"));
        Assert.Equal(FontWeights.Normal, converter.ConvertFromString("Regular"));
        Assert.Equal(FontWeights.SemiBold, converter.ConvertFromString("DemiBold"));
        Assert.Equal("Bold", converter.ConvertToString(FontWeights.Bold));
        Assert.Equal("ExtraLight", converter.ConvertToString(FontWeights.ExtraLight));
    }

    [Fact]
    public void Weight_numerics_land_raw_like_live()
    {
        var converter = new FontWeightConverter();
        Assert.Equal(FontWeights.Bold, converter.ConvertFromString("700"));
        Assert.Equal(FontWeights.Thin, converter.ConvertFromString("100"));
        // 450 stays a raw 450 (ConvertToString "450"), not snapped to Medium.
        var raw = (FontWeight)converter.ConvertFromString("450")!;
        Assert.Equal("450", converter.ConvertToString(raw));
        Assert.NotEqual(FontWeights.Medium, raw);
        Assert.Equal("Thin", converter.ConvertToString(FontWeight.FromRawWeight(100)));
        // Range pins (live ArgumentOutOfRangeException both ends).
        Assert.Throws<ArgumentOutOfRangeException>(() => converter.ConvertFromString("1000"));
        Assert.Throws<ArgumentOutOfRangeException>(() => converter.ConvertFromString("0"));
        Assert.Throws<ArgumentOutOfRangeException>(() => converter.ConvertFromString("-5"));
    }

    [Fact]
    public void Weight_rejects_live_matched_inputs()
    {
        var converter = new FontWeightConverter();
        foreach (var bad in new[] { "  Bold  ", "", "Garbage" })
            Assert.Throws<FormatException>(() => converter.ConvertFromString(bad));
        Assert.Throws<NotSupportedException>(() => converter.ConvertFrom(null!));
        Assert.Throws<ArgumentException>(() => converter.ConvertFrom(FontWeights.Bold));
        Assert.Throws<NotSupportedException>(() => converter.ConvertTo(FontWeights.Bold, typeof(int)));
        Assert.True(converter.CanConvertFrom(typeof(string)));
        Assert.False(converter.CanConvertFrom(typeof(int)));
        Assert.True(converter.CanConvertTo(typeof(string)));
        Assert.False(converter.CanConvertTo(typeof(int)));
    }

    [Fact]
    public void Converters_carry_public_parameterless_ctors()
    {
        // The exact converted call shape: HighlightingColor/XshdColor/
        // V2Loader each new up the converters.
        Assert.NotNull(typeof(FontStyleConverter).GetConstructor(Type.EmptyTypes));
        Assert.NotNull(typeof(FontWeightConverter).GetConstructor(Type.EmptyTypes));
        Assert.True(typeof(FontStyleConverter).IsSealed);
        Assert.True(typeof(FontWeightConverter).IsSealed);
        Assert.True(typeof(FontStyleConverter).IsSubclassOf(typeof(System.ComponentModel.TypeConverter)));
        Assert.True(typeof(FontWeightConverter).IsSubclassOf(typeof(System.ComponentModel.TypeConverter)));
    }
}
