using System.Security.Cryptography;
using System.Text.Json;
using DesktopWasm.Convert;
using Mono.Cecil;

namespace DesktopWasm.Convert.Tests;

/// <summary>
/// Slice-69 staged oracle for material-design: the MaterialDesignDemo entry
/// prerequisites (Fonts, XmlLanguage, ScrollBar/Track/ScrollEvent*,
/// CustomPopupPlacement*/PopupPrimaryAxis, ExceptionValidationRule) plus
/// the System.Windows.Presentation-facade DispatcherExtensions redirect
/// clear 37 live rows (142 -&gt; 106) and stage two more libraries
/// (MaterialDesignDemo.Shared, BluwolfIcons), with zero slice-gap rows left
/// and zero new gap kinds introduced. Slice-70 extends the same oracle: the
/// theme/behavior closure (automation peers + PatternInterface, TextSelection
/// over TextRange/TextPointer, GridView/GridViewColumn(+Collection)/ViewBase,
/// Animatable-based keyframes/collections/groups/easings, NameScope/INameScope,
/// BlurEffect, SkewTransform) clears 35 more rows (106 -&gt; 71) with zero
/// slice-gap rows left and zero new gap kinds. Slice-71 extends it again: the
/// visual-state cluster (VisualStateManager/VisualState/VisualStateGroup/
/// VisualTransition/VisualStateChangedEventArgs/TemplateVisualStateAttribute
/// plus the FreezableCollection concrete lists) clears 8 more rows
/// (71 -&gt; 63) with zero slice-gap rows left and zero new gap kinds; only
/// the two nested FreezableCollection Enumerator rows remain, on the declared
/// nested-type boundary. Slice-72 clears the 40-row weak-event/columns/
/// media3D/keyframe/manipulation/shader remainder (63 -&gt; 23) with zero
/// slice-gap rows left and zero new gap kinds; the supporting CacheMode and
/// ExceptionRoutedEventArgs ride along as engine-declared signature types.
/// The entry stays conversion-failed on the declared configuration boundary
/// (the static ConfigurationManager facade has no browser path), and the
/// remaining rows (DatePicker/ContextMenu/Adorned chrome, color/converter
/// rows, navigation/annotation, the two nested FreezableCollection
/// Enumerators, the absent facade binary) stay declared boundaries.
/// Slice-73 clears the 19-row date-picker/context-menu/converter/focus/
/// size/resource/weak-manager/localization/annotation/navigation remainder
/// (23 -&gt; 4) with zero slice-gap rows left and zero new gap kinds, and
/// stages MaterialDesignColors (the converted swatch cctors bind the engine
/// ColorConverter). The entry stays conversion-failed on the declared
/// configuration boundary (the static ConfigurationManager facade has no
/// browser path); the two nested FreezableCollection Enumerator rows stay on
/// the declared nested-type boundary and the absent facade binary stays a
/// declared missing-dependency.
/// Slice-84 re-packages material-design against the slice-83 converter with
/// zero engine work (4 -&gt; 1, 6 -&gt; 8 staged): the two nested Enumerator
/// rows clear through the generic-outer MapNestedType resolution, and the
/// Themes facade missing-dependency clears through the existing slice-69
/// DispatcherExtensions redirect (the lone upstream facade-scoped member
/// use, DispatcherExtensions::Invoke, rewrites onto the engine, so the
/// converted Themes image drops the System.Windows.Presentation reference).
/// The entry stays conversion-failed on the declared configuration boundary
/// (the static ConfigurationManager facade has no browser path).
/// Converted-state oracle: a later re-package against the current converter
/// converts the entry too (1 -&gt; 0 gaps, 8 -&gt; 11 staged) with zero
/// engine work. The entry's lone ConfigurationManager row clears through the
/// explicit WpfTypeMap redirect onto the engine System.Configuration
/// adapter (AppSettings only, values seeded at package time from the real
/// upstream MaterialDesignDemo.dll.config &lt;appSettings&gt; block into the
/// converted image's __AppSettings.json resource, served at boot by
/// WpfAppBoot): the converted entry keeps no
/// System.Configuration.ConfigurationManager assembly reference and embeds
/// the seed, so the facade row is gone rather than swallowed. The same
/// re-package stages ShowMeTheXAML.AvalonEdit and ICSharpCode.AvalonEdit
/// (both convert clean); the four superseded hash-named images are pruned,
/// so the staged directory holds exactly the 11 manifested images.
/// </summary>
public sealed class MaterialDesignStagedTests
{
    [Fact]
    public void Staged_material_design_hashes_verify()
    {
        Assert.Empty(StagedMaterialDesignLoader.VerifyHashes());
    }

