using System.Collections;
using System.Reflection;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Slice-93 AvalonEdit composition-scope regression tests:
/// TextCompositionEventHandler + InputManager + LogicalDirection +
/// LanguageSpecificStringDictionary ride the default System.Windows.* rule once
/// the engine declares the exact upstream shapes (member shapes verified with
/// Mono.Cecil against the 10.0.13 PresentationCore/PresentationFramework ref
/// packs plus live net10.0-windows runtime probes). Converted call sites:
/// TextArea add/remove_TextEntering/TextEntered plus OnTextEntering/OnTextEntered
/// Invoke, CompletionWindow AttachEvents/DetachEvents plus
/// textArea_PreviewTextInput (PreviewTextInput attach plus the
/// (InputDevice, TextComposition) news with RaiseEventPair token loads),
/// TextArea.PerformTextInput (InputManager.get_Current plus the
/// (InputManager, IInputElement, string) news), and the VisualLine*
/// GetNextCaretPosition overrides taking LogicalDirection. The dictionary has
/// no code user (orphaned TypeRef only) and exists to clear the conversion row
/// with real behavior. No Application state is touched, so these stay outside
/// the serialized ApplicationState collection (the Modifiers-race note in
/// particular: nothing here reads or mutates WpfInputManager.Current or
/// Keyboard.PrimaryDevice.Modifiers).
/// </summary>
[ResetAmbientState]
public sealed class Slice93CompositionTests
{
    [Fact]
    public void TextCompositionEventHandler_shape_matches_live()
    {
        var handler = typeof(TextCompositionEventHandler);
        Assert.True(handler.IsSealed);
        Assert.Same(typeof(MulticastDelegate), handler.BaseType);
        var invoke = handler.GetMethod("Invoke")!;
        Assert.Equal(typeof(void), invoke.ReturnType);
        var parameters = invoke.GetParameters();
        Assert.Equal(2, parameters.Length);
        Assert.Equal(typeof(object), parameters[0].ParameterType);
        Assert.Equal(typeof(TextCompositionEventArgs), parameters[1].ParameterType);
    }

    [Fact]
    public void InputManager_shape_matches_live()
    {
        Assert.True(typeof(InputManager).IsSealed);
        Assert.False(typeof(InputManager).IsAbstract);
        Assert.Same(typeof(DispatcherObject), typeof(InputManager).BaseType);
        Assert.Null(typeof(InputManager).GetConstructor(Type.EmptyTypes));
        var ctor = typeof(InputManager).GetConstructor(
            BindingFlags.NonPublic | BindingFlags.Instance, Type.EmptyTypes)!;
        Assert.NotNull(ctor);
        Assert.True(ctor!.IsPrivate);
        var current = typeof(InputManager).GetProperty("Current")!;
        Assert.NotNull(current);
        Assert.True(current!.GetGetMethod()!.IsPublic);
        Assert.True(current.GetGetMethod()!.IsStatic);
        Assert.Equal(typeof(InputManager), current.PropertyType);
        Assert.Null(current.GetSetMethod(true));
    }

    [Fact]
    public void InputManager_Current_is_thread_local_identity()
    {
        Assert.NotNull(InputManager.Current);
        Assert.Same(InputManager.Current, InputManager.Current);
        InputManager? other = null;
        var thread = new Thread(() => { other = InputManager.Current; });
        thread.Start();
        thread.Join();
        Assert.NotNull(other);
        Assert.NotSame(InputManager.Current, other);
    }

    [Fact]
    public void InputManager_staging_pipeline_stays_follow_up()
    {
        // Boundary pin: the engine owns only the thread-local Current identity
        // converted construction binds against. The live staging pipeline
        // (ProcessInput, pre/post-process and notify events, providers, menu
        // mode, hit-test invalidation) needs a dispatcher pump and stays out.
        foreach (var name in new[]
        {
            "ProcessInput", "PreProcessInput", "PostProcessInput",
            "PreNotifyInput", "PostNotifyInput", "MostRecentInputDevice",
            "PrimaryKeyboardDevice", "PrimaryMouseDevice", "PushMenuMode",
        })
            Assert.Empty(typeof(InputManager).GetMember(name,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static));
    }

