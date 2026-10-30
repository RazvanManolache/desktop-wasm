// Copyright (c) 2026 The DesktopWasm Authors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Reflection;

namespace DesktopWasm.Wpf;

/// <summary>
/// Engine approximation of the HandyControl tab styles the converted demo
/// needs at application scope
/// (pack://application:,,,/HandyControl;component/Themes/Theme.xaml).
///
/// The real theme never ships to the browser in convertible form: the staged
/// HandyControl image carries it only as compiled BAML
/// (themes/skindefault.baml is a 585-byte merge root, themes/theme.baml is
/// the 466350-byte combined theme; verified by reading the staged
/// HandyControl.g.resources entries and grepping the raw bytes), and the
/// engine BAML reader covers element content only (see BamlReader: styles,
/// templates and resources are precise-error boundaries, not silent gaps).
/// The demo's own loose Style.xaml (staged as
/// Resources/Themes/Styles/Style.xaml) defines TabItemTransparent
/// (TargetType TabItem, BasedOn the lib TabItemInLine plus a transparent
/// Background), but it merges behind the line-17 lib SkinDefault stop in
/// App.xaml, so neither key resolves from converted resources today.
///
/// The engine therefore carries exactly the frontier keys the staged demo
/// views read at application scope, starting with the two tab keys the
/// staged LeftMainContent view reads on its TabControl (line 14):
/// TabControlInLine (lib theme key, adjacent to TabItemInLine in the theme
/// BAML string table; applied to a TabControl) and TabItemTransparent
/// (TargetType TabItem, matching the demo definition). Both are honest
/// chrome-less shims: empty styles with the correct TargetType, so loading
/// proceeds with default control chrome and no fabricated lib visuals (the
/// same idiom as FluentTheme.DefaultMenuItemStyle). Upstream HandyControl is
/// MIT-licensed; no upstream setter content is replicated because the BAML
/// stop keeps it unreachable. Consulted last in Application.TryFindResource,
/// so real converted or merged styles always win when a later slice makes
/// them reachable. Missing keys still fail, like WPF.
///
/// Slice-101 adds the EffectShadow4 key the same views read on their root
/// Borders (LeftMainContent line 11, MainContent lines 6 and 13). Unlike the
/// tab styles this one carries the real upstream values, not an empty shim:
/// the lib source Themes/Basic/Effects.xaml (HandyControl_Shared, MIT) is
/// loose text with five DropShadowEffect entries plus an EffectShadowColor
/// color, and the engine resource-entry reader already parses
/// DropShadowEffect entries with literal attributes (see
/// XamlLoader.TryReadEffectResourceEntry). Only EffectShadow4 ships here -
/// the smallest genuinely-advancing scope (the siblings stay follow-ups):
/// BlurRadius 25, ShadowDepth 8, Direction 270, Color #88000000 (the
/// EffectShadowColor value inlined, because resource entries take literals
/// only and reject markup extensions), Opacity .2, RenderingBias
/// Performance. The upstream o:Freeze="True" freeze hint is omitted (the
/// resource-entry reader rejects options-namespace attributes); the engine
/// Slice-102 adds the MainContent/LeftMainContent view keys the staged
/// views read at application scope: BorderClip plus the per-view
/// text/button/list styles above land as empty chrome-less shims with
/// the correct TargetType (their lib base styles ship BAML-only, so no
/// setter content is replicated - the slice-100 idiom), while Logo/Code/
/// Document/New/Left/RightGeometry carry the real upstream path data
/// (parsed through the Geometry resource entry) and
/// Boolean2VisibilityConverter aliases the stock BooleanToVisibilityConverter
/// the lib key names. CloudDrawingBrush (MainWindowContent Background) stays
/// a precise boundary: its DrawingBrush-over-pack-PNG content needs engine
/// ImageDrawing support, a later slice.
///
/// Slice-103 adds the keys behind the slice-95/96/97 app-less frontiers
/// (GeometryItem Path4GeometryItem, AppNotification LogoGeometry,
/// InteractiveDialog/TextDialog TextBlockLargeBold): with an Application the
/// staged GeometryItem/AppNotification/InteractiveDialog views now move past
/// those pins to BorderCircular (GeometryItem root style),
/// PrimaryBrush (AppNotification caption Foreground) and TextBoxExtend
/// (InteractiveDialog input style). BorderCircular and TextBoxExtend land as
/// empty chrome-less shims with the correct TargetTypes (Border, TextBox -
/// their lib setters chain through BAML-only base styles, so no content is
/// replicated); PrimaryBrush carries the real upstream structure (a
/// two-stop LinearGradientBrush, StartPoint 0,0 EndPoint 1,0) with the
/// default-skin PrimaryColor/DarkPrimaryColor #326cf3 value inlined for both
/// stops (resource entries take literals only, like EffectShadow4's inlined
/// EffectShadowColor). The EffectShadow1/2/3/5 siblings ship with real
/// upstream values (BlurRadius 5/8/14/35, ShadowDepth 1/1.5/4.5/13,
/// Direction 270, Color #88000000, Opacity .2, RenderingBias Performance).
/// TextDialog/TextDialogWithTimer advance to the converted ErrorGeometry
/// icon lookup, ButtonPrimary stays the next InteractiveDialog blocker
/// (line 15, ahead of the still-pending line-10 Locator flush), and the
/// line-17 SkinDefault BAML-only stop plus the 2 absent binaries stay
/// declared with no browser path.
///
/// Freezable default (unfrozen) applies, which never changes lookup or
/// paint behavior.
///
/// Slice-104 adds the ButtonPrimary key the staged InteractiveDialog view
/// reads on its confirm Button (line 15) plus the ErrorGeometry key both
/// staged TextDialog views read on their close Button
/// (hc:IconElement.Geometry): ButtonPrimary lands as an empty chrome-less
/// shim with TargetType Button (its upstream BasedOn ButtonBaseStyle plus
/// the PrimaryBrush setters chain through BAML-only base styles, the
/// slice-100 idiom); ErrorGeometry carries the real upstream path data from
/// the lib Themes/Theme.xaml geometry table (the LogoGeometry precedent).
/// The same slice resolves the demo "Locator" the InteractiveDialog and
/// TextDialogWithTimer roots bind their DataContext through (line 10):
/// the demo App.xaml declares it, but App.xaml never hydrates behind the
/// line-17 SkinDefault BAML-only stop, so the lookup instantiates the real
/// converted HandyControlDemo.ViewModel.ViewModelLocator from the staged
/// entry with its real SimpleIoc registrations (the slice-41 converted
/// view-model precedent) instead of a hand-built imitation.
/// Slice-105 adds the BorderDemo/Avatar/ChatBox/CardDemo/NotifyIconDemo
/// view keys the staged views read at application scope: BorderRegion plus
/// the five BorderTip styles, ToggleButtonSwitch, GroupBoxOriginal,
/// ButtonCustom, ToggleButtonIcon, StackPanelVerticalListBox and
/// WrapPanelHorizontalListBox land as empty chrome-less shims with the
/// correct TargetTypes (their lib setters chain through BAML-only base
/// styles, the slice-100 idiom); FluidMoveBehaviorWrapPanelItemsPanelTemplate
/// lands as an ItemsPanelTemplate over a plain WrapPanel (layout preserved;
/// the Blend FluidMove animation stays a declared boundary like the
/// TransitionStoryboard one); KeyboardGeometry and ImageGeometry carry the
/// real demo Basic/Geometries.xaml path data (staged-entry loose text, the
/// LogoGeometry precedent; resource entries take literals only, so the
/// o:Freeze hints are omitted). The demo StringRepeatConverter and the lib
/// Boolean2VisibilityReConverter resolve in code (see Slice105HandyViews),
/// not XAML: resource entries cannot name converted entry/lib types without
/// a staged image present.
/// Slice-106 adds the AudioGeometry key the staged ChatBox view reads on its
/// ToggleButtonAudio (line 39, hc:IconSwitchElement.Geometry; KeyboardGeometry
/// on GeometrySelected was slice-105). It carries the real upstream MIT path
/// data from the lib Themes/Theme.xaml geometry table (the ErrorGeometry
/// precedent): the exact record was additionally verified byte-verbatim inside
/// the staged HandyControl image (closing the slice-105 BAML string-table
/// attribution gap by contiguous-bytes evidence), and the key name matches the
/// converted lib HandyControl.Data.ResourceToken.AudioGeometry string constant
/// (the lib-surface key evidence). Resource entries take literals only, so the
/// o:Freeze hint is omitted.
///
/// Slice-107 adds the Object2VisibilityConverter key the converted Card
/// default template reads on its header/footer slots ({TemplateBinding
/// Header/Footer, Converter={StaticResource Object2VisibilityConverter}}):
/// the upstream lib Tools/Converter/Object2VisibilityConverter semantics
/// (null to Collapsed, anything else to Visible, ConvertBack throws), carried
/// in the engine because the lib entry ships BAML-only behind the line-17
/// SkinDefault stop.
/// Slice-108 adds the remaining default-skin theme brushes the staged
/// BrushDemo view reads on its 27 rows (root Background plus per-row
/// DynamicResource Background/Foreground pairs) plus the five Label* row
/// styles it names via StaticResource (LabelPrimary/LabelDanger/
/// LabelWarning/LabelInfo/LabelSuccess). Every brush carries the real
/// upstream default-skin value, verified live against the real WPF theme:
/// a net10.0-windows probe merged the upstream HandyControl
/// Themes/SkinDefault.xaml + Themes/Theme.xaml and read each key
/// (RegionBrush #FFFFFFFF, SecondaryRegionBrush #FFEEEEEE,
/// ThirdlyRegionBrush #FFFFFFFF, BackgroundBrush #FFEEEEEE,
/// BorderBrush #FFE0E0E0, SecondaryBorderBrush #FF757575, TitleBrush and
/// Success/Info/Danger/Warning linear #FF326CF3/#FF2DB84D/#FF00BCD4/
/// #FFDB3340/#FFE9AF20 gradients, DefaultBrush #FFFFFFFF,
/// PrimaryTextBrush #FF212121, SecondaryTextBrush #FF757575,
/// ThirdlyTextBrush #FFBDBDBD, ReverseTextBrush #FF212121,
/// TextIconBrush #FFFFFFFF, AccentBrush/DarkAccentBrush #FFF8491E,
/// DarkPrimary/Success/Info/Danger/Warning #FF326CF3/#FF2DB84D/#FF00BCD4/
/// #FFDB3340/#FFE9AF20, DarkDefaultBrush #FFF5F5F5, DarkMaskBrush #20000000,
/// DarkOpacityBrush #40000000); gradient brushes keep the upstream
/// StartPoint 0,0 EndPoint 1,0 shape with both stops inlined, the
/// PrimaryBrush/EffectShadowColor precedent (resource entries take literals
/// only). The five Label* row styles the staged BrushDemo view names plus
/// the six Label*.Small variants the staged LabelDemo view names land as
/// empty chrome-less shims with TargetType Label (the slice-100 idiom):
/// their lib setters chain through BAML-only base styles, so no setter
/// content is replicated and styled rows keep default label chrome (an
/// honest boundary, pinned in-test).
/// Slice-112 adds the CirclePanelButton key the staged CirclePanelDemo view
/// reads on its 8 ring buttons (line 8): an empty chrome-less shim with
/// TargetType Button (its upstream BasedOn chain ships BAML-only behind the
/// line-17 SkinDefault stop, the slice-100 idiom), so the converted
/// CirclePanel ring arranges with default button chrome.
/// Slice-110 adds the keys behind the slice-109 ElementGroup/Honeycomb/
/// Gravatar stops: ClockGeometry carries the real upstream path data from
/// the lib Themes/Theme.xaml geometry table (the ErrorGeometry precedent;
/// start/middle/end verified byte-verbatim inside the staged HandyControl
/// image) and ListBoxCustom lands as an empty chrome-less shim with
/// TargetType ListBox (its BasedOn ListBoxBaseStyle plus the
/// ItemContainerStyle/HorizontalContentAlignment setters chain through
/// BAML-only base styles, the slice-100 idiom). GravatarCircle and
/// GravatarCircleImg resolve in code (see Slice110HandyElement), not XAML:
/// their TargetType is the converted lib Gravatar control, and resource
/// entries cannot name converted lib types without a staged image present.
/// </summary>
internal static class HandyTheme
{
    /// <summary>Converted demo ViewModelLocator full name (key "Locator").</summary>
    internal const string LocatorTypeName = "HandyControlDemo.ViewModel.ViewModelLocator";