    [Fact]
    public void Gaps_are_exactly_the_declared_remainder()
    {
        var manifest = StagedMaterialDesignLoader.ReadManifest();
        // Converted-state oracle: the entry converts through the engine
        // ConfigurationManager adapter redirect, so no gaps remain at all.
        Assert.Equal("converted", manifest.Status);
        Assert.Empty(manifest.Gaps);
        Assert.Contains("MaterialDesignDemo", manifest.Assemblies);
        // Slice-84: both nested Enumerator rows cleared through the slice-83
        // generic-outer MapNestedType resolution, so no nested-type gaps
        // remain anywhere; the Themes facade missing-dependency cleared
        // through the existing slice-69 DispatcherExtensions redirect (the
        // converted Themes image drops the System.Windows.Presentation
        // reference), so no missing-dependency gaps remain either.
        Assert.DoesNotContain(manifest.Gaps, g => g.Kind == "nested-type");
        Assert.DoesNotContain(manifest.Gaps, g => g.Kind == "missing-dependency");
        // No converter-failure kinds anywhere (zero gaps total).
        Assert.DoesNotContain(manifest.Gaps, g =>
            g.Kind is "rewrite-error" or "stale-framework-reference");
        // Zero slice-gap rows left (entry, themes, colors, behaviors).
        string[] sliceTypes =
        [
            "Fonts", "XmlLanguage", "ScrollBar", "ScrollEvent", "Track",
            "CustomPopupPlacement", "PopupPrimaryAxis", "ExceptionValidationRule",
            "CollectionViewSource", "ICollectionView", "DataGridSelectionUnit",
            "ValueConversionAttribute", "ComponentResourceKey", "PopupAnimation",
            "FrameworkElementFactory", "MarkupExtensionReturnTypeAttribute",
            "DependencyPropertyHelper", "DispatcherOperation", "DispatcherExtensions",
            "AutomationPeer", "PatternInterface", "AutomationControlType",
            "TextSelection", "TextRange", "TextPointer",
            "GridView", "ViewBase",
            "Animatable", "TimelineGroup", "ParallelTimeline",
            "DoubleAnimationUsingKeyFrames", "DoubleKeyFrame", "DoubleKeyFrameCollection",
            "DiscreteDoubleKeyFrame", "LinearDoubleKeyFrame", "EasingDoubleKeyFrame",
            "SineEase", "CubicEase",
            "NameScope", "INameScope",
            "BlurEffect", "KernelType", "SkewTransform",
            "VisualStateManager", "VisualStateGroup", "VisualState",
            "VisualTransition", "VisualStateChangedEventArgs",
            "TemplateVisualStateAttribute",
            "SpellCheck", "SpellingError",
            "ParserContext", "XmlnsDictionary",
            "CollectionChangedEventManager",
            "ItemsChangedEventArgs", "ItemsChangedEventHandler",
            "RequestBringIntoViewEventHandler",
            "DataGridComboBoxColumn", "DataGridAutoGeneratingColumnEventArgs",
            "DataGridCheckBoxColumn", "DataGridTemplateColumn",
            "MediaElement", "LayoutInformation",
            "ThicknessAnimationUsingKeyFrames", "ThicknessKeyFrameCollection",
            "ThicknessKeyFrame",
            "Int32AnimationUsingKeyFrames", "Int32KeyFrameCollection",
            "Int32KeyFrame", "Int32Animation",
            "PointAnimation",
            "ManipulationModes", "ManipulationStartingEventArgs",
            "ManipulationInertiaStartingEventArgs", "ManipulationVelocities",
            "InertiaTranslationBehavior", "InertiaRotationBehavior",
            "ShaderEffect", "SamplingMode", "ClockState",
            "Quaternion", "QuaternionRotation3D", "Viewport2DVisual3D",
            "Transform3DCollection", "Model3DGroup", "Model3DCollection",
            "CaptureMode", "CacheMode", "ExceptionRoutedEventArgs",
            "DatePickerTextBox", "DatePickerFormat",
            "ContextMenuEventHandler", "ContextMenuEventArgs",
            "AdornedElementPlaceholder", "CachingHint",
            "ColorConverter", "FocusManager", "SizeChangedInfo",
            "StaticResourceExtension", "WeakEventManager",
            "LocalizabilityAttribute", "LocalizationCategory",
            "Annotation", "NavigationService",
            "TimelineCollection", "TransformCollection",
            "FreezableCollection", "DispatcherExtensions",
        ];
        foreach (var type in sliceTypes)
            Assert.DoesNotContain(manifest.Gaps, g => g.Detail.Contains(type, StringComparison.Ordinal));
        // Slice-84 advance pins: no gap names the nested Enumerator shape or
        // the facade binary, and no gap names the converted Themes/Behaviors
        // images at all (both convert and stage now).
        Assert.DoesNotContain(manifest.Gaps, g =>
            g.Detail.Contains("Enumerator", StringComparison.Ordinal));
        Assert.DoesNotContain(manifest.Gaps, g =>
            g.Detail.Contains("System.Windows.Presentation", StringComparison.Ordinal));
        Assert.DoesNotContain(manifest.Gaps, g =>
            g.Assembly is "MaterialDesignThemes.Wpf" or "Microsoft.Xaml.Behaviors");
    }

