using System.Security.Cryptography;
using System.Text.Json;
using DesktopWasm.Convert;
using Mono.Cecil;

namespace DesktopWasm.Convert.Tests;

/// <summary>
/// Slice-83 staged oracle for handycontrol: the lib nested
/// FreezableCollection`1/Enumerator row rides the default System.Windows.*
/// rule once the engine declares the nested struct with the live 10.0.13
/// PresentationCore shape plus version-stamped invalidation and
/// MapNestedType resolves nested types on generic outers (all verified
/// member-by-member against the upstream net10.0-windows HandyControl.dll
/// with Mono.Cecil: the lib's own Interactivity behavior system iterates
/// FreezableCollection&lt;Behavior/TriggerAction/TriggerBase/T&gt; through
/// compiler-generated foreach machinery in 8 attach/detach/invoke methods,
/// plus the engine-side Slice83FreezableEnumeratorTests and
/// MapCoversFreezableEnumeratorSlice pinning the contract). The package
/// re-packages 63 -&gt; 62 with zero new gap kinds and the lib itself now
/// converts and stages (5 staged: the 4 pre-existing images byte-identical,
/// HandyControl new with its inputSha256 receipt; the converted
/// Behavior/Trigger collections enumerate through the engine nested struct
/// at runtime, proven by the staged boot-oracle test);
/// the lib keeps its 2 absent-binary reference rows (its own live
/// System.Windows.Extensions / System.Security.Permissions references,
/// declared boundaries); ICSharpCode.AvalonEdit now converts and stages too
/// (6 staged: the 5 pre-existing images byte-identical, AvalonEdit new with
/// its inputSha256 receipt; the converted TextArea/CompletionWindow
/// composition call shapes rebind onto the engine contracts, proven by the
/// staged rebind oracle).
/// The package stays conversion-failed.
/// (Slice-93 history: the AvalonEdit composition TextCompositionEventHandler/
/// InputManager/LogicalDirection/LanguageSpecificStringDictionary scope
/// cleared 6 -&gt; 2 with zero new kinds (the handler plus the thread-local
/// InputManager.Current composition identity, the live TextComposition/
/// TextCompositionEventArgs construction shapes, the UIElement
/// PreviewTextInput/TextInput attach surface, the LogicalDirection enum and
/// the real-behavior language dictionary all ride the default
/// System.Windows.* rule; the converted TextArea/CompletionWindow call shapes
/// bind while the IME staging pipeline stays a precise boundary).
/// (Slice-92 history: the AvalonEdit device InputDevice/StylusDevice pair
/// cleared 8 -&gt; 6 with zero new kinds and the 5 staged images
/// byte-identical (both ride the default System.Windows.* rule onto the
/// engine contracts: the abstract InputDevice root with get-only Device on
/// InputEventArgs plus the sealed StylusDevice with the live MouseEventArgs
/// (MouseDevice, int[, StylusDevice]) shapes and the QueryCursor stylus
/// overload; the converted TextView.RaiseHoverEventPair construction plus
/// the CompletionWindow/PerformTextInput get_Device call shapes bind, while
/// the TextCompositionEventArgs composition ctor stayed a follow-up with
/// TextCompositionEventHandler/InputManager, now bound in slice-93).
/// (Slice-91 history: the AvalonEdit weak-event PropertyChangedEventManager
/// row cleared 9 -&gt; 8 with zero new kinds and the 5 staged images
/// byte-identical (the manager rides an explicit WindowsBase Override onto
/// the engine contract, like CollectionChangedEventManager; the converted
/// TextEditor/LineNumberMargin AddListener/RemoveListener call shapes bind,
/// while the InputDevice/StylusDevice, LanguageSpecificStringDictionary,
/// LogicalDirection, InputManager, and TextCompositionEventHandler rows stay
/// follow-ups with their own scopes).
/// (Slice-90 history: the AvalonEdit IME-suspension InputMethod contract
/// plus the FontStyleConverter/FontWeightConverter pair cleared 12 -&gt; 9
/// with zero new kinds and the 5 staged images byte-identical (all three ride
/// the default System.Windows.* rule; the converted ImeSupport
/// SetIsInputMethodSuspended call shape and the HighlightingColor/XshdColor/
/// V2Loader converter .ctor call shapes bind, while the OS IME context and
/// the XAML setter path stay precise boundaries).
/// (Slice-89 history: the AvalonEdit cursor-query QueryCursorEventArgs/Handler
/// pair cleared 14 -> 12 with zero new kinds and the 5 staged images byte-identical
/// (both ride the default System.Windows.* rule; the converted
/// SelectionMouseHandler attach/detach plus textArea_QueryCursor set_Cursor/
/// set_Handled call shapes and the TextView/VisualLineElement/VisualLineLinkText
/// OnQueryCursor overrides bind onto the engine event/slot, while hover-driven
/// cursor application stays a precise engine boundary).
/// (Slice-88 history: the AvalonEdit scrolling IScrollInfo row cleared
/// 15 -> 14 with zero new kinds and the 5 staged images byte-identical
/// (the single interface rides the default System.Windows.* rule; the
/// converted TextArea/TextView explicit implementations plus the
/// TextEditor/BackgroundGeometryBuilder/CompletionWindowBase call shapes
/// bind, while scroll delegation stays a precise engine boundary).
/// (Slice-87 history: the AvalonEdit UIA text-provider 10-row cluster
/// cleared 25 -> 15 with zero new kinds and the 5 staged images
/// byte-identical (ITextProvider/IValueProvider/ITextRangeProvider/
/// IRawElementProviderSimple plus TextUnit/TextPatternRangeEndpoint/
/// SupportedTextSelection/AutomationProperty/ValuePatternIdentifiers/
/// AutomationEvents ride the default System.Windows.* rule; the converted
/// peer/interface call shapes bind while raises stay in-engine records
/// with no OS automation tree).
/// (Slice-86 history: the AvalonEdit text-formatting 30-row core cleared
/// 55 -&gt; 25 with zero new kinds and the 5 staged images byte-identical
/// (20 System.Windows.Media.TextFormatting line-pipeline types plus the 9
/// System.Windows typography/layout enums plus TextEffectCollection ride
/// the default System.Windows.* rule; the converted source/property/embedded
/// overrides and Create/FormatLine/Draw/hit-test call shapes bind, while
/// line layout stays a precise v1 NotSupported boundary); AvalonEdit keeps
/// 23 rows: UIA providers, scrolling, cursor query, IME, font converters,
/// weak-event plumbing, input devices, language-dictionary follow-ups.
/// Slice-85 history: the AvalonEdit drag-feedback 7-row cluster cleared
/// 62 -&gt; 55 with zero new kinds and the 5 staged images byte-identical
/// (DragAction/GiveFeedback/QueryContinueDrag args+handlers plus
/// DataObjectCopying/SettingData args ride the default System.Windows.*
/// rule; UIElement gains the GiveFeedback/QueryContinueDrag attach points
/// the converted SelectionMouseHandler pattern needs, and DataObject gains
/// the Copying/SettingData routing tokens the converted copy ctors set);
/// AvalonEdit keeps 53 rows: the 2 absent-binary references plus
/// text-formatting/UIA/scroll/cursor/IME/converter follow-ups.
/// Slice-82 history: IAddChild/StyleTypedProperty/AutoToolTip cleared
/// 66 -&gt; 63; slice-81: KeySpline/IKeyFrameAnimation/Clock 69 -&gt; 66;
/// slice-80: ObjectDataProvider/TextSearch cleared 71 -&gt; 69;
/// slice-79: DataGrid x7 cleared 78 -&gt; 71; slice-78: Media3D x5
/// cleared 83 -&gt; 78; slice-76: SplashScreen/SoundPlayer 85 -&gt; 83,
/// 3 -&gt; 4 staged; slice-75: TouchEventArgs/TouchDevice 88 -&gt; 85;
/// slice-74: MouseBinding/MouseGesture/MouseAction 91 -&gt; 88 plus 82
/// retired stale rows.)
/// </summary>
public sealed class HandyControlStagedTests
{
    [Fact]
    public void Staged_handycontrol_hashes_verify()
    {
        Assert.Empty(StagedHandyControlLoader.VerifyHashes());
    }