    private static readonly object LocatorGate = new();
    private static Assembly? _locatorSource;
    private static object? _locator;

    /// <summary>
    /// Resolves the demo "Locator" resource from the staged converted entry.
    /// True with the shared converted ViewModelLocator instance when the
    /// current element source carries that exact type with a default
    /// constructor; false (a WPF miss) when there is no source, no such
    /// type, no default constructor, or its constructor throws. Cached per
    /// source assembly, so the SimpleIoc registrations in the converted
    /// constructor run exactly once per staged entry.
    /// </summary>
    internal static bool TryResolveLocator(object key, out object? value)
    {
        value = null;
        if (key is not string name || !string.Equals(name, "Locator", StringComparison.Ordinal))
            return false;
        var source = XamlLoader.CustomElementSource;
        if (source is null)
            return false;
        lock (LocatorGate)
        {
            if (_locator is not null && ReferenceEquals(_locatorSource, source))
            {
                value = _locator;
                return true;
            }
            Type? type;
            try { type = source.GetType(LocatorTypeName, throwOnError: false, ignoreCase: false); }
            catch { return false; }
            if (type is null || type.IsAbstract)
                return false;
            if (type.GetConstructor(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                    null, Type.EmptyTypes, null) is null)
                return false;
            object instance;
            try { instance = Activator.CreateInstance(type, nonPublic: true)!; }
            catch { return false; }
            _locatorSource = source;
            _locator = instance;
            value = instance;
            return true;
        }
    }