    [Fact]
    public void Staged_set_gains_themes_and_behaviors_with_zero_orphans()
    {
        var manifest = StagedMaterialDesignLoader.ReadManifest();
        // Converted-state oracle: the entry stages alongside the
        // ShowMeTheXAML.AvalonEdit/ICSharpCode.AvalonEdit closure (11 total,
        // zero orphans: StagedFiles must equal the manifested set exactly).
        Assert.Equal(
            ["BluwolfIcons", "CommunityToolkit.Mvvm", "ICSharpCode.AvalonEdit", "MaterialDesignColors", "MaterialDesignDemo", "MaterialDesignDemo.Shared", "MaterialDesignThemes.Wpf", "Microsoft.Xaml.Behaviors", "ShowMeTheXAML", "ShowMeTheXAML.AvalonEdit", "System.CommandLine"],
            manifest.Assemblies.Order());
        Assert.Equal(
            StagedMaterialDesignLoader.StagedFiles().Order(),
            manifest.RawAssemblies.Select(a => a.File).Order());
    }

    [Fact]
    public void Gaps_split_per_assembly_with_zero_new_kinds()
    {
        var manifest = StagedMaterialDesignLoader.ReadManifest();
        // Converted-state oracle: zero gaps, so zero per-assembly groups and
        // zero converter-failure kinds anywhere.
        Assert.Empty(manifest.Gaps);
        Assert.DoesNotContain(manifest.Gaps, g =>
            g.Kind is "rewrite-error" or "stale-framework-reference");
    }