    [Fact]
    public void Gaps_are_exactly_the_declared_remainder()
    {
        var manifest = StagedHandyControlLoader.ReadManifest();
        Assert.Equal("conversion-failed", manifest.Status);
        Assert.Equal(2, manifest.Gaps.Count);
        // No converter-failure kinds anywhere: every remaining row is a
        // plain unmapped-type, an absent binary, or a nested-type boundary.
        Assert.DoesNotContain(manifest.Gaps, g =>
            g.Kind is "rewrite-error" or "stale-framework-reference");
        // Zero mouse rows left anywhere in the closure.
        foreach (var type in new[] { "MouseBinding", "MouseGesture", "MouseAction" })
            Assert.DoesNotContain(manifest.Gaps, g => g.Detail.Contains(type, StringComparison.Ordinal));
        // Zero touch rows left anywhere in the closure (the slice-75 advance).
        foreach (var type in new[] { "TouchEventArgs", "TouchDevice" })
            Assert.DoesNotContain(manifest.Gaps, g => g.Detail.Contains(type, StringComparison.Ordinal));
        // Zero Media3D x5 rows left anywhere in the closure (slice-78).
        foreach (var type in new[]
        {
            "ContainerUIElement3D", "Vector3DCollection", "ProjectionCamera",
            "Point3DAnimation", "RayMeshGeometry3DHitTestResult",
        })
            Assert.DoesNotContain(manifest.Gaps, g => g.Detail.Contains(type, StringComparison.Ordinal));
        // Zero DataGrid x7 rows left anywhere in the closure (slice-79).
        foreach (var type in new[]
        {
            "VirtualizingPanel", "ScrollUnit", "MultiSelector",
            "DataGridRowEventArgs", "DataGridSortingEventHandler",
            "DataGridSortingEventArgs", "DataGridColumnEventArgs",
        })
            Assert.DoesNotContain(manifest.Gaps, g => g.Detail.Contains(type, StringComparison.Ordinal));
        // Zero ObjectDataProvider/TextSearch rows left anywhere in the
        // closure (the slice-80 advance).
        foreach (var type in new[] { "ObjectDataProvider", "TextSearch" })
            Assert.DoesNotContain(manifest.Gaps, g => g.Detail.Contains(type, StringComparison.Ordinal));
        // Zero KeySpline/IKeyFrameAnimation/Clock rows left anywhere in the
        // closure (the slice-81 advance).
        foreach (var type in new[] { "KeySpline", "IKeyFrameAnimation", "Clock" })
            Assert.DoesNotContain(manifest.Gaps, g => g.Detail.Contains(type, StringComparison.Ordinal));
        // Zero IAddChild/StyleTypedProperty/AutoToolTip rows left anywhere
        // in the closure (the slice-82 advance).
        foreach (var type in new[] { "IAddChild", "StyleTypedProperty", "AutoToolTip" })
            Assert.DoesNotContain(manifest.Gaps, g => g.Detail.Contains(type, StringComparison.Ordinal));
        // Zero drag-feedback rows left anywhere in the closure (this
        // slice: DragAction, both feedback/continue arg+handler pairs, and
        // both DataObject negotiation args).
        foreach (var type in new[]
        {
            "DragAction", "GiveFeedbackEventArgs", "GiveFeedbackEventHandler",
            "QueryContinueDragEventArgs", "QueryContinueDragEventHandler",
            "DataObjectCopyingEventArgs", "DataObjectSettingDataEventArgs",
        })
            Assert.DoesNotContain(manifest.Gaps, g => g.Detail.Contains(type, StringComparison.Ordinal));
        // Zero text-formatting rows left anywhere in the closure (this
        // slice: the 20 line-pipeline types plus the 9 typography/layout
        // enums plus TextEffectCollection).
        foreach (var type in new[]
        {
            "TextFormatter", "TextLine", "TextRun", "TextCharacters",
            "TextEndOfParagraph", "TextEmbeddedObject", "TextEmbeddedObjectMetrics",
            "TextSource", "TextRunProperties", "TextParagraphProperties",
            "TextRunTypographyProperties", "TextMarkerProperties", "TextBounds",
            "TextLineBreak", "CharacterBufferRange", "CharacterBufferReference",
            "CharacterHit", "TextSpan`1", "CultureSpecificCharacterBufferRange",
            "InvertAxes", "BaselineAlignment", "LineBreakCondition",
            "FontVariants", "FontNumeralStyle", "FontNumeralAlignment",
            "FontFraction", "FontEastAsianWidths", "FontEastAsianLanguage",
            "FontCapitals", "TextEffectCollection",
        })
            Assert.DoesNotContain(manifest.Gaps, g => g.Detail.Contains(type, StringComparison.Ordinal));
        // Zero UIA text-provider rows left anywhere in the closure (this
        // slice: ITextProvider/IValueProvider/ITextRangeProvider/
        // IRawElementProviderSimple plus TextUnit/TextPatternRangeEndpoint/
        // SupportedTextSelection/AutomationProperty/ValuePatternIdentifiers/
        // AutomationEvents).
        foreach (var type in new[]
        {
            "ITextProvider", "IValueProvider", "ITextRangeProvider",
            "IRawElementProviderSimple", "TextUnit", "TextPatternRangeEndpoint",
            "SupportedTextSelection", "ValuePatternIdentifiers", "AutomationEvents",
        })
            Assert.DoesNotContain(manifest.Gaps, g => g.Detail.Contains(type, StringComparison.Ordinal));
        Assert.DoesNotContain(manifest.Gaps, g =>
            g.Detail.Contains("System.Windows.Automation.AutomationProperty", StringComparison.Ordinal));
        // Zero scrolling rows left anywhere in the closure (this slice:
        // the single IScrollInfo interface; the converted TextArea/TextView
        // explicit implementations bind while scroll delegation stays a
        // precise engine boundary).
        Assert.DoesNotContain(manifest.Gaps, g =>
            g.Detail.Contains("IScrollInfo", StringComparison.Ordinal));
        // Zero cursor-query rows left anywhere in the closure (this slice:
        // QueryCursorEventArgs + QueryCursorEventHandler; the converted
        // SelectionMouseHandler attach/detach plus set_Cursor/set_Handled call
        // shapes and the OnQueryCursor overrides bind while hover-driven
        // cursor application stays a precise engine boundary).
        foreach (var type in new[]
        {
            "QueryCursorEventArgs", "QueryCursorEventHandler",
        })
            Assert.DoesNotContain(manifest.Gaps, g => g.Detail.Contains(type, StringComparison.Ordinal));
        // Zero IME/converter rows left anywhere in the closure (this slice:
        // InputMethod plus the FontStyleConverter/FontWeightConverter pair;
        // the converted ImeSupport suspend loop and the HighlightingColor/
        // XshdColor/V2Loader converter constructions bind while the OS IME
        // context stays a precise boundary).
        foreach (var type in new[]
        {
            "InputMethod",
            "FontStyleConverter", "FontWeightConverter",
        })
            Assert.DoesNotContain(manifest.Gaps, g => g.Detail.Contains(type, StringComparison.Ordinal));
        // Zero weak-event rows left anywhere in the closure (this slice:
        // PropertyChangedEventManager; the converted TextEditor/
        // LineNumberMargin AddListener/RemoveListener call shapes bind onto
        // the engine contract).
        Assert.DoesNotContain(manifest.Gaps, g =>
            g.Detail.Contains("PropertyChangedEventManager", StringComparison.Ordinal));
        // Zero device rows left anywhere in the closure (this slice: the
        // abstract InputDevice root plus the sealed StylusDevice; the
        // converted RaiseHoverEventPair construction plus the
        // CompletionWindow/PerformTextInput get_Device call shapes bind
        // onto the engine contracts).
        foreach (var type in new[]
        {
            "System.Windows.Input.InputDevice", "System.Windows.Input.StylusDevice",
        })
            Assert.DoesNotContain(manifest.Gaps, g => g.Detail.Contains(type, StringComparison.Ordinal));
        // Zero composition rows left anywhere in the closure (this slice:
        // TextCompositionEventHandler plus the InputManager composition
        // identity, LogicalDirection, and LanguageSpecificStringDictionary;
        // the converted TextArea/CompletionWindow call shapes bind while the
        // IME staging pipeline stays a precise boundary).
        foreach (var type in new[]
        {
            "TextCompositionEventHandler", "TextCompositionEventArgs",
            "LogicalDirection", "LanguageSpecificStringDictionary", "InputManager",
        })
            Assert.DoesNotContain(manifest.Gaps, g => g.Detail.Contains(type, StringComparison.Ordinal));
        // The remaining blockers stay pinned: only the 2 absent-binary
        // references (no browser path, out of scope as declared).
        foreach (var type in new[]
        {
            "System.Security.Permissions", "System.Windows.Extensions",
        })
            Assert.Contains(manifest.Gaps, g => g.Detail.Contains(type, StringComparison.Ordinal));
        // Zero stale rows from slices 60-73 anywhere in the closure.
        foreach (var type in new[]
        {
            "InputBinding", "KeyBinding", "FocusManager", "XmlLanguage",
            "TimelineCollection", "Model3DCollection",
            "TransformCollection", "ICollectionView", "CollectionViewSource",
            "Animatable", "ComponentResourceKey", "TextOptions",
            "ValueConversionAttribute", "GeneratorStatus", "HwndHost",
            "ToolBar", "DispatcherOperation",
        })
            Assert.DoesNotContain(manifest.Gaps, g => g.Detail.Contains(type, StringComparison.Ordinal));
        // The entry converts clean: the splash image maps through the
        // pack-store/decode path and the only facade reference rebinds onto
        // the engine player (the facade reference prunes as unused and
        // unresolvable). Zero entry rows remain.
        var entry = manifest.Gaps.Where(g => g.Assembly == "HandyControlDemo").ToList();
        Assert.Empty(entry);
        Assert.DoesNotContain(manifest.Gaps, g => g.Detail.Contains("SplashScreen", StringComparison.Ordinal));
        // The lib keeps its own live facade rows (HandyControl itself is
        // not staged): CAS permission calls plus an assembly-level
        // Extensions reference, both declared boundaries for their scope.
        Assert.Contains(manifest.Gaps, g =>
            g.Kind == "missing-dependency" && g.Assembly == "HandyControl" &&
            g.Detail.Contains("System.Security.Permissions", StringComparison.Ordinal));
        Assert.Contains(manifest.Gaps, g =>
            g.Kind == "missing-dependency" && g.Assembly == "HandyControl" &&
            g.Detail.Contains("System.Windows.Extensions", StringComparison.Ordinal));
        // Zero nested-type rows left anywhere in the closure (this slice:
        // the converter maps nested framework types on generic outers, so
        // the FreezableCollection`1/Enumerator row rewrites onto the engine
        // nested struct and the HandyControl lib itself converts and stages).
        Assert.DoesNotContain(manifest.Gaps, g =>
            g.Kind == "nested-type" && g.Detail.Contains("FreezableCollection`1/Enumerator", StringComparison.Ordinal));
    }

