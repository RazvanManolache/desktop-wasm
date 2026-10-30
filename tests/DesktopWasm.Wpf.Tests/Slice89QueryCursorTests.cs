using System.Reflection;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Slice-89 AvalonEdit cursor-query regression tests: the QueryCursor pair
/// (QueryCursorEventArgs + QueryCursorEventHandler) rides the default
/// System.Windows.* rule once the engine declares the exact upstream shapes
/// (member shapes verified with Mono.Cecil against the 10.0.13
/// PresentationCore ref pack, token/defaults verified against a live
/// 10.0.13 runtime probe). Converted call sites: SelectionMouseHandler
/// attach/detach (handler ctor plus UIElement add_/remove_QueryCursor),
/// textArea_QueryCursor (get_Handled plus set_Cursor IBeam/Arrow plus
/// set_Handled), TextView/VisualLineElement/VisualLineLinkText OnQueryCursor
/// overrides (link sets Handled plus Cursors.Hand). No Application state is
/// touched, so these stay outside the serialized ApplicationState collection.
/// </summary>
[ResetAmbientState]
public sealed class Slice89QueryCursorTests
{
    [Fact]
    public void Args_shape_matches_wpf()
    {
        Assert.True(typeof(QueryCursorEventArgs).IsSubclassOf(typeof(MouseEventArgs)));
        Assert.False(typeof(QueryCursorEventArgs).IsSealed);
        var cursor = typeof(QueryCursorEventArgs).GetProperty("Cursor")!;
        Assert.Equal(typeof(Cursor), cursor.PropertyType);
        Assert.NotNull(cursor.GetSetMethod());
    }

    [Fact]
    public void Args_defaults_match_live_probe()
    {
        // Live probe: fresh args carry Cursor null, the timestamp through,
        // and a null RoutedEvent (the raiser assigns the token).
        var args = NewQueryCursor(Environment.TickCount);
        Assert.Null(args.Cursor);
        Assert.Null(args.RoutedEvent);
        Assert.False(args.Handled);
        args.Cursor = Cursors.IBeam;
        Assert.Same(Cursors.IBeam, args.Cursor);
        args.Handled = true;
        Assert.True(args.Handled);
    }

    [Fact]
    public void Args_ctor_stays_internal_like_siblings()
    {
        Assert.Null(typeof(QueryCursorEventArgs).GetConstructor([typeof(MouseDevice), typeof(int)]));
        var ctor = typeof(QueryCursorEventArgs).GetConstructor(
            BindingFlags.NonPublic | BindingFlags.Instance,
            [typeof(MouseDevice), typeof(int)]);
        Assert.NotNull(ctor);
        Assert.True(ctor!.IsAssembly);
        // Slice-92 advance: the stylus-carrying overload now matches the
        // live public shape (the engine declares StylusDevice); the full
        // hover plumbing is pinned in Slice92InputDeviceTests.
        var stylus = typeof(QueryCursorEventArgs).GetConstructor(
            [typeof(MouseDevice), typeof(int), typeof(StylusDevice)])!;
        Assert.NotNull(stylus);
        Assert.True(stylus!.IsPublic);
    }

    [Fact]
    public void Handler_delegate_carries_converted_signature()
    {
        var invoke = typeof(QueryCursorEventHandler).GetMethod("Invoke")!;
        Assert.Equal(typeof(void), invoke.ReturnType);
        Assert.Equal([typeof(object), typeof(QueryCursorEventArgs)],
            invoke.GetParameters().Select(p => p.ParameterType).ToArray());
    }

    [Fact]
    public void UIElement_owns_the_converted_query_event()
    {
        // The exact converted attach/detach shapes
        // (UIElement::add_QueryCursor/remove_QueryCursor) bind onto this CLR
        // event.
        var query = typeof(UIElement).GetEvent("QueryCursor")!;
        Assert.NotNull(query);
        Assert.Equal(typeof(QueryCursorEventHandler), query!.EventHandlerType);
        Assert.NotNull(query.GetAddMethod());
        Assert.NotNull(query.GetRemoveMethod());
    }

    [Fact]
    public void Query_token_matches_live_probe()
    {
        Assert.Equal("QueryCursor", UIElement.QueryCursorEvent.Name);
        Assert.Equal(RoutingStrategy.Bubble, UIElement.QueryCursorEvent.RoutingStrategy);
        Assert.Equal(typeof(QueryCursorEventHandler), UIElement.QueryCursorEvent.HandlerType);
        Assert.Equal(typeof(Mouse), UIElement.QueryCursorEvent.OwnerType);
    }

    [Fact]
    public void OnQueryCursor_slot_is_protected_virtual()
    {
        var slot = typeof(UIElement).GetMethod("OnQueryCursor",
            BindingFlags.NonPublic | BindingFlags.Instance, [typeof(QueryCursorEventArgs)])!;
        Assert.NotNull(slot);
        Assert.True(slot!.IsFamily);
        Assert.True(slot.IsVirtual);
    }

    [Fact]
    public void Query_attach_raise_detach_runs_the_converted_loop()
    {
        // Mirrors the converted SelectionMouseHandler attach/detach shape:
        // the handler lands, commits IBeam + Handled through the OnQueryCursor
        // raiser, and stops after removal.
        var target = new QueryBorder();
        int queries = 0;
        QueryCursorEventHandler handler = (sender, e) =>
        {
            queries++;
            Assert.Same(target, sender);
            if (!e.Handled)
            {
                e.Cursor = Cursors.IBeam;
                e.Handled = true;
            }
        };
        target.QueryCursor += handler;
        var args = NewQueryCursor(Environment.TickCount);
        target.RaiseQuery(args);
        Assert.Equal(1, queries);
        Assert.Same(Cursors.IBeam, args.Cursor);
        Assert.True(args.Handled);
        target.QueryCursor -= handler;
        var again = NewQueryCursor(Environment.TickCount);
        target.RaiseQuery(again);
        Assert.Equal(1, queries);
        Assert.Null(again.Cursor);
        Assert.False(again.Handled);
    }

    [Fact]
    public void Link_style_override_commits_hand_like_visual_line_link_text()
    {
        // Mirrors the converted VisualLineLinkText.OnQueryCursor override:
        // set Handled and commit Cursors.Hand.
        var target = new LinkBorder();
        var args = NewQueryCursor(Environment.TickCount);
        target.RaiseQuery(args);
        Assert.True(args.Handled);
        Assert.Same(Cursors.Hand, args.Cursor);
    }

    private static QueryCursorEventArgs NewQueryCursor(int timestamp) =>
        (QueryCursorEventArgs)typeof(QueryCursorEventArgs)
            .GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance,
                [typeof(MouseDevice), typeof(int)])!
            .Invoke([Mouse.PrimaryDevice, timestamp]);

    private sealed class QueryBorder : ContentControl
    {
        public void RaiseQuery(QueryCursorEventArgs e) => OnQueryCursor(e);
    }

    private sealed class LinkBorder : ContentControl
    {
        public void RaiseQuery(QueryCursorEventArgs e) => OnQueryCursor(e);

        protected override void OnQueryCursor(QueryCursorEventArgs e)
        {
            e.Handled = true;
            e.Cursor = Cursors.Hand;
        }
    }
}
