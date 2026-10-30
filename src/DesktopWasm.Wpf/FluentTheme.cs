// Copyright (c) 2026 The DesktopWasm Authors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root.

namespace DesktopWasm.Wpf;

/// <summary>
/// Engine approximation of the WPF Fluent theme
/// (PresentationFramework.Fluent;component/Themes/Fluent.xaml).
///
/// Converted gallery App.xaml merges that pack URI first; the real theme
/// assembly never ships to the browser, so the engine carries the theme
/// keys the gallery (and the other Fluent-era catalog apps) actually
/// consume: text/control/layer/card brushes, the card-background brush and
/// expander padding tokens the sample pages merge, the accent and default
/// button styles, the default menu-item style MenuPage keys off, the
/// corner-radius/padding/font tokens and the BooleanToVisibilityConverter
/// instance the tile templates reference.
///
/// Values follow the WinUI light palette the Fluent theme tracks
/// (documented approximations, not the theme binary): text primary is
/// near-black, control alt fills are translucent black overlays, the card
/// stroke is a hairline, and the default accent is #0078D4. Missing
/// DynamicResource keys stay null-safe (WPF defers them); only
/// StaticResource misses fail, like WPF.
/// </summary>
internal static class FluentTheme
{
    /// <summary>Pack URI converted apps merge for the Fluent theme.</summary>
    internal const string PackUri = "pack://application:,,,/PresentationFramework.Fluent;component/Themes/Fluent.xaml";

    internal const string Xaml =
        "<ResourceDictionary " +
        "xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
        "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"> " +
        "<SolidColorBrush x:Key=\"TextFillColorPrimaryBrush\" Color=\"#E4000000\" /> " +
        "<SolidColorBrush x:Key=\"TextFillColorSecondaryBrush\" Color=\"#9E000000\" /> " +
        "<SolidColorBrush x:Key=\"TextFillColorDisabledBrush\" Color=\"#9D000000\" /> " +
        "<SolidColorBrush x:Key=\"TextControlForeground\" Color=\"#E4000000\" /> " +
        "<SolidColorBrush x:Key=\"ControlFillColorDefaultBrush\" Color=\"#F9F9F9\" /> " +
        "<SolidColorBrush x:Key=\"ControlStrokeColorDefaultBrush\" Color=\"#E5E5E5\" /> " +
        "<SolidColorBrush x:Key=\"ControlAltFillColorTertiaryBrush\" Color=\"#0A000000\" /> " +
        "<SolidColorBrush x:Key=\"ControlAltFillColorQuarternaryBrush\" Color=\"#11000000\" /> " +
        "<SolidColorBrush x:Key=\"LayerFillColorDefaultBrush\" Color=\"#F9F9F9\" /> " +
        "<SolidColorBrush x:Key=\"SolidBackgroundFillColorBaseBrush\" Color=\"#F3F3F3\" /> " +
        "<SolidColorBrush x:Key=\"CardStrokeColorDefaultBrush\" Color=\"#19000000\" /> " +
        "<SolidColorBrush x:Key=\"CardBackgroundFillColorDefaultBrush\" Color=\"#FFFFFFFF\" /> " +
        "<Thickness x:Key=\"ExpanderPadding\">12,12,12,12</Thickness> " +
        "<Thickness x:Key=\"ExpanderBorderThemeThickness\">1</Thickness> " +
        "<SolidColorBrush x:Key=\"SystemAccentColorPrimaryBrush\" Color=\"#FF0078D4\" /> " +
        "<SolidColorBrush x:Key=\"AccentColorBrushKey\" Color=\"#FF0078D4\" /> " +
        "<SolidColorBrush x:Key=\"AccentColorDark1BrushKey\" Color=\"#FF0067B8\" /> " +
        "<SolidColorBrush x:Key=\"AccentColorDark2BrushKey\" Color=\"#FF005494\" /> " +
        "<SolidColorBrush x:Key=\"AccentColorDark3BrushKey\" Color=\"#FF003966\" /> " +
        "<SolidColorBrush x:Key=\"AccentColorLight1BrushKey\" Color=\"#FF1A86D9\" /> " +
        "<SolidColorBrush x:Key=\"AccentColorLight2BrushKey\" Color=\"#FF4BA0E1\" /> " +
        "<SolidColorBrush x:Key=\"AccentColorLight3BrushKey\" Color=\"#FF83BDEA\" /> " +
        "<SolidColorBrush x:Key=\"AcrylicBackgroundFillColorDefault\" Color=\"#F9F9F9\" /> " +
        "<SolidColorBrush x:Key=\"SubtleFillColorSecondaryBrush\" Color=\"#08000000\" /> " +
        "<SolidColorBrush x:Key=\"SubtleFillColorTertiaryBrush\" Color=\"#05000000\" /> " +
        "<SolidColorBrush x:Key=\"SystemColorHighlightColorBrush\" Color=\"#3390FF\" /> " +
        "<SolidColorBrush x:Key=\"SystemColorHighlightTextColorBrush\" Color=\"#FFFFFF\" /> " +
        "<SolidColorBrush x:Key=\"SystemColorWindowColorBrush\" Color=\"#FFFFFF\" /> " +
        "<SolidColorBrush x:Key=\"SystemColorWindowTextColorBrush\" Color=\"#000000\" /> " +
        "<SolidColorBrush x:Key=\"SystemFillColorCriticalBrush\" Color=\"#C42B1C\" /> " +
        "<Double x:Key=\"ControlContentThemeFontSize\">14</Double> " +
        "<Double x:Key=\"DeemphasizedTextOpacity\">0.7</Double> " +
        "<CornerRadius x:Key=\"ControlCornerRadius\">4</CornerRadius> " +
        "<Thickness x:Key=\"ButtonPadding\">8,4,8,4</Thickness> " +
        "<FontFamily x:Key=\"SymbolThemeFontFamily\">Segoe Fluent Icons</FontFamily> " +
        "<BooleanToVisibilityConverter x:Key=\"BooleanToVisibilityConverter\" /> " +
        "<Style TargetType=\"Button\"> " +
        "<Setter Property=\"Background\" Value=\"{StaticResource ControlFillColorDefaultBrush}\" /> " +
        "<Setter Property=\"BorderBrush\" Value=\"{StaticResource ControlStrokeColorDefaultBrush}\" /> " +
        "<Setter Property=\"Foreground\" Value=\"{StaticResource TextFillColorPrimaryBrush}\" /> " +
        "<Setter Property=\"BorderThickness\" Value=\"1\" /> " +
        "<Setter Property=\"Padding\" Value=\"{StaticResource ButtonPadding}\" /> " +
        "</Style> " +
        "<Style x:Key=\"DefaultButtonStyle\" TargetType=\"Button\" " +
        "BasedOn=\"{StaticResource {x:Type Button}}\"> " +
        "</Style> " +
        "<Style x:Key=\"DefaultMenuItemStyle\" TargetType=\"MenuItem\"> " +
        "</Style> " +
        "<Style x:Key=\"AccentButtonStyle\" TargetType=\"Button\" BasedOn=\"{StaticResource DefaultButtonStyle}\"> " +
        "<Setter Property=\"Background\" Value=\"#0078D4\" /> " +
        "<Setter Property=\"BorderBrush\" Value=\"Transparent\" /> " +
        "<Setter Property=\"Foreground\" Value=\"White\" /> " +
        "</Style> " +
        "</ResourceDictionary>";
}