    [Fact]
    public void Gaps_split_per_assembly_with_zero_new_kinds()
    {
        var manifest = StagedHandyControlLoader.ReadManifest();
        var byAssembly = manifest.Gaps.GroupBy(g => g.Assembly)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        Assert.False(byAssembly.ContainsKey("HandyControlDemo"));
        Assert.False(byAssembly.ContainsKey("ICSharpCode.AvalonEdit"));
        Assert.Equal(2, byAssembly["HandyControl"]);
        Assert.Equal(1, byAssembly.Count);
        Assert.DoesNotContain(manifest.Gaps, g =>
            g.Kind is "rewrite-error" or "stale-framework-reference");
    }

    [Fact]
    public void Staged_set_unchanged_with_zero_orphans()
    {
        var manifest = StagedHandyControlLoader.ReadManifest();
        Assert.Equal(
            ["GalaSoft.MvvmLight", "HandyControl", "HandyControlDemo", "HandyControlDemoCode", "ICSharpCode.AvalonEdit", "Newtonsoft.Json"],
            manifest.Assemblies.Order());
        Assert.Equal(
            StagedHandyControlLoader.StagedFiles().Order(),
            manifest.RawAssemblies.Select(a => a.File).Order());
    }

    [Fact]
    public void Converted_entry_carries_no_framework_splash_or_sound_references()
    {
        // The converter rebound both slice rows onto the engine: the staged
        // entry keeps no framework-scoped SplashScreen/SoundPlayer reference
        // (App.OpenSplashScreen + ChatBoxViewModel.ReadMessage bind the
        // engine types), and the facade assembly reference pruned away.
        using var image = StagedHandyControlLoader.LoadAssembly("HandyControlDemo");
        Assert.DoesNotContain(image.MainModule.GetTypeReferences(), t =>
            t.Scope.Name is "PresentationFramework" or "PresentationCore" or "WindowsBase" or "System.Xaml" or "System.Windows.Extensions" &&
            t.Name is "SplashScreen" or "SoundPlayer");
        Assert.DoesNotContain(image.MainModule.AssemblyReferences, r =>
            string.Equals(r.Name, "System.Windows.Extensions", StringComparison.Ordinal));
        var engineRefs = image.MainModule.GetTypeReferences()
            .Where(t => string.Equals(t.Scope.Name, "DesktopWasm.Wpf", StringComparison.Ordinal) &&
                t.Name is "SplashScreen" or "SoundPlayer")
            .Select(t => t.Namespace + "." + t.Name)
            .Distinct(StringComparer.Ordinal)
            .Order()
            .ToList();
        Assert.Equal(["DesktopWasm.Wpf.SplashScreen", "System.Media.SoundPlayer"], engineRefs);
    }