    [Fact]
    public void TextComposition_live_ctor_binds()
    {
        Assert.False(typeof(TextComposition).IsSealed);
        Assert.Same(typeof(DispatcherObject), typeof(TextComposition).BaseType);
        var ctor = typeof(TextComposition).GetConstructor(
            [typeof(InputManager), typeof(IInputElement), typeof(string)])!;
        Assert.NotNull(ctor);
        Assert.True(ctor!.IsPublic);
    }

    [Fact]
    public void TextComposition_carries_text_with_live_null_rules()
    {
        var element = new StubCompositionBox();
        var composition = new TextComposition(InputManager.Current, element, "hi");
        Assert.Equal("hi", composition.Text);
        Assert.Equal(string.Empty, composition.CompositionText);
        // Live tolerates a null manager and a null source (probed).
        Assert.Equal("x", new TextComposition(null, element, "x").Text);
        Assert.Equal("y", new TextComposition(InputManager.Current, null, "y").Text);
        // Live rejects a null result text with ArgumentException (probed).
        Assert.Throws<ArgumentException>(() => new TextComposition(InputManager.Current, element, null!));
        // The engine text convenience keeps the two-text shape.
        var legacy = new TextComposition("a", "b");
        Assert.Equal("a", legacy.Text);
        Assert.Equal("b", legacy.CompositionText);
    }

    [Fact]
    public void TextComposition_extended_surface_stays_follow_up()
    {
        // Boundary pin: converted AvalonEdit reads Text only, so the engine
        // carries no AutoComplete/Stage/Source/Complete/ControlText/SystemText
        // surface (live TextCompositionManager-driven behavior).
        foreach (var name in new[]
        {
            "AutoComplete", "Stage", "Source", "Complete",
            "SystemText", "SystemCompositionText", "ControlText",
        })
            Assert.Empty(typeof(TextComposition).GetMember(name,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static));
    }

    [Fact]
    public void TextCompositionEventArgs_shapes_bind()
    {
        Assert.True(typeof(TextCompositionEventArgs).IsSealed);
        Assert.Same(typeof(InputEventArgs), typeof(TextCompositionEventArgs).BaseType);
        var legacy = new TextCompositionEventArgs("q");
        Assert.Equal("q", legacy.Text);
        Assert.Null(legacy.Device);
        Assert.NotNull(legacy.TextComposition);
        Assert.Equal("q", legacy.TextComposition.Text);
        var element = new StubCompositionBox();
        var composition = new TextComposition(InputManager.Current, element, "hi");
        var args = new TextCompositionEventArgs(Keyboard.PrimaryDevice, composition);
        Assert.Same(Keyboard.PrimaryDevice, args.Device);
        Assert.Same(composition, args.TextComposition);
        Assert.Equal("hi", args.Text);
        Assert.Throws<ArgumentNullException>(
            () => new TextCompositionEventArgs(Keyboard.PrimaryDevice, null!));
    }

    [Fact]
    public void TextCompositionManager_stays_absent()
    {
        // Boundary pin: live owns both TextInput tokens on TextCompositionManager,
        // which the upstream binary never references (verified 0 hits), so the
        // engine owns the tokens on InputManager instead (see UIElement surface).
        Assert.Null(typeof(UIElement).Assembly.GetType("DesktopWasm.Wpf.TextCompositionManager"));
    }

    [Fact]
    public void UIElement_text_surface_matches_live()
    {
        var preview = typeof(UIElement).GetField("PreviewTextInputEvent")!;
        Assert.NotNull(preview);
        Assert.True(preview!.IsPublic);
        Assert.True(preview.IsStatic);
        Assert.Equal(RoutingStrategy.Tunnel, ((RoutedEvent)preview.GetValue(null)!).RoutingStrategy);
        Assert.Equal(typeof(TextCompositionEventHandler),
            ((RoutedEvent)preview.GetValue(null)!).HandlerType);
        Assert.Equal(typeof(InputManager),
            ((RoutedEvent)preview.GetValue(null)!).OwnerType);
        var text = typeof(UIElement).GetField("TextInputEvent")!;
        Assert.NotNull(text);
        Assert.Equal(RoutingStrategy.Bubble, ((RoutedEvent)text!.GetValue(null)!).RoutingStrategy);
        Assert.Equal(typeof(TextCompositionEventHandler),
            ((RoutedEvent)text.GetValue(null)!).HandlerType);
        Assert.Equal(typeof(InputManager),
            ((RoutedEvent)text.GetValue(null)!).OwnerType);
        foreach (var name in new[] { "add_PreviewTextInput", "remove_PreviewTextInput", "add_TextInput", "remove_TextInput" })
        {
            var accessor = typeof(UIElement).GetMethod(name)!;
            Assert.NotNull(accessor);
            Assert.True(accessor!.IsPublic);
            Assert.True(accessor.GetParameters()[0].ParameterType == typeof(TextCompositionEventHandler));
        }
        foreach (var name in new[] { "OnPreviewTextInput", "OnTextInput" })
        {
            var slot = typeof(UIElement).GetMethod(name,
                BindingFlags.NonPublic | BindingFlags.Instance)!;
            Assert.NotNull(slot);
            Assert.True(slot!.IsFamily);
            Assert.True(slot.IsVirtual);
            Assert.Equal(typeof(TextCompositionEventArgs), slot.GetParameters()[0].ParameterType);
        }
    }