    [Fact]
    public void Converted_images_carry_no_framework_slice_references()
    {
        // The converter rebound every slice type onto the engine: the staged
        // images keep no framework-scoped reference to any of them (mirrors
        // the AvalonDock closure pins). The converted entry stages now, so
        // it is audited too.
        foreach (var name in new[] { "MaterialDesignDemo", "MaterialDesignDemo.Shared", "BluwolfIcons", "MaterialDesignThemes.Wpf", "Microsoft.Xaml.Behaviors" })
        {
            using var image = StagedMaterialDesignLoader.LoadAssembly(name);
            Assert.DoesNotContain(image.MainModule.GetTypeReferences(), t =>
                t.Scope.Name is "PresentationFramework" or "PresentationCore" or "WindowsBase" or "System.Xaml" or "System.Windows.Presentation" &&
                t.Name is "Fonts" or "XmlLanguage" or "ScrollBar" or "Track" or
                    "ScrollEventArgs" or "ScrollEventHandler" or "CustomPopupPlacement" or
                    "CustomPopupPlacementCallback" or "ExceptionValidationRule" or "DispatcherExtensions" or
                    "Animatable" or "TimelineGroup" or "ParallelTimeline" or
                    "DoubleAnimationUsingKeyFrames" or "DoubleKeyFrame" or "DoubleKeyFrameCollection" or
                    "DiscreteDoubleKeyFrame" or "LinearDoubleKeyFrame" or "EasingDoubleKeyFrame" or
                    "SineEase" or "CubicEase" or "BlurEffect" or "KernelType" or "SkewTransform" or
                    "VisualStateManager" or "VisualState" or "VisualStateGroup" or
                    "VisualTransition" or "VisualStateChangedEventArgs" or
                    "TemplateVisualStateAttribute" or "FreezableCollection" or
                    "SpellCheck" or "SpellingError" or
                    "ParserContext" or "XmlnsDictionary" or
                    "CollectionChangedEventManager" or
                    "ItemsChangedEventArgs" or "ItemsChangedEventHandler" or
                    "RequestBringIntoViewEventHandler" or
                    "DataGridComboBoxColumn" or "DataGridAutoGeneratingColumnEventArgs" or
                    "DataGridCheckBoxColumn" or "DataGridTemplateColumn" or
                    "MediaElement" or "LayoutInformation" or
                    "ThicknessAnimationUsingKeyFrames" or "ThicknessKeyFrameCollection" or
                    "ThicknessKeyFrame" or
                    "Int32AnimationUsingKeyFrames" or "Int32KeyFrameCollection" or
                    "Int32KeyFrame" or "Int32Animation" or
                    "PointAnimation" or "ClockState" or
                    "Quaternion" or "QuaternionRotation3D" or "Viewport2DVisual3D" or
                    "Transform3DCollection" or "Model3DGroup" or "Model3DCollection" or
                    "CaptureMode" or "CacheMode" or
                    "ManipulationModes" or "ManipulationStartingEventArgs" or
                    "ManipulationInertiaStartingEventArgs" or "ManipulationVelocities" or
                    "InertiaTranslationBehavior" or "InertiaRotationBehavior" or
                    "ShaderEffect" or "SamplingMode" or "ExceptionRoutedEventArgs" or
                    "NameScope" or "INameScope" or "GridView" or "GridViewColumn" or
                    "GridViewColumnCollection" or "ViewBase" or "TextSelection" or "TextRange" or
                    "TextPointer" or "PatternInterface" or "AutomationControlType" or
                    "ItemAutomationPeer" or "ItemsControlAutomationPeer" or "SelectorAutomationPeer" or
                    "ListBoxItemAutomationPeer" or "ListViewAutomationPeer");
        }
        // Slice-73: MaterialDesignColors converts clean and stages (its 4
        // icon/color rows are engine-scoped now), so no Colors gap rows
        // remain at all.
        var colors = StagedMaterialDesignLoader.ReadManifest();
        Assert.DoesNotContain(colors.Gaps, g => g.Assembly == "MaterialDesignColors");
    }

    [Fact]
    public void Converted_themes_image_drops_the_presentation_facade()
    {
        // Slice-84 positive pin on the facade advance: the converted Themes
        // image carries no System.Windows.Presentation reference of any kind
        // (the lone upstream facade-scoped member use,
        // DispatcherExtensions::Invoke, rewrites onto the engine), and the
        // converted Behaviors image never carried one.
        foreach (var name in new[] { "MaterialDesignThemes.Wpf", "Microsoft.Xaml.Behaviors" })
        {
            using var image = StagedMaterialDesignLoader.LoadAssembly(name);
            Assert.DoesNotContain(image.MainModule.AssemblyReferences, r =>
                r.Name == "System.Windows.Presentation");
            Assert.DoesNotContain(image.MainModule.GetTypeReferences(), t =>
                t.Scope.Name == "System.Windows.Presentation");
        }
    }

    [Fact]
    public void Converted_entry_redirects_config_onto_engine_with_real_seed()
    {
        // Positive pin on the converted-state advance: the entry gap is gone
        // because the facade reference rewrites onto the engine adapter, not
        // because it was dropped. The converted entry keeps no
        // System.Configuration.ConfigurationManager assembly reference, and
        // it embeds the __AppSettings.json seed written from the real
        // upstream .dll.config <appSettings> block.
        using var image = StagedMaterialDesignLoader.LoadAssembly("MaterialDesignDemo");
        Assert.DoesNotContain(image.MainModule.AssemblyReferences, r =>
            r.Name == "System.Configuration.ConfigurationManager");
        Assert.Contains(image.MainModule.Resources, r =>
            r.Name == "__AppSettings.json");
    }