    [Fact]
    public void Converted_lib_enumerator_refs_rebind_onto_engine()
    {
        // This slice: the converted HandyControl lib stages, and its
        // Interactivity foreach machinery (get_Current/MoveNext calls,
        // constrained prefixes and enumerator locals over
        // FreezableCollection`1/Enumerator instantiations) rebinds onto the
        // engine nested struct — every such reference in live code scopes to
        // the engine, none to a framework assembly.
        using var image = StagedHandyControlLoader.LoadAssembly("HandyControl");
        var hits = new List<string>();
        foreach (var type in image.MainModule.Types)
            CollectNestedEnumeratorRefs(type, hits);
        Assert.NotEmpty(hits);
        Assert.All(hits, h => Assert.Contains("DesktopWasm.Wpf", h, StringComparison.Ordinal));
        Assert.Contains(hits, h => h.Contains("Behavior", StringComparison.Ordinal));
        Assert.Contains(hits, h => h.Contains("TriggerAction", StringComparison.Ordinal));
        Assert.Contains(hits, h => h.Contains("TriggerBase", StringComparison.Ordinal));

        static void CollectNestedEnumeratorRefs(TypeDefinition type, List<string> hits)
        {
            foreach (var method in type.Methods)
            {
                if (!method.HasBody) continue;
                foreach (var instruction in method.Body.Instructions)
                {
                    TypeReference? reference = instruction.Operand switch
                    {
                        MethodReference called => called.DeclaringType,
                        FieldReference field => field.DeclaringType,
                        TypeReference typeOperand => typeOperand,
                        _ => null,
                    };
                    if (reference?.FullName.Contains("FreezableCollection`1/Enumerator", StringComparison.Ordinal) == true)
                        hits.Add($"{type.FullName}::{method.Name} -> {reference.Scope.Name}:{reference.FullName}");
                }
                foreach (var variable in method.Body.Variables)
                {
                    if (variable.VariableType.FullName.Contains("FreezableCollection`1/Enumerator", StringComparison.Ordinal))
                        hits.Add($"{type.FullName}::{method.Name} local -> {variable.VariableType.Scope.Name}:{variable.VariableType.FullName}");
                }
            }
            foreach (var nested in type.NestedTypes)
                CollectNestedEnumeratorRefs(nested, hits);
        }
    }