    [Fact]
    public void TextInput_attach_raise_detach_flows_preview_then_bubble()
    {
        var element = new StubCompositionBox();
        var seen = new List<string>();
        TextCompositionEventHandler preview = (s, e) =>
        {
            seen.Add("preview:" + e.Text);
            e.Handled = true;
        };
        TextCompositionEventHandler bubble = (s, e) => seen.Add("bubble:" + e.Text);
        element.PreviewTextInput += preview;
        element.TextInput += bubble;
        var args = new TextCompositionEventArgs("ab");
        element.RaiseTextInputForTest(args, preview: true);
        Assert.Equal(["preview:ab"], seen);
        // The preview marked the args handled, so the bubble route skips plain
        // subscribers (routed handled filtering, like the completion pair).
        element.RaiseTextInputForTest(args, preview: false);
        Assert.Equal(["preview:ab"], seen);
        // Fresh args flow the bubble phase.
        element.RaiseTextInputForTest(new TextCompositionEventArgs("cd"), preview: false);
        Assert.Equal(["preview:ab", "bubble:cd"], seen);
        element.PreviewTextInput -= preview;
        element.TextInput -= bubble;
        element.RaiseTextInputForTest(new TextCompositionEventArgs("ef"), preview: true);
        element.RaiseTextInputForTest(new TextCompositionEventArgs("ef"), preview: false);
        Assert.Equal(["preview:ab", "bubble:cd"], seen);
    }

    [Fact]
    public void TextInput_AddHandler_route_fires_and_override_binds()
    {
        var element = new OverridingCompositionBox();
        TextCompositionEventHandler added = (s, e) => element.Calls.Add("added:" + e.Text);
        element.AddHandler(UIElement.TextInputEvent, added);
        element.RaiseTextInputForTest(new TextCompositionEventArgs("g"), preview: false);
        Assert.Equal(["override", "added:g"], element.Calls);
        element.RemoveHandler(UIElement.TextInputEvent, added);
        element.RaiseTextInputForTest(new TextCompositionEventArgs("h"), preview: false);
        Assert.Equal(["override", "added:g", "override"], element.Calls);
    }

    [Fact]
    public void LogicalDirection_values_match_live()
    {
        Assert.True(typeof(LogicalDirection).IsEnum);
        Assert.Equal(0, (int)LogicalDirection.Backward);
        Assert.Equal(1, (int)LogicalDirection.Forward);
    }

    [Fact]
    public void LanguageSpecificStringDictionary_round_trips()
    {
        var dictionary = NewDictionary();
        var english = XmlLanguage.GetLanguage("en-us");
        dictionary.Add(english, "hello");
        Assert.Equal("hello", dictionary[english]);
        Assert.Equal(["hello"], dictionary.Values.ToList());
        Assert.Equal(["en-us"], dictionary.Keys.Select(k => k.IetfLanguageTag).ToList());
        Assert.True(dictionary.ContainsKey(english));
        Assert.Contains(new KeyValuePair<XmlLanguage, string>(english, "hello"), dictionary);
        Assert.True(dictionary.TryGetValue(english, out var value));
        Assert.Equal("hello", value);
        Assert.False(dictionary.TryGetValue(XmlLanguage.GetLanguage("fr-fr"), out _));
        Assert.False(dictionary.IsReadOnly);
        Assert.Single(dictionary);
        dictionary[english] = "hi";
        Assert.Equal("hi", dictionary[english]);
        Assert.True(dictionary.Remove(english));
        Assert.Empty(dictionary);
    }

