namespace DesktopWasm.Wpf;

// Slice-88 AvalonEdit scrolling: IScrollInfo (the last convertible scrolling
// row in the handycontrol package: 1 unmapped-type gap plus the TextArea/TextView
// interface implementations). Member evidence via Mono.Cecil on the upstream
// net10.0-windows ICSharpCode.AvalonEdit.dll, attributed to the exact converted
// call sites: ICSharpCode.AvalonEdit.Rendering.TextView and
// ICSharpCode.AvalonEdit.Editing.TextArea both implement the interface;
// TextEditor.ScrollTo reads get_CanHorizontallyScroll;
// BackgroundGeometryBuilder.<ProcessTextLines> reads ExtentWidth/ViewportWidth;
// TextView.OnScrollChange reads get_ScrollOwner and its explicit Line/Page/
// MouseWheel verbs forward into SetVerticalOffset/SetHorizontalOffset;
// TextArea.ApplyScrollInfo writes ScrollOwner/CanVerticallyScroll/
// CanHorizontallyScroll; CompletionWindowBase.TextViewScrollOffsetChanged reads
// the offsets and viewports. Live shapes verified against the 10.0.13
// PresentationFramework ref pack (public interface, get/set scroll flags,
// get-only extent/viewport/offset doubles, get/set ScrollViewer owner, the 12
// line/page/wheel verbs, the two absolute setters, MakeVisible(Visual, Rect)
// returning Rect). The interface rides the default System.Windows.* rule once
// the engine declares it, so no Overrides entry is needed.
// Boundary, never fabricated: the engine ScrollViewer keeps its pixel-based
// scrolling (CanContentScroll logical scrolling stays a boundary) and does not
// delegate to IScrollInfo children; the converted TextArea/TextView carry their
// own implementations and the engine declares the contract they bind against.

/// <summary>
/// Scrollable-content contract (matches
/// System.Windows.Controls.Primitives.IScrollInfo, PresentationFramework):
/// a ScrollViewer with CanContentScroll delegates scrolling to the child
/// implementing this interface. The converted AvalonEdit TextArea/TextView
/// implement it explicitly; the engine declares the contract so the
/// conversion maps and the converted call shapes bind.
/// </summary>
public interface IScrollInfo
{
    /// <summary>Horizontal scrolling participation (matches IScrollInfo.CanHorizontallyScroll).</summary>
    bool CanHorizontallyScroll { get; set; }

    /// <summary>Vertical scrolling participation (matches IScrollInfo.CanVerticallyScroll).</summary>
    bool CanVerticallyScroll { get; set; }

    /// <summary>Content extent width (matches IScrollInfo.ExtentWidth).</summary>
    double ExtentWidth { get; }

    /// <summary>Content extent height (matches IScrollInfo.ExtentHeight).</summary>
    double ExtentHeight { get; }

    /// <summary>Horizontal scroll position (matches IScrollInfo.HorizontalOffset).</summary>
    double HorizontalOffset { get; }

    /// <summary>Scroll owner (matches IScrollInfo.ScrollOwner).</summary>
    ScrollViewer ScrollOwner { get; set; }

    /// <summary>Vertical scroll position (matches IScrollInfo.VerticalOffset).</summary>
    double VerticalOffset { get; }

    /// <summary>Viewport width (matches IScrollInfo.ViewportWidth).</summary>
    double ViewportWidth { get; }

    /// <summary>Viewport height (matches IScrollInfo.ViewportHeight).</summary>
    double ViewportHeight { get; }

    /// <summary>Scrolls one line up (matches IScrollInfo.LineUp).</summary>
    void LineUp();

    /// <summary>Scrolls one line down (matches IScrollInfo.LineDown).</summary>
    void LineDown();

    /// <summary>Scrolls one line left (matches IScrollInfo.LineLeft).</summary>
    void LineLeft();

    /// <summary>Scrolls one line right (matches IScrollInfo.LineRight).</summary>
    void LineRight();

    /// <summary>Scrolls one page up (matches IScrollInfo.PageUp).</summary>
    void PageUp();

    /// <summary>Scrolls one page down (matches IScrollInfo.PageDown).</summary>
    void PageDown();

    /// <summary>Scrolls one page left (matches IScrollInfo.PageLeft).</summary>
    void PageLeft();

    /// <summary>Scrolls one page right (matches IScrollInfo.PageRight).</summary>
    void PageRight();

    /// <summary>Mouse-wheel scroll up (matches IScrollInfo.MouseWheelUp).</summary>
    void MouseWheelUp();

    /// <summary>Mouse-wheel scroll down (matches IScrollInfo.MouseWheelDown).</summary>
    void MouseWheelDown();

    /// <summary>Mouse-wheel scroll left (matches IScrollInfo.MouseWheelLeft).</summary>
    void MouseWheelLeft();

    /// <summary>Mouse-wheel scroll right (matches IScrollInfo.MouseWheelRight).</summary>
    void MouseWheelRight();

    /// <summary>Scrolls to an absolute horizontal offset (matches IScrollInfo.SetHorizontalOffset).</summary>
    void SetHorizontalOffset(double offset);

    /// <summary>Scrolls to an absolute vertical offset (matches IScrollInfo.SetVerticalOffset).</summary>
    void SetVerticalOffset(double offset);

    /// <summary>
    /// Makes a child rect visible (matches IScrollInfo.MakeVisible).
    /// </summary>
    Rect MakeVisible(Visual visual, Rect rectangle);
}