    [Fact]
    public void Converted_images_carry_no_framework_mouse_references()
    {
        // The converter rebound the trio onto the engine: none of the
        // staged images keeps a framework-scoped MouseBinding/MouseGesture/
        // MouseAction reference (this pins the staged closure including the
        // newly staged HandyControl lib).
        foreach (var name in new[] { "GalaSoft.MvvmLight", "HandyControl", "HandyControlDemoCode", "Newtonsoft.Json" })
        {
            using var image = StagedHandyControlLoader.LoadAssembly(name);
            Assert.DoesNotContain(image.MainModule.GetTypeReferences(), t =>
                t.Scope.Name is "PresentationFramework" or "PresentationCore" or "WindowsBase" or "System.Xaml" &&
                t.Name is "MouseBinding" or "MouseGesture" or "MouseAction");
        }
    }

    [Fact]
    public void Converted_images_carry_no_framework_touch_references()
    {
        // The converter rebound the pair onto the engine: none of the
        // staged images keeps a framework-scoped TouchEventArgs/TouchDevice
        // reference (this pins the staged closure including the newly
        // staged HandyControl lib).
        foreach (var name in new[] { "GalaSoft.MvvmLight", "HandyControl", "HandyControlDemoCode", "Newtonsoft.Json" })
        {
            using var image = StagedHandyControlLoader.LoadAssembly(name);
            Assert.DoesNotContain(image.MainModule.GetTypeReferences(), t =>
                t.Scope.Name is "PresentationFramework" or "PresentationCore" or "WindowsBase" or "System.Xaml" &&
                t.Name is "TouchEventArgs" or "TouchDevice");
        }
    }