    [Fact]
    public void LanguageSpecificStringDictionary_rejects_like_live()
    {
        var dictionary = NewDictionary();
        var english = XmlLanguage.GetLanguage("en-us");
        Assert.Throws<ArgumentNullException>(() => dictionary.Add(null!, "x"));
        Assert.Throws<ArgumentNullException>(() => dictionary.Add(english, null!));
        Assert.Throws<ArgumentNullException>(() => dictionary.Add(
            new KeyValuePair<XmlLanguage, string>(english, null!)));
        Assert.Throws<KeyNotFoundException>(() => dictionary[XmlLanguage.GetLanguage("fr-fr")]);
        IDictionary bare = dictionary;
        Assert.Null(bare[42]);
        Assert.Null(bare[null!]);
        Assert.False(bare.Contains(42));
        Assert.False(bare.Contains(null!));
        bare.Remove(42);
        Assert.Throws<ArgumentException>(() => { bare[42] = "x"; });
        Assert.Throws<ArgumentNullException>(() => { bare[null!] = "x"; });
        Assert.Throws<ArgumentNullException>(() => { bare["en-us"] = null!; });
        Assert.Throws<ArgumentNullException>(() => bare.Add(null!, "x"));
        Assert.Throws<ArgumentNullException>(() => bare.Add("en-us", null!));
        Assert.False(bare.IsFixedSize);
        Assert.False(bare.IsReadOnly);
        Assert.False(((ICollection)bare).IsSynchronized);
    }

    [Fact]
    public void LanguageSpecificStringDictionary_converts_string_keys_like_live()
    {
        var dictionary = NewDictionary();
        IDictionary bare = dictionary;
        bare["en-US"] = "hello";
        Assert.Equal("hello", dictionary[XmlLanguage.GetLanguage("en-us")]);
        Assert.Equal("hello", bare["en-us"]!);
        dictionary.Clear();
        Assert.Empty(dictionary);
    }

    [Fact]
    public void PerformTextInput_mirror_runs_the_converted_head()
    {
        // Mirrors the converted TextArea.PerformTextInput head (the exact
        // upstream call shapes): InputManager.get_Current, then
        // TextComposition..ctor(InputManager, IInputElement, string) with the
        // ambient manager, then TextCompositionEventArgs..ctor(InputDevice,
        // TextComposition) with the keyboard device, then a preview/bubble
        // pair over a text element.
        var element = new StubCompositionBox();
        var seen = new List<string>();
        element.PreviewTextInput += (s, e) => seen.Add("preview:" + e.Text);
        element.TextInput += (s, e) => seen.Add("bubble:" + e.Text);
        var manager = InputManager.Current;
        var composition = new TextComposition(manager, element, "k");
        var args = new TextCompositionEventArgs(Keyboard.PrimaryDevice, composition);
        Assert.Same(manager, InputManager.Current);
        Assert.Same(composition, args.TextComposition);
        element.RaiseTextInputForTest(args, preview: true);
        element.RaiseTextInputForTest(
            new TextCompositionEventArgs(args.Device, args.TextComposition), preview: false);
        Assert.Equal(["preview:k", "bubble:k"], seen);
    }

    private static LanguageSpecificStringDictionary NewDictionary() =>
        (LanguageSpecificStringDictionary)typeof(LanguageSpecificStringDictionary)
            .GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance,
                [typeof(IDictionary<XmlLanguage, string>)]!)!
            .Invoke([new Dictionary<XmlLanguage, string>()]);

    private class StubCompositionBox : FrameworkElement
    {
        protected override WpfSize MeasureCore(WpfSize available) => new(10, 10);
        protected override void ArrangeCore(WpfRect finalRect) { }
        protected override void OnRender(DrawingContext dc) { }

        internal void RaiseTextInputForTest(TextCompositionEventArgs e, bool preview) =>
            RaiseTextInput(e, preview);
    }

    private sealed class OverridingCompositionBox : StubCompositionBox
    {
        public readonly List<string> Calls = new();

        protected override void OnTextInput(TextCompositionEventArgs e)
        {
            Calls.Add("override");
            base.OnTextInput(e);
        }
    }
}
