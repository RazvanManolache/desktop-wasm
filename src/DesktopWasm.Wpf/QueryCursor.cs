namespace DesktopWasm.Wpf;

// Slice-89 AvalonEdit cursor query: QueryCursorEventArgs + QueryCursorEventHandler
// (the convertible cursor-query pair in the handycontrol package: 2 unmapped-type
// gaps on ICSharpCode.AvalonEdit). Member evidence via Mono.Cecil on the upstream
// net10.0-windows ICSharpCode.AvalonEdit.dll, attributed to the exact converted
// call sites: SelectionMouseHandler.ITextAreaInputHandler.Attach news up a
// QueryCursorEventHandler(textArea_QueryCursor) and calls UIElement.add_QueryCursor
// (Detach mirrors with remove_QueryCursor); textArea_QueryCursor reads
// RoutedEventArgs.Handled and calls QueryCursorEventArgs.set_Cursor three times
// (IBeam/Arrow/IBeam) plus set_Handled; TextView.OnQueryCursor forwards into
// VisualLineElement.OnQueryCursor (empty overridable base); the
// VisualLineLinkText override sets Handled and sets Cursors.Hand. Live shapes
// verified against the 10.0.13 PresentationCore ref pack (unsealed args over
// MouseEventArgs, public (MouseDevice, int) + (MouseDevice, int, StylusDevice)
// ctors, Cursor get/set, InvokeEventHandler override; public add/remove,
// protected virtual OnQueryCursor, QueryCursor event, QueryCursorEvent field;
// (object, QueryCursorEventArgs):void delegate) plus a live net10.0-windows
// runtime probe (QueryCursorEvent is Bubble owned by System.Windows.Input.Mouse
// with QueryCursorEventHandler; freshly constructed args carry Cursor null,
// the timestamp through, and a null RoutedEvent — the raiser assigns the token,
// so the engine ctor leaves it null too). The pair rides the default
// System.Windows.* rule once the engine declares it, so no Overrides entry is
// needed.
// Boundary, never fabricated: the engine keeps no cursor layer (UIElement.Cursor
// is retained, unset by default) and raises no hover-driven QueryCursor loop;
// the converted SelectionMouseHandler/VisualLineLinkText handlers bind against
// this contract and drive Cursor/Handled themselves. The stylus-carrying
// (MouseDevice, int, StylusDevice) overload matches the live public shape now
// that the engine declares StylusDevice (slice-92); the engine raiser still
// passes a null stylus because no pen hardware is driven.

/// <summary>
/// Cursor-query data (matches System.Windows.Input.QueryCursorEventArgs,
/// PresentationCore): a handler commits the cursor the pointer should show by
/// setting <see cref="Cursor"/> (null selects the element's own
/// <see cref="UIElement.Cursor"/>), like the converted AvalonEdit
/// textArea_QueryCursor/link handlers do.
/// </summary>
public class QueryCursorEventArgs : MouseEventArgs
{
    internal QueryCursorEventArgs(MouseDevice? mouse, int timestamp)
        : base(mouse, timestamp,
            MouseButtonState.Released, MouseButtonState.Released, MouseButtonState.Released,
            MouseButtonState.Released, MouseButtonState.Released)
    {
    }

    /// <summary>
    /// Live stylus-carrying shape (matches the (MouseDevice, int,
    /// StylusDevice) overload; closes the slice-89 follow-up now that the
    /// engine declares StylusDevice). Converted code never constructs args;
    /// the raiser keeps the internal path with a null stylus.
    /// </summary>
    public QueryCursorEventArgs(MouseDevice? mouse, int timestamp, StylusDevice? stylusDevice)
        : base(mouse, timestamp, stylusDevice)
    {
    }

    /// <summary>
    /// Cursor the pointer should show (null selects the element's own cursor).
    /// </summary>
    public Cursor? Cursor { get; set; }
}

/// <summary>Cursor-query handler (matches System.Windows.Input.QueryCursorEventHandler).</summary>
public delegate void QueryCursorEventHandler(object sender, QueryCursorEventArgs e);