    [Fact]
    public void Converted_avalonedit_composition_refs_rebind_onto_engine()
    {
        // This slice: the converted AvalonEdit image stages, and its
        // TextArea/CompletionWindow composition call shapes (handler news and
        // Invoke, InputManager.get_Current, TextComposition/TextCompositionEventArgs
        // news, PreviewTextInput attach/detach, RaiseEventPair token loads)
        // rebind onto the engine — every such reference in live code scopes to
        // the engine, none to a framework assembly. The orphaned
        // LanguageSpecificStringDictionary TypeRef resolves through the same
        // default rule.
        using var image = StagedHandyControlLoader.LoadAssembly("ICSharpCode.AvalonEdit");
        foreach (var scope in new[] { "PresentationFramework", "PresentationCore", "WindowsBase", "System.Xaml" })
        {
            Assert.DoesNotContain(image.MainModule.GetTypeReferences(), t =>
                string.Equals(t.Scope.Name, scope, StringComparison.Ordinal) &&
                t.Name is "TextCompositionEventHandler" or "TextCompositionEventArgs" or
                    "TextComposition" or "InputManager" or "LogicalDirection" or
                    "LanguageSpecificStringDictionary");
        }
        var engineRefs = image.MainModule.GetTypeReferences()
            .Where(t => string.Equals(t.Scope.Name, "DesktopWasm.Wpf", StringComparison.Ordinal) &&
                t.Name is "TextCompositionEventHandler" or "TextCompositionEventArgs" or
                    "TextComposition" or "InputManager" or "LogicalDirection" or
                    "LanguageSpecificStringDictionary")
            .Select(t => t.Namespace + "." + t.Name)
            .Distinct(StringComparer.Ordinal)
            .Order()
            .ToList();
        Assert.Contains("DesktopWasm.Wpf.TextCompositionEventHandler", engineRefs);
        Assert.Contains("DesktopWasm.Wpf.TextCompositionEventArgs", engineRefs);
        Assert.Contains("DesktopWasm.Wpf.TextComposition", engineRefs);
        Assert.Contains("DesktopWasm.Wpf.InputManager", engineRefs);
        Assert.Contains("DesktopWasm.Wpf.LogicalDirection", engineRefs);
        Assert.Contains("DesktopWasm.Wpf.LanguageSpecificStringDictionary", engineRefs);
        var hits = new List<string>();
        var tokenLoads = new List<string>();
        foreach (var type in image.MainModule.Types)
            CollectCompositionMemberRefs(type, hits, tokenLoads);
        Assert.NotEmpty(hits);
        Assert.All(hits, h => Assert.Contains("DesktopWasm.Wpf", h, StringComparison.Ordinal));
        Assert.Contains(hits, h => h.Contains("TextArea", StringComparison.Ordinal));
        Assert.Contains(hits, h => h.Contains("CompletionWindow", StringComparison.Ordinal));
        // The RaiseEventPair token loads (ldsfld PreviewTextInputEvent/
        // TextInputEvent) rebind onto the engine UIElement fields.
        Assert.NotEmpty(tokenLoads);
        Assert.All(tokenLoads, h => Assert.Contains("DesktopWasm.Wpf", h, StringComparison.Ordinal));

        static void CollectCompositionMemberRefs(
            TypeDefinition type, List<string> hits, List<string> tokenLoads)
        {
            foreach (var method in type.Methods)
            {
                if (!method.HasBody) continue;
                foreach (var instruction in method.Body.Instructions)
                {
                    if (instruction.Operand is FieldReference field &&
                        field.Name is "TextInputEvent" or "PreviewTextInputEvent")
                    {
                        tokenLoads.Add(
                            $"{type.FullName}::{method.Name} -> {field.DeclaringType.Scope.Name}:{field.DeclaringType.FullName}.{field.Name}");
                        continue;
                    }
                    TypeReference? reference = instruction.Operand switch
                    {
                        MethodReference called => called.DeclaringType,
                        FieldReference _ => null,
                        TypeReference typeOperand => typeOperand,
                        _ => null,
                    };
                    // Post-conversion the member refs scope to the engine
                    // (the converter rewrote scope and namespace, names kept).
                    if (reference?.Name is "TextCompositionEventHandler" or
                        "TextCompositionEventArgs" or "TextComposition" or "InputManager" or
                        "LogicalDirection")
                        hits.Add($"{type.FullName}::{method.Name} -> {reference.Scope.Name}:{reference.FullName}");
                }
            }
            foreach (var nested in type.NestedTypes)
                CollectCompositionMemberRefs(nested, hits, tokenLoads);
        }
    }