    [Fact]
    public void Converted_themes_behaviors_enumerator_refs_rebind_onto_engine()
    {
        // Slice-84 positive pin on the nested advance: the converted
        // Themes/Behaviors images stage, and their Interactivity foreach
        // machinery (get_Current/MoveNext calls, constrained prefixes and
        // enumerator locals over FreezableCollection`1/Enumerator
        // instantiations, including the generic-parameter form) rebinds onto
        // the engine nested struct â€” every such reference in live code
        // scopes to the engine, none to a framework assembly (mirrors the
        // slice-83 HandyControl pin).
        var hitsByImage = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var name in new[] { "MaterialDesignThemes.Wpf", "Microsoft.Xaml.Behaviors" })
        {
            using var image = StagedMaterialDesignLoader.LoadAssembly(name);
            var hits = new List<string>();
            foreach (var type in image.MainModule.Types)
                CollectNestedEnumeratorRefs(type, hits);
            hitsByImage[name] = hits;
        }
        Assert.All(hitsByImage.Values, hits => Assert.NotEmpty(hits));
        Assert.All(hitsByImage.Values.SelectMany(h => h),
            h => Assert.Contains("DesktopWasm.Wpf", h, StringComparison.Ordinal));
        Assert.Contains(hitsByImage["MaterialDesignThemes.Wpf"],
            h => h.Contains("Behavior", StringComparison.Ordinal));
        Assert.Contains(hitsByImage["Microsoft.Xaml.Behaviors"],
            h => h.Contains("TriggerAction", StringComparison.Ordinal));
        Assert.Contains(hitsByImage["Microsoft.Xaml.Behaviors"],
            h => h.Contains("TriggerBase", StringComparison.Ordinal));

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
    public void Staged_behaviors_collections_enumerate_through_engine_nested_enumerator()
    {
        // Slice-84 engine-native oracle on the staged Behaviors image (status
        // gate bypassed in-probe only; the staged image is sha256-verified
        // by the loader test): the concrete converted collections enumerate
        // through the engine nested FreezableCollection<T>/Enumerator
        // instantiation (empty MoveNext is false, Current throws before the
        // start, like WPF) â€” the exact converted foreach shape binding at
        // JIT time.
        var bytes = StagedMaterialDesignLoader.ReadStagedBytes("Microsoft.Xaml.Behaviors");
        var image = System.Reflection.Assembly.Load(bytes);
        foreach (var name in new[]
        {
            "Microsoft.Xaml.Behaviors.TriggerCollection",
            "Microsoft.Xaml.Behaviors.Core.ConditionCollection",
        })
        {
            var type = image.GetType(name, throwOnError: true)!;
            var collection = Activator.CreateInstance(type, nonPublic: true)!;
            var getEnumerator = type.GetMethod("GetEnumerator", Type.EmptyTypes)!;
            Assert.True(getEnumerator.ReturnType.IsNested);
            Assert.StartsWith("DesktopWasm.Wpf.FreezableCollection`1+Enumerator[", getEnumerator.ReturnType.FullName, StringComparison.Ordinal);
            var enumerator = getEnumerator.Invoke(collection, null)!;
            var moveNext = getEnumerator.ReturnType.GetMethod("MoveNext", Type.EmptyTypes)!;
            Assert.False((bool)moveNext.Invoke(enumerator, null)!);
            var getCurrent = getEnumerator.ReturnType.GetMethod("get_Current", Type.EmptyTypes)!;
            var error = Assert.Throws<System.Reflection.TargetInvocationException>(() => getCurrent.Invoke(enumerator, null));
            Assert.IsType<InvalidOperationException>(error.InnerException);
        }
    }