    /// <summary>Pack URI converted HandyControl apps merge for the lib theme.</summary>
    internal const string ThemePackUri = "pack://application:,,,/HandyControl;component/Themes/Theme.xaml";

    /// <summary>Pack URI converted HandyControl apps merge for the lib skin.</summary>
    internal const string SkinPackUri = "pack://application:,,,/HandyControl;component/Themes/SkinDefault.xaml";

    internal const string Xaml =
        "<ResourceDictionary " +
        "xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
        "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"> " +
        "<Style x:Key=\"TabControlInLine\" TargetType=\"TabControl\"> " +
        "</Style> " +
        "<Style x:Key=\"TabItemTransparent\" TargetType=\"TabItem\"> " +
        "</Style> " +
        "<DropShadowEffect x:Key=\"EffectShadow4\" BlurRadius=\"25\" ShadowDepth=\"8\" " +
        "Direction=\"270\" Color=\"#88000000\" Opacity=\".2\" RenderingBias=\"Performance\" /> " +
        "<DropShadowEffect x:Key=\"EffectShadow1\" BlurRadius=\"5\" ShadowDepth=\"1\" " +
        "Direction=\"270\" Color=\"#88000000\" Opacity=\".2\" RenderingBias=\"Performance\" /> " +
        "<DropShadowEffect x:Key=\"EffectShadow2\" BlurRadius=\"8\" ShadowDepth=\"1.5\" " +
        "Direction=\"270\" Color=\"#88000000\" Opacity=\".2\" RenderingBias=\"Performance\" /> " +
        "<DropShadowEffect x:Key=\"EffectShadow3\" BlurRadius=\"14\" ShadowDepth=\"4.5\" " +
        "Direction=\"270\" Color=\"#88000000\" Opacity=\".2\" RenderingBias=\"Performance\" /> " +
        "<DropShadowEffect x:Key=\"EffectShadow5\" BlurRadius=\"35\" ShadowDepth=\"13\" " +
        "Direction=\"270\" Color=\"#88000000\" Opacity=\".2\" RenderingBias=\"Performance\" /> " +
                "<Style x:Key=\"BorderClip\" TargetType=\"Border\"> </Style> " +
        "<Style x:Key=\"BorderCircular\" TargetType=\"Border\"> </Style> " +
        "<Style x:Key=\"TextBlockDefault\" TargetType=\"TextBlock\"> </Style> " +
        "<Style x:Key=\"TextBlockLargeBold\" TargetType=\"TextBlock\"> </Style> " +
        "<Style x:Key=\"ToggleButtonIconTransparent\" TargetType=\"ToggleButton\"> </Style> " +
        "<Style x:Key=\"ButtonIcon\" TargetType=\"Button\"> </Style> " +
        "<Style x:Key=\"ButtonIconCircular\" TargetType=\"Button\"> </Style> " +
        "<Style x:Key=\"ButtonPrimary\" TargetType=\"Button\"> </Style> " +
        "<Style x:Key=\"BorderRegion\" TargetType=\"Border\"> </Style> " +
        "<Style x:Key=\"BorderTipPrimary\" TargetType=\"Border\"> </Style> " +
        "<Style x:Key=\"BorderTipDanger\" TargetType=\"Border\"> </Style> " +
        "<Style x:Key=\"BorderTipWarning\" TargetType=\"Border\"> </Style> " +
        "<Style x:Key=\"BorderTipInfo\" TargetType=\"Border\"> </Style> " +
        "<Style x:Key=\"BorderTipSuccess\" TargetType=\"Border\"> </Style> " +
        "<Style x:Key=\"ToggleButtonSwitch\" TargetType=\"ToggleButton\"> </Style> " +
        "<Style x:Key=\"GroupBoxOriginal\" TargetType=\"GroupBox\"> </Style> " +
        "<Style x:Key=\"ButtonCustom\" TargetType=\"Button\"> </Style> " +
        "<Style x:Key=\"ToggleButtonIcon\" TargetType=\"ToggleButton\"> </Style> " +
        "<Style x:Key=\"StackPanelVerticalListBox\" TargetType=\"ListBox\"> </Style> " +
        "<Style x:Key=\"WrapPanelHorizontalListBox\" TargetType=\"ListBox\"> </Style> " +
        "<ItemsPanelTemplate x:Key=\"FluidMoveBehaviorWrapPanelItemsPanelTemplate\"><WrapPanel />" +
        "</ItemsPanelTemplate> " +
        "<Style x:Key=\"SearchBarPlus\" TargetType=\"TextBox\"> </Style> " +
        "<Style x:Key=\"TextBoxExtend\" TargetType=\"TextBox\"> </Style> " +
        "<Style x:Key=\"ListBoxTransparent\" TargetType=\"ListBox\"> </Style> " +
        "<Style x:Key=\"ListBoxCustom\" TargetType=\"ListBox\"> </Style> " +
        "<Style x:Key=\"CirclePanelButton\" TargetType=\"Button\"> </Style> " +
        "<Geometry x:Key=\"ClockGeometry\">M498.506684 256.571517 425.359734 256.571517 425.359734 622.190635 720.166063 622.190635 720.166063 549.725206 498.506684 549.725206ZM512.714268 80.162738c-245.740844 0-445.181175 197.577914-445.181175 441.024505s199.439308 441.024505 445.181175 441.024505c245.738797 0 445.178105-197.577914 445.178105-441.024505S758.453065 80.162738 512.714268 80.162738zM512.714268 874.005824c-195.880249 0-356.145349-158.768003-356.145349-352.818581s160.2651-352.821651 356.145349-352.821651c195.877179 0 356.14228 158.77005 356.14228 352.821651S708.591447 874.005824 512.714268 874.005824z</Geometry> " +
        "<Style x:Key=\"DemoInfoSortButtonStyle\" TargetType=\"ToggleButton\"> </Style> " +
        "<Style x:Key=\"DemoGroupItemStyle\" TargetType=\"GroupItem\"> </Style> " +
        "<Style x:Key=\"Path4GeometryItem\" TargetType=\"Path\"> </Style> " +
        "<Geometry x:Key=\"LogoGeometry\">M40.493382,19.001303 C36.637875,19.06448 33.938568,21.421986 33.948524,25.271919 L34.068279,71.56765 34.066906,71.598007 34.068745,71.747833 34.18895,118.21758 C34.202225,123.35083 39.026951,129.19344 44.965271,131.2674 50.903587,133.34137 55.706783,130.86133 55.693504,125.72808 L55.589508,85.524513 93.866371,85.524513 93.950943,118.21758 C93.964218,123.35083 98.788948,129.19344 104.72726,131.2674 110.66558,133.34137 115.46878,130.86133 115.4555,125.72808 L115.33575,79.432381 115.33712,79.401993 115.33528,79.252007 115.21507,32.782413 C115.2018,27.64917 110.37708,21.806566 104.43876,19.732603 98.500435,17.658638 93.697243,20.138674 93.710518,25.271919 L93.814514,65.475487 55.537647,65.475487 55.453079,32.782413 C55.439804,27.64917 50.615082,21.806566 44.676762,19.732603 43.192181,19.214111 41.778549,18.980244 40.493382,19.001303 z M9.999999,0 L140,0 C145.52284,0 150,4.4771523 150,9.999999 L150,140 C150,145.52284 145.52284,150 140,150 L9.999999,150 C4.4771523,150 0,145.52284 0,140 L0,9.999999 C0,4.4771523 4.4771523,0 9.999999,0 z</Geometry> " +
        "<Geometry x:Key=\"CodeGeometry\">M288.752 319.495c-12.497-12.497-32.758-12.497-45.255 0L73.619 489.372a32 32 0 0 0 0 45.254l169.878 169.879c6.249 6.248 14.438 9.372 22.627 9.372s16.379-3.124 22.627-9.372c12.497-12.497 12.497-32.759 0-45.255L141.502 512l147.25-147.25c12.497-12.497 12.497-32.758 0-45.255zM950.38 489.372L780.504 319.495c-12.498-12.497-32.759-12.497-45.255 0-12.497 12.497-12.497 32.758 0 45.255L882.499 512l-147.25 147.251c-12.496 12.496-12.496 32.758 0.001 45.255 6.248 6.248 14.438 9.372 22.627 9.372s16.379-3.124 22.628-9.373l169.877-169.878c12.496-12.497 12.496-32.758-0.002-45.255zM600.417 312.63c-16.289-6.858-35.052 0.785-41.91 17.074L415.451 669.46c-6.858 16.288 0.786 35.052 17.074 41.91a31.892 31.892 0 0 0 12.404 2.517c12.488 0 24.355-7.359 29.506-19.591L617.491 354.54c6.858-16.288-0.786-35.052-17.074-41.91z</Geometry> " +
        "<Geometry x:Key=\"DocumentGeometry\">M979.478261 706.381913a44.521739 44.521739 0 0 1-11.842783 57.967304l-4.36313 2.849392-393.794783 227.862261-5.38713 4.763826a84.012522 84.012522 0 0 1-43.942957 17.808695l-8.102956 0.400696c-19.055304 0-37.309217-6.544696-52.045913-18.253913l-5.431652-4.719304-393.750261-227.862261a44.521739 44.521739 0 0 1-18.610087-56.186435l2.359652-4.630261a44.521739 44.521739 0 0 1 60.816696-16.250435l405.325913 234.540522 1.335652 1.513739 1.335652-1.513739 405.281391-234.540522a44.521739 44.521739 0 0 1 60.861218 16.250435z m0-222.608696a44.521739 44.521739 0 0 1-11.842783 57.967305l-4.36313 2.849391-393.794783 227.862261-5.38713 4.763826a84.012522 84.012522 0 0 1-43.942957 17.808696l-8.102956 0.400695c-19.055304 0-37.309217-6.544696-52.045913-18.253913l-5.431652-4.719304-393.750261-227.862261a44.521739 44.521739 0 0 1-18.610087-56.186435l2.359652-4.630261a44.521739 44.521739 0 0 1 60.816696-16.250434l405.325913 234.540521 1.335652 1.513739 1.335652-1.513739 405.281391-234.540521a44.521739 44.521739 0 0 1 60.861218 16.250434zM512 0c18.788174 0 36.864 6.099478 51.645217 17.185391l4.58574 3.739826 403.500521 199.68 5.609739 6.144c32.50087 35.439304 32.50087 89.889391 0 125.328696l-5.609739 6.144-403.500521 199.590957-4.541218 3.784347a86.238609 86.238609 0 0 1-43.675826 16.829218L512 578.782609c-18.788174 0-36.864-6.099478-51.645217-17.185392l-4.630261-3.784347L52.268522 358.221913l-5.609739-6.144a92.738783 92.738783 0 0 1 0-125.328696l5.609739-6.144L455.724522 20.925217l4.585739-3.739826c12.688696-9.48313 27.826087-15.315478 43.675826-16.829217z m0 90.37913l-3.561739 3.873392L114.064696 289.391304l394.462608 195.138783 3.517218 3.82887 3.561739-3.82887L909.935304 289.391304l-394.462608-195.138782L512 90.37913z</Geometry> " +
        "<Geometry x:Key=\"NewGeometry\">M512 0h1216a512 512 0 1 1 0 1024H512A512 512 0 1 1 512 0zM420.16 311.04V768h74.88V433.92h2.56l228.48 334.08h72.96V311.04h-75.52v329.6h-2.56l-225.92-329.6h-74.88z m468.48 0V768h340.48v-64h-265.6V565.76h239.36v-64h-239.36V375.04h254.72v-64h-329.6z m369.28 0l131.2 456.96h80l89.6-343.68h2.56l88.96 343.68h79.36l131.84-456.96h-84.48l-85.76 347.52h-2.56l-90.24-347.52h-77.44l-90.24 347.52h-2.56l-85.76-347.52h-84.48z</Geometry> " +
        "<Geometry x:Key=\"LeftGeometry\">M394.24 512L683.52 248.32c10.24-10.24 10.24-25.6 0-35.84-10.24-10.24-25.6-10.24-35.84 0l-307.2 279.04c-5.12 5.12-7.68 12.8-7.68 20.48 0 7.68 2.56 15.36 7.68 20.48l307.2 279.04c10.24 10.24 25.6 10.24 35.84 0 10.24-10.24 10.24-25.6 0-35.84L394.24 512z</Geometry> " +
        "<Geometry x:Key=\"RightGeometry\">M4.1666641,0 C5.2083321,0 6.25,0.41666794 7.0833321,1.25 L57.083331,46.666664 C57.916664,47.499998 58.33333,48.749998 58.333329,49.999998 58.33333,51.249997 57.916664,52.499997 57.083331,53.333331 L7.0833321,98.749996 C5.4166641,100.41666 2.9166641,100.41666 1.2499962,98.749996 -0.41666794,97.083328 -0.41666794,94.583328 1.2499962,92.916664 L48.333331,49.999998 1.2499962,7.0833321 C-0.41666794,5.4166641 -0.41666794,2.9166641 1.2499962,1.25 2.0833282,0.41666794 3.1249962,0 4.1666641,0 z</Geometry> " +
        "<Geometry x:Key=\"KeyboardGeometry\">M19.692308 512A25 25 0 1 1 19.692308 531.692308ZM512 303.027832C512 286.548884 524.86564 273.174666 540.717948 273.174666L598.153846 273.174666C614.006154 273.174666 626.871794 286.548884 626.871794 303.027832L626.871794 362.734167C626.871794 379.213115 614.006154 392.587333 598.153846 392.587333L540.717948 392.587333C524.86564 392.587333 512 379.213115 512 362.734167L512 303.027832 512 303.027832ZM512 482.146834 512 541.853166C512 558.332115 499.13436 571.706335 483.282052 571.706335L425.846154 571.706335C409.993846 571.706335 397.128206 558.332115 397.128206 541.853166L397.128206 482.146834C397.128206 465.667885 409.993846 452.293665 425.846154 452.293665L483.282052 452.293665C499.13436 452.293665 512 465.667885 512 482.146834L512 482.146834ZM339.692308 303.027832C339.692308 286.548884 352.557948 273.174666 368.410256 273.174666L425.846154 273.174666C441.698462 273.174666 454.564102 286.548884 454.564102 303.027832L454.564102 362.734167C454.564102 379.213115 441.698462 392.587333 425.846154 392.587333L368.410256 392.587333C352.557948 392.587333 339.692308 379.213115 339.692308 362.734167L339.692308 303.027832 339.692308 303.027832ZM167.384615 303.027832C167.384615 286.548884 180.250256 273.174666 196.102564 273.174666L253.538462 273.174666C269.390769 273.174666 282.25641 286.548884 282.25641 303.027832L282.25641 362.734167C282.25641 379.213115 269.390769 392.587333 253.538462 392.587333L196.102564 392.587333C180.250256 392.587333 167.384615 379.213115 167.384615 362.734167L167.384615 303.027832 167.384615 303.027832ZM282.25641 720.972168C282.25641 737.451116 269.390769 750.825334 253.538462 750.825334L196.102564 750.825334C180.250256 750.825334 167.384615 737.451116 167.384615 720.972168L167.384615 661.265833C167.384615 644.786885 180.250256 631.412667 196.102564 631.412667L253.538462 631.412667C269.390769 631.412667 282.25641 644.786885 282.25641 661.265833L282.25641 720.972168 282.25641 720.972168ZM310.97436 571.706335 253.538462 571.706335C237.686154 571.706335 224.820513 558.332115 224.820513 541.853166L224.820513 482.146834C224.820513 465.667885 237.686154 452.293665 253.538462 452.293665L310.97436 452.293665C326.826667 452.293665 339.692308 465.667885 339.692308 482.146834L339.692308 541.853166C339.692308 558.332115 326.826667 571.706335 310.97436 571.706335L310.97436 571.706335ZM569.435898 720.972168C569.435898 737.451116 556.570256 750.825334 540.717948 750.825334L368.410256 750.825334C352.557948 750.825334 339.692308 737.451116 339.692308 720.972168L339.692308 661.265833C339.692308 644.786885 352.557948 631.412667 368.410256 631.412667L540.717948 631.412667C556.570256 631.412667 569.435898 644.786885 569.435898 661.265833L569.435898 720.972168 569.435898 720.972168ZM569.435898 541.853166 569.435898 482.146834C569.435898 465.667885 582.301538 452.293665 598.153846 452.293665L655.589744 452.293665C671.442052 452.293665 684.307692 465.667885 684.307692 482.146834L684.307692 541.853166C684.307692 558.332115 671.442052 571.706335 655.589744 571.706335L598.153846 571.706335C582.301538 571.706335 569.435898 558.332115 569.435898 541.853166L569.435898 541.853166ZM856.615385 720.972168C856.615385 737.451116 843.749744 750.825334 827.897437 750.825334L655.589744 750.825334C639.737437 750.825334 626.871794 737.451116 626.871794 720.972168L626.871794 661.265833C626.871794 644.786885 639.737437 631.412667 655.589744 631.412667L713.02564 631.412667C728.877948 631.412667 741.74359 618.038449 741.74359 601.559501L741.74359 482.146834C741.74359 465.667885 754.609231 452.293665 770.461538 452.293665L827.897437 452.293665C843.749744 452.293665 856.615385 465.667885 856.615385 482.146834L856.615385 720.972168 856.615385 720.972168ZM856.615385 362.734167C856.615385 379.213115 843.749744 392.587333 827.897437 392.587333L713.02564 392.587333C697.173333 392.587333 684.307692 379.213115 684.307692 362.734167L684.307692 303.027832C684.307692 286.548884 697.173333 273.174666 713.02564 273.174666L827.897437 273.174666C843.749744 273.174666 856.615385 286.548884 856.615385 303.027832L856.615385 362.734167 856.615385 362.734167Z</Geometry> " +
        "<Geometry x:Key=\"ImageGeometry\">M736 224H288a64 64 0 0 0-64 64v448a64 64 0 0 0 64 64h448a64 64 0 0 0 64-64V288a64 64 0 0 0-64-64z m-416 160a64 64 0 1 1 128.032 0.032A64 64 0 0 1 320 384z m416 320a32 32 0 0 1-32 32H384a32 32 0 0 1-32-32l96-160c26.432-2.048 68.128 6.016 128 64l128-224a32 32 0 0 1 32 32v288z</Geometry> " +
        " <Geometry x:Key=\"AudioGeometry\">M627.52666696 962L550.66666696 890.47999971c116.82-92.99999971 191.09999971-230.94 191.09999971-385.2 0-146.99999971-67.61999971-279-174.96-371.57999942L643.84666667 62C770.86666638 172.99999971 850.66666667 330.2 850.66666667 505.09999971c0 182.27999971-86.58 345.42-223.13999971 456.90000029zM405.64666638 782l-76.32-77.22a227.88 227.88 0 0 0 116.40000058-199.26A228.06 228.06 0 0 0 347.86666638 317.84000029L422.86666696 242A334.37999971 334.37999971 0 0 1 550.66666696 505.52 334.62 334.62 0 0 1 405.64666638 782zM205.66666638 596.6C164.26666638 596.6 130.66666667 562.51999971 130.66666667 520.64 130.66666667 478.76000029 164.26666638 444.79999971 205.66666638 444.79999971s74.99999971 33.95999971 75.00000058 75.90000058c0 41.87999971-33.59999971 75.84000029-75.00000058 75.83999942z</Geometry> " +
        "<Geometry x:Key=\"ErrorGeometry\">M495.469714 0C224.621714 0 0 224.621714 0 495.469714c0 270.884571 224.621714 495.506286 495.469714 495.506286 270.884571 0 495.506286-224.621714 495.506286-495.506286C990.976 224.621714 766.354286 0 495.469714 0z m211.419429 634.221714c19.821714 19.821714 19.821714 46.226286 0 66.048s-46.226286 19.821714-66.048 0l-138.752-138.715428-145.334857 145.334857a51.858286 51.858286 0 0 1-72.667429 0 51.858286 51.858286 0 0 1 0-72.667429l145.334857-145.334857-138.752-138.752c-19.821714-19.821714-19.821714-46.226286 0-66.048s46.262857-19.821714 66.084572 0l138.715428 138.715429 145.371429-145.334857a51.858286 51.858286 0 0 1 72.667428 0 51.858286 51.858286 0 0 1 0 72.667428l-145.371428 145.334857 138.752 138.752z</Geometry> " +
        "<BooleanToVisibilityConverter x:Key=\"Boolean2VisibilityConverter\" /> " +
        "<Object2VisibilityConverter x:Key=\"Object2VisibilityConverter\" /> " +
                "<DrawingBrush x:Key=\"CloudDrawingBrush\" Opacity=\".1\" Viewport=\"0,0,157,157\" ViewportUnits=\"Absolute\" Stretch=\"Uniform\" TileMode=\"Tile\"><DrawingBrush.Drawing><ImageDrawing ImageSource=\"/HandyControlDemo;component/Resources/Img/cloud.png\" Rect=\"0,0,157,157\"/></DrawingBrush.Drawing></DrawingBrush> " +
        "<LinearGradientBrush x:Key=\"PrimaryBrush\" StartPoint=\"0,0\" EndPoint=\"1,0\"> " +
        "<GradientStop Color=\"#326cf3\" Offset=\"0\" /> " +
        "<GradientStop Color=\"#326cf3\" Offset=\"1\" /> " +
        "</LinearGradientBrush> " +
        "<SolidColorBrush x:Key=\"RegionBrush\" Color=\"#FFFFFFFF\" /> " +
        "<SolidColorBrush x:Key=\"SecondaryRegionBrush\" Color=\"#FFEEEEEE\" /> " +
        "<SolidColorBrush x:Key=\"ThirdlyRegionBrush\" Color=\"#FFFFFFFF\" /> " +
        "<SolidColorBrush x:Key=\"BackgroundBrush\" Color=\"#FFEEEEEE\" /> " +
        "<SolidColorBrush x:Key=\"BorderBrush\" Color=\"#FFE0E0E0\" /> " +
        "<SolidColorBrush x:Key=\"SecondaryBorderBrush\" Color=\"#FF757575\" /> " +
        "<LinearGradientBrush x:Key=\"TitleBrush\" StartPoint=\"0,0\" EndPoint=\"1,0\"> " +
        "<GradientStop Color=\"#FF326CF3\" Offset=\"0\" /> " +
        "<GradientStop Color=\"#FF326CF3\" Offset=\"1\" /> " +
        "</LinearGradientBrush> " +
        "<SolidColorBrush x:Key=\"DefaultBrush\" Color=\"#FFFFFFFF\" /> " +
        "<SolidColorBrush x:Key=\"PrimaryTextBrush\" Color=\"#FF212121\" /> " +
        "<SolidColorBrush x:Key=\"SecondaryTextBrush\" Color=\"#FF757575\" /> " +
        "<SolidColorBrush x:Key=\"ThirdlyTextBrush\" Color=\"#FFBDBDBD\" /> " +
        "<SolidColorBrush x:Key=\"ReverseTextBrush\" Color=\"#FF212121\" /> " +
        "<SolidColorBrush x:Key=\"TextIconBrush\" Color=\"#FFFFFFFF\" /> " +
        "<LinearGradientBrush x:Key=\"SuccessBrush\" StartPoint=\"0,0\" EndPoint=\"1,0\"> " +
        "<GradientStop Color=\"#FF2DB84D\" Offset=\"0\" /> " +
        "<GradientStop Color=\"#FF2DB84D\" Offset=\"1\" /> " +
        "</LinearGradientBrush> " +
        "<LinearGradientBrush x:Key=\"InfoBrush\" StartPoint=\"0,0\" EndPoint=\"1,0\"> " +
        "<GradientStop Color=\"#FF00BCD4\" Offset=\"0\" /> " +
        "<GradientStop Color=\"#FF00BCD4\" Offset=\"1\" /> " +
        "</LinearGradientBrush> " +
        "<LinearGradientBrush x:Key=\"DangerBrush\" StartPoint=\"0,0\" EndPoint=\"1,0\"> " +
        "<GradientStop Color=\"#FFDB3340\" Offset=\"0\" /> " +
        "<GradientStop Color=\"#FFDB3340\" Offset=\"1\" /> " +
        "</LinearGradientBrush> " +
        "<LinearGradientBrush x:Key=\"WarningBrush\" StartPoint=\"0,0\" EndPoint=\"1,0\"> " +
        "<GradientStop Color=\"#FFE9AF20\" Offset=\"0\" /> " +
        "<GradientStop Color=\"#FFE9AF20\" Offset=\"1\" /> " +
        "</LinearGradientBrush> " +
        "<SolidColorBrush x:Key=\"AccentBrush\" Color=\"#FFF8491E\" /> " +
        "<SolidColorBrush x:Key=\"DarkAccentBrush\" Color=\"#FFF8491E\" /> " +
        "<SolidColorBrush x:Key=\"DarkPrimaryBrush\" Color=\"#FF326CF3\" /> " +
        "<SolidColorBrush x:Key=\"DarkSuccessBrush\" Color=\"#FF2DB84D\" /> " +
        "<SolidColorBrush x:Key=\"DarkInfoBrush\" Color=\"#FF00BCD4\" /> " +
        "<SolidColorBrush x:Key=\"DarkDangerBrush\" Color=\"#FFDB3340\" /> " +
        "<SolidColorBrush x:Key=\"DarkWarningBrush\" Color=\"#FFE9AF20\" /> " +
        "<SolidColorBrush x:Key=\"DarkDefaultBrush\" Color=\"#FFF5F5F5\" /> " +
        "<SolidColorBrush x:Key=\"DarkMaskBrush\" Color=\"#20000000\" /> " +
        "<SolidColorBrush x:Key=\"DarkOpacityBrush\" Color=\"#40000000\" /> " +
        "<Style x:Key=\"LabelPrimary\" TargetType=\"Label\"> </Style> " +
        "<Style x:Key=\"LabelDanger\" TargetType=\"Label\"> </Style> " +
        "<Style x:Key=\"LabelWarning\" TargetType=\"Label\"> </Style> " +
        "<Style x:Key=\"LabelInfo\" TargetType=\"Label\"> </Style> " +
        "<Style x:Key=\"LabelSuccess\" TargetType=\"Label\"> </Style> " +
        "<Style x:Key=\"LabelDefault.Small\" TargetType=\"Label\"> </Style> " +
        "<Style x:Key=\"LabelDanger.Small\" TargetType=\"Label\"> </Style> " +
        "<Style x:Key=\"LabelWarning.Small\" TargetType=\"Label\"> </Style> " +
        "<Style x:Key=\"LabelInfo.Small\" TargetType=\"Label\"> </Style> " +
        "<Style x:Key=\"LabelPrimary.Small\" TargetType=\"Label\"> </Style> " +
        "<Style x:Key=\"LabelSuccess.Small\" TargetType=\"Label\"> </Style> " +
        "</ResourceDictionary>";
}