    [Fact]
    public void Input_hashes_match_the_upstream_build()
    {
        // Receipt chain: every staged image records the sha256 of the exact
        // upstream binary it was converted from (net10.0-windows, the newest
        // build output the live manifest was packaged from).
        var root = FindBuildDir();
        var manifest = StagedHandyControlLoader.ReadManifest();
        var inputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["GalaSoft.MvvmLight"] = Path.Combine(root, "GalaSoft.MvvmLight.dll"),
            ["HandyControl"] = Path.Combine(root, "HandyControl.dll"),
            ["HandyControlDemo"] = Path.Combine(root, "HandyControlDemo.dll"),
            ["HandyControlDemoCode"] = Path.Combine(root, "HandyControlDemoCode.dll"),
            ["Newtonsoft.Json"] = Path.Combine(root, "Newtonsoft.Json.dll"),
            ["ICSharpCode.AvalonEdit"] = Path.Combine(root, "ICSharpCode.AvalonEdit.dll"),
        };
        foreach (var (name, upstream) in inputs)
        {
            Assert.True(File.Exists(upstream), $"upstream binary missing: {upstream}");
            using var sha = SHA256.Create();
            var actual = System.Convert.ToHexString(sha.ComputeHash(File.ReadAllBytes(upstream)));
            Assert.Equal(manifest.RawAssemblies.Single(a => a.Name == name).InputSha256, actual);
        }
        Assert.Equal("e9ad3d701b90e724399a413569fbd7f35a141053", manifest.SourceCommit);
        Assert.Equal("wpf-convert-v1", manifest.MapVersion);
        Assert.Equal("HandyControlDemo", manifest.EntryAssembly);
    }

    private static string FindBuildDir()
    {
        // Receipt chain: the packager converts the pinned checkout tree
        // (targets/.../HandyControlDemo_Net_GE45/bin/Release/net10.0-windows,
        // the newest upstream build output), so the input hashes are proven
        // against those files.
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "targets", "studio-inputs", "handycontrol", "src", "Net_GE45", "HandyControlDemo_Net_GE45", "bin", "Release", "net10.0-windows")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return Path.Combine(directory!.FullName, "targets", "studio-inputs", "handycontrol", "src",
            "Net_GE45", "HandyControlDemo_Net_GE45", "bin", "Release", "net10.0-windows");
    }

    private sealed record StagedGap(string Assembly, string Kind, string Detail);
    private sealed record StagedAssembly(string Name, string Sha256, string InputSha256, string File);
    private sealed record StagedManifest(string Status, List<StagedGap> Gaps, List<string> Assemblies, List<StagedAssembly> RawAssemblies, string? SourceCommit, string? MapVersion, string? EntryAssembly);

    private static class StagedHandyControlLoader
    {
        public static List<string> VerifyHashes()
        {
            var failures = new List<string>();
            foreach (var (_, file, bytes, expected, _) in ReadStaged())
            {
                var actual = System.Convert.ToHexString(SHA256.HashData(bytes));
                if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                    failures.Add($"hash mismatch in staged '{file}'");
            }
            return failures;
        }

        public static List<string> StagedFiles()
        {
            var apps = AppsDir();
            return Directory.GetFiles(apps, "*.bin").Select(f => Path.GetFileName(f)!).ToList();
        }

        public static StagedManifest ReadManifest()
        {
            var apps = AppsDir();
            using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(apps, "app-manifest.json")));
            var root = document.RootElement;
            var gaps = new List<StagedGap>();
            foreach (var gap in root.GetProperty("gaps").EnumerateArray())
                gaps.Add(new StagedGap(
                    gap.GetProperty("assembly").GetString()!,
                    gap.GetProperty("kind").GetString()!,
                    gap.GetProperty("detail").GetString()!));
            var assemblies = new List<string>();
            var raw = new List<StagedAssembly>();
            foreach (var assembly in root.GetProperty("assemblies").EnumerateArray())
            {
                assemblies.Add(assembly.GetProperty("name").GetString()!);
                raw.Add(new StagedAssembly(
                    assembly.GetProperty("name").GetString()!,
                    assembly.GetProperty("sha256").GetString()!,
                    assembly.GetProperty("inputSha256").GetString()!,
                    assembly.GetProperty("file").GetString()!));
            }
            return new StagedManifest(
                root.GetProperty("status").GetString()!, gaps, assemblies, raw,
                root.TryGetProperty("sourceCommit", out var commit) ? commit.GetString() : null,
                root.TryGetProperty("mapVersion", out var map) ? map.GetString() : null,
                root.TryGetProperty("entryAssembly", out var entry) ? entry.GetString() : null);
        }

        public static AssemblyDefinition LoadAssembly(string name)
        {
            var images = ReadStaged().ToDictionary(e => e.name, e => e.bytes, StringComparer.OrdinalIgnoreCase);
            if (!images.ContainsKey(name))
                throw new InvalidOperationException($"staged image missing: {name}");
            return AssemblyDefinition.ReadAssembly(new MemoryStream(images[name]));
        }

        private static List<(string name, string file, byte[] bytes, string expected, string input)> ReadStaged()
        {
            var apps = AppsDir();
            using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(apps, "app-manifest.json")));
            var entries = new List<(string, string, byte[], string, string)>();
            foreach (var assembly in document.RootElement.GetProperty("assemblies").EnumerateArray())
            {
                var file = assembly.GetProperty("file").GetString()!;
                var expected = assembly.GetProperty("sha256").GetString()!;
                var input = assembly.GetProperty("inputSha256").GetString()!;
                var bytes = File.ReadAllBytes(Path.Combine(apps, file));
                entries.Add((assembly.GetProperty("name").GetString()!, file, bytes, expected, input));
            }
            return entries;
        }

        private static string AppsDir()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "src", "DesktopWasm.BrowserHost", "wwwroot", "apps", "handycontrol", "app-manifest.json")))
                directory = directory.Parent;
            Assert.NotNull(directory);
            return Path.Combine(directory!.FullName, "src", "DesktopWasm.BrowserHost", "wwwroot", "apps", "handycontrol");
        }
    }
}