    [Fact]
    public void Staged_colors_image_binds_the_engine_converter()
    {
        // Positive pin on the slice advance: the converted Colors image
        // references the engine converter/extension/annotation types
        // (never the framework scopes) and its palette cctors run against
        // the engine.
        using var image = StagedMaterialDesignLoader.LoadAssembly("MaterialDesignColors");
        string[] cleared =
        [
            "ColorConverter", "StaticResourceExtension",
            "LocalizabilityAttribute", "LocalizationCategory",
        ];
        Assert.DoesNotContain(image.MainModule.GetTypeReferences(), t =>
            t.Scope.Name is "PresentationFramework" or "PresentationCore" or "WindowsBase" or "System.Xaml" or "System.Windows.Presentation" &&
            cleared.Contains(t.Name, StringComparer.Ordinal));
        var engineRefs = image.MainModule.GetTypeReferences()
            .Where(t => t.Scope.Name == "DesktopWasm.Wpf")
            .Select(t => t.Name)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var name in cleared)
            Assert.Contains(name, engineRefs);
    }

    [Fact]
    public void Staged_colors_swatches_construct_against_the_engine()
    {
        // Engine-native staged-bin oracle (status gate bypassed in-probe
        // only; the staged image is sha256-verified by the loader test):
        // the converted Recommended swatch cctors execute, binding
        // DesktopWasm.Wpf.ColorConverter.ConvertFromString for real.
        var bytes = StagedMaterialDesignLoader.ReadStagedBytes("MaterialDesignColors");
        var image = System.Reflection.Assembly.Load(bytes);
        var swatch = image.GetType("MaterialDesignColors.Recommended.AmberSwatch", throwOnError: true)!;
        System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(swatch.TypeHandle);
    }

    [Fact]
    public void Input_hashes_match_the_upstream_build()
    {
        // Receipt chain: every staged image records the sha256 of the exact
        // upstream binary it was converted from (release_net8.0-windows, the
        // closure the live manifest was packaged from).
        var root = FindBuildDir();
        var manifest = StagedMaterialDesignLoader.ReadManifest();
        var inputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["MaterialDesignDemo"] = Path.Combine(root, "MaterialDesignDemo.dll"),
            ["System.CommandLine"] = Path.Combine(root, "System.CommandLine.dll"),
            ["MaterialDesignDemo.Shared"] = Path.Combine(root, "MaterialDesignDemo.Shared.dll"),
            ["CommunityToolkit.Mvvm"] = Path.Combine(root, "CommunityToolkit.Mvvm.dll"),
            ["MaterialDesignColors"] = Path.Combine(root, "MaterialDesignColors.dll"),
            ["MaterialDesignThemes.Wpf"] = Path.Combine(root, "MaterialDesignThemes.Wpf.dll"),
            ["Microsoft.Xaml.Behaviors"] = Path.Combine(root, "Microsoft.Xaml.Behaviors.dll"),
            ["BluwolfIcons"] = Path.Combine(root, "BluwolfIcons.dll"),
            ["ShowMeTheXAML"] = Path.Combine(root, "ShowMeTheXAML.dll"),
            ["ShowMeTheXAML.AvalonEdit"] = Path.Combine(root, "ShowMeTheXAML.AvalonEdit.dll"),
            ["ICSharpCode.AvalonEdit"] = Path.Combine(root, "ICSharpCode.AvalonEdit.dll"),
        };
        foreach (var (name, upstream) in inputs)
        {
            Assert.True(File.Exists(upstream), $"upstream binary missing: {upstream}");
            using var sha = SHA256.Create();
            var actual = System.Convert.ToHexString(sha.ComputeHash(File.ReadAllBytes(upstream)));
            Assert.Equal(manifest.RawAssemblies.Single(a => a.Name == name).InputSha256, actual);
        }
        Assert.Equal("087728011a8d71926ccf86cb28dc99439e9b1c3f", manifest.SourceCommit);
        Assert.Equal("wpf-convert-v1", manifest.MapVersion);
        Assert.Equal("MaterialDesignDemo", manifest.EntryAssembly);
    }

    private static string FindBuildDir()
    {
        // Receipt chain: the packager converts the pinned checkout tree
        // (targets/.../src/MainDemo.Wpf/bin/Release/net8.0-windows), so the
        // input hashes are proven against those files — not the
        // preparation-time snapshot under artifacts/app-builds (whose
        // MaterialDesignDemo.Shared copy predates a benign same-shape
        // rebuild of the checkout and hashes differently).
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "targets", "studio-inputs", "material-design", "src", "MainDemo.Wpf", "bin", "Release", "net8.0-windows")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return Path.Combine(directory!.FullName, "targets", "studio-inputs", "material-design", "src",
            "MainDemo.Wpf", "bin", "Release", "net8.0-windows");
    }

    private sealed record StagedGap(string Assembly, string Kind, string Detail);
    private sealed record StagedAssembly(string Name, string Sha256, string InputSha256, string File);
    private sealed record StagedManifest(string Status, List<StagedGap> Gaps, List<string> Assemblies, List<StagedAssembly> RawAssemblies, string? SourceCommit, string? MapVersion, string? EntryAssembly);

    private static class StagedMaterialDesignLoader
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

        public static byte[] ReadStagedBytes(string name)
        {
            var images = ReadStaged().ToDictionary(e => e.name, e => e.bytes, StringComparer.OrdinalIgnoreCase);
            if (!images.TryGetValue(name, out var bytes))
                throw new InvalidOperationException($"staged image missing: {name}");
            return bytes;
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
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "src", "DesktopWasm.BrowserHost", "wwwroot", "apps", "material-design", "app-manifest.json")))
                directory = directory.Parent;
            Assert.NotNull(directory);
            return Path.Combine(directory!.FullName, "src", "DesktopWasm.BrowserHost", "wwwroot", "apps", "material-design");
        }
    }
}
