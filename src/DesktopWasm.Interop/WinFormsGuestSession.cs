extern alias Fork;
extern alias WasmDraw;

using System.Reflection;
using DesktopWasm.Core;
using DesktopWasm.Graphics;
using DesktopWasm.Wpf;
using ForkForms = Fork::System.Windows.Forms;
using WfBitmap = WasmDraw::System.Drawing.Bitmap;
using WfColor = WasmDraw::System.Drawing.Color;
using WfPoint = WasmDraw::System.Drawing.Point;
using WfRectangle = WasmDraw::System.Drawing.Rectangle;
using WfSize = WasmDraw::System.Drawing.Size;

namespace DesktopWasm.Interop;

/// <summary>
/// Live WinForms-guest host session: owns a real fork
/// <see cref="Fork::System.Windows.Forms.Control"/>, sizes it in device px,
/// and delivers DPI-and-clip translated input through the control's real
/// focus/capture/mouse/keyboard surface. Rendering rasterizes the guest via
/// <c>DrawToBitmap</c> and shares it as a <see cref="DrawCommand.DrawImage"/>
/// texture (the explicit surface exchange). Text rasterization off-browser
/// throws <see cref="NotSupportedException"/> from the drawing layer; the
/// browser canvas remains the text paint oracle (see TextBridgeTests).
/// </summary>
public sealed class WinFormsGuestSession : IHostedGuest
{
    private static readonly MethodInfo s_onMouseDown =
        typeof(ForkForms.Control).GetMethod("OnMouseDown",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly MethodInfo s_onMouseUp =
        typeof(ForkForms.Control).GetMethod("OnMouseUp",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly MethodInfo s_onMouseMove =
        typeof(ForkForms.Control).GetMethod("OnMouseMove",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly MethodInfo s_onKeyDown =
        typeof(ForkForms.Control).GetMethod("OnKeyDown",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly MethodInfo s_onKeyUp =
        typeof(ForkForms.Control).GetMethod("OnKeyUp",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly MethodInfo s_onKeyPress =
        typeof(ForkForms.Control).GetMethod("OnKeyPress",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly MethodInfo s_onClick =
        typeof(ForkForms.Control).GetMethod("OnClick",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly MethodInfo s_onMouseClick =
        typeof(ForkForms.Control).GetMethod("OnMouseClick",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly MethodInfo s_onPaintBackground =
        typeof(ForkForms.Control).GetMethod("OnPaintBackground",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!;
    private static readonly MethodInfo s_onPaint =
        typeof(ForkForms.Control).GetMethod("OnPaint",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!;

    private bool _disposed;
    private ForkForms.Control? _pressed;

    /// <summary>
    /// Hosts <paramref name="content"/> at <paramref name="size"/> layout px
    /// (a zero <paramref name="dpi"/> means identity). The fork control is
    /// sized in device px; a <see cref="Fork::System.Windows.Forms.Form"/>
    /// root is shown headlessly so focus works like the fork's own tests.
    /// </summary>
    public WinFormsGuestSession(ForkForms.Control content, WpfSize size, InteropDpi dpi = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        Content = content;
        Size = RequireLayoutSize(size, nameof(size));
        Dpi = NormalizeDpi(dpi);
        ApplySize();
        _ = Content.Handle; // force headless handle creation (fork test pattern)
        EnsureSubtreeHandles(Content);
        if (Content is ForkForms.Form form)
            form.Show();
    }

    /// <summary>The real hosted control (state mutates here).</summary>
    public ForkForms.Control Content { get; }

    /// <summary>Session size in layout px.</summary>
    public WpfSize Size { get; private set; }

    /// <summary>Session DPI (device px per layout px).</summary>
    public InteropDpi Dpi { get; private set; }

    /// <summary>Control holding the current press (null when none).</summary>
    public ForkForms.Control? PressedControl => _pressed;

    public bool IsDisposed => _disposed;

    public event Action? Invalidated;

    /// <summary>Re-sizes the guest (guards empty sizes).</summary>
    public void Resize(WpfSize size)
    {
        ThrowIfDisposed();
        Size = RequireLayoutSize(size, nameof(size));
        ApplySize();
        Invalidated?.Invoke();
    }

    /// <summary>Re-applies DPI (control is re-sized in device px).</summary>
    public void SetDpi(InteropDpi dpi)
    {
        ThrowIfDisposed();
        Dpi = NormalizeDpi(dpi);
        ApplySize();
        Invalidated?.Invoke();
    }

    /// <summary>
    /// Forwards a device-px pointer event: scales to layout, drops presses
    /// that start outside the clipped host bounds, and routes everything
    /// else through the real control surface (explicit capture keeps the
    /// pressed target for moves/releases outside). Returns false only for
    /// a dropped outside press.
    /// </summary>
    public bool DispatchPointer(HostPointerEvent pointer)
    {
        ThrowIfDisposed();
        var (x, y) = pointer.ToLayout(Dpi);
        if (pointer.Kind == WasmPointerKind.Down &&
            !pointer.IsInside(Dpi, Size.Width, Size.Height))
            return false;

        var buttons = MapButton(pointer.Button);
        switch (pointer.Kind)
        {
            case WasmPointerKind.Down:
            {
                var target = HitTest(x, y) ?? Content;
                target.Focus();
                _pressed = target;
                target.Capture = true;
                var local = ToClient(target, x, y);
                s_onMouseDown.Invoke(target,
                    [new ForkForms.MouseEventArgs(buttons, 1, local.X, local.Y, 0)]);
                return true;
            }
            case WasmPointerKind.Up:
            case WasmPointerKind.Cancel:
            {
                var target = _pressed ?? HitTest(x, y) ?? Content;
                var local = ToClient(target, x, y);
                if (pointer.Kind == WasmPointerKind.Up)
                {
                    s_onMouseUp.Invoke(target,
                        [new ForkForms.MouseEventArgs(buttons, 1, local.X, local.Y, 0)]);
                    if (pointer.Button == WasmPointerButton.Left &&
                        _pressed is not null && ReferenceEquals(HitTest(x, y), _pressed))
                        Activate(target, local, buttons);
                }
                ReleasePress();
                return true;
            }
            default: // Move (and any future phase): hover the point.
            {
                var target = _pressed ?? HitTest(x, y) ?? Content;
                var local = ToClient(target, x, y);
                s_onMouseMove.Invoke(target,
                    [new ForkForms.MouseEventArgs(buttons, 0, local.X, local.Y, 0)]);
                return true;
            }
        }
    }

    /// <summary>
    /// Forwards a key event to the focused guest through the real keyboard
    /// surface (KeyDown/KeyPress/KeyUp in desktop order). Ctrl+A on a
    /// <see cref="Fork::System.Windows.Forms.TextBoxBase"/> selects all,
    /// mirroring the production browser host. Tab/Shift+Tab moves focus via
    /// the container's real <c>SelectNextControl</c>.
    /// </summary>
    public void DispatchKey(HostKeyEvent key)
    {
        ThrowIfDisposed();
        var (keyData, keyChar) = MapKey(key);
        var target = FocusedOrContent();

        if (key.IsDown && keyChar == '\t')
        {
            MoveFocusTab(shift: key.Shift, target);
            return;
        }

        if (key.IsDown && (keyData & ForkForms.Keys.KeyCode) == ForkForms.Keys.A &&
            (keyData & ForkForms.Keys.Control) != 0 &&
            target is ForkForms.TextBoxBase textBox)
        {
            textBox.SelectAll();
        }

        var args = new ForkForms.KeyEventArgs(keyData);
        if (key.IsDown)
        {
            s_onKeyDown.Invoke(target, [args]);
            if (!args.Handled && keyChar is not null)
                s_onKeyPress.Invoke(target, [new ForkForms.KeyPressEventArgs(keyChar.Value)]);
        }
        else
        {
            s_onKeyUp.Invoke(target, [args]);
        }
    }

    /// <summary>
    /// Activation authority for a release-inside press. Mirrors the
    /// production browser host's click dispatch: a real
    /// <see cref="Fork::System.Windows.Forms.Button"/> activates through
    /// the public <c>PerformClick</c> (the fork's own tests drive the same
    /// public path; the virtual mouse-up path cannot observe release-inside
    /// headlessly because <c>WindowFromPoint</c> answers default), every
    /// other control through the protected <c>OnClick</c> virtual; both are
    /// followed by the protected <c>OnMouseClick</c> like production.
    /// </summary>
    private void Activate(ForkForms.Control target, WfPoint local, ForkForms.MouseButtons buttons)
    {
        if (target is ForkForms.Button button)
        {
            button.PerformClick();
        }
        else
        {
            s_onClick.Invoke(target,
                [new ForkForms.MouseEventArgs(buttons, 1, local.X, local.Y, 0)]);
        }
        s_onMouseClick.Invoke(target,
            [new ForkForms.MouseEventArgs(buttons, 1, local.X, local.Y, 0)]);
    }

    /// <summary>
    /// Rasterizes the guest to a device-px texture frame by invoking the
    /// real <c>OnPaintBackground</c>/<c>OnPaint</c> virtuals over a
    /// canvas-backed bitmap (the production host's raster structure, minus
    /// its canvas bridge), then shares pixels as a
    /// <see cref="DrawCommand.DrawImage"/> texture. Text rasterization
    /// off-browser throws <see cref="NotSupportedException"/> from the
    /// drawing layer; the browser canvas remains the text paint oracle.
    /// </summary>
    public HostedFrame RenderFrame()
    {
        ThrowIfDisposed();
        var (deviceW, deviceH) = DeviceSize();
        using var bitmap = new WfBitmap(deviceW, deviceH);
        using var graphics = WasmDraw::System.Drawing.Graphics.FromImage(bitmap);
        PaintTree(graphics, Content, new WfRectangle(0, 0, deviceW, deviceH));
        var rgba = new byte[deviceW * deviceH * 4];
        for (var row = 0; row < deviceH; row++)
            for (var col = 0; col < deviceW; col++)
            {
                var c = bitmap.GetPixel(col, row);
                var i = (row * deviceW + col) * 4;
                rgba[i] = c.R;
                rgba[i + 1] = c.G;
                rgba[i + 2] = c.B;
                rgba[i + 3] = c.A;
            }
        return new HostedFrame(deviceW, deviceH, Dpi,
            [new DrawCommand.DrawImage(rgba, deviceW, deviceH, 0, 0, deviceW, deviceH)]);
    }

    private void PaintTree(
        WasmDraw::System.Drawing.Graphics graphics,
        ForkForms.Control control,
        WfRectangle clip)
    {
        if (clip.Width <= 0 || clip.Height <= 0)
            return;
        using var args = new ForkForms.PaintEventArgs(graphics, clip);
        try
        {
            s_onPaintBackground.Invoke(control, [args]);
            s_onPaint.Invoke(control, [args]);
        }
        catch (TargetInvocationException tie) when (tie.InnerException is not null)
        {
            // Surface the real paint failure (e.g. off-browser text
            // rasterization), not the reflection wrapper.
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(tie.InnerException);
        }
        foreach (ForkForms.Control child in control.Controls)
        {
            var childClip = WfRectangle.Intersect(
                new WfRectangle(
                    clip.X + child.Location.X,
                    clip.Y + child.Location.Y,
                    child.Width,
                    child.Height),
                clip);
            if (childClip.Width <= 0 || childClip.Height <= 0)
                continue;
            var state = graphics.Save();
            try
            {
                graphics.TranslateTransform(child.Location.X, child.Location.Y);
                PaintTree(graphics, child, new WfRectangle(0, 0, child.Width, child.Height));
            }
            finally
            {
                graphics.Restore(state);
            }
        }
    }

    /// <summary>
    /// Realizes handles for the visible subtree, like a desktop show: the
    /// fork creates child handles on show (WmShowWindow runs CreateControl
    /// over the visible subtree), not on parent handle creation, so without
    /// this nested hit-testing and child input have no windows to resolve.
    /// Runs top-down (parents before children); invisible children keep
    /// their lazy handles until shown. Never throws.
    /// </summary>
    private static void EnsureSubtreeHandles(ForkForms.Control root)
    {
        foreach (ForkForms.Control child in root.Controls)
        {
            try
            {
                if (!child.IsDisposed && !child.IsHandleCreated && child.Visible)
                    _ = child.Handle;
            }
            catch
            {
            }
            EnsureSubtreeHandles(child);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ReleasePress();
        Content.Dispose();
    }

    private void ReleasePress()
    {
        if (_pressed is not null)
        {
            try { _pressed.Capture = false; }
            catch { }
            _pressed = null;
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private (int Width, int Height) DeviceSize()
    {
        var w = Math.Max(1, (int)Math.Round(Size.Width * Dpi.ScaleX));
        var h = Math.Max(1, (int)Math.Round(Size.Height * Dpi.ScaleY));
        return (w, h);
    }

    private void ApplySize()
    {
        var (w, h) = DeviceSize();
        Content.Size = new WfSize(w, h);
    }

    private ForkForms.Control HitTest(double x, double y)
    {
        var pt = new WfPoint((int)Math.Round(x), (int)Math.Round(y));
        return Content.GetChildAtPoint(pt) ?? Content;
    }

    private static WfPoint ToClient(ForkForms.Control target, double x, double y)
    {
        // Root sits at (0,0); subtract ancestor offsets up to the root.
        var ox = 0;
        var oy = 0;
        for (var c = target; c is not null; c = c.Parent)
        {
            ox += c.Location.X;
            oy += c.Location.Y;
        }
        return new WfPoint((int)Math.Round(x) - ox, (int)Math.Round(y) - oy);
    }

    private ForkForms.Control FocusedOrContent()
    {
        if (Content is ForkForms.ContainerControl container && container.ActiveControl is not null)
            return container.ActiveControl;
        if (Content.Focused)
            return Content;
        return FindFocused(Content) ?? Content;
    }

    private static ForkForms.Control? FindFocused(ForkForms.Control root)
    {
        foreach (ForkForms.Control child in root.Controls)
        {
            if (child.Focused)
                return child;
            if (FindFocused(child) is { } deep)
                return deep;
        }
        return null;
    }

    private void MoveFocusTab(bool shift, ForkForms.Control target)
    {
        var container = target as ForkForms.ContainerControl
            ?? target.Parent as ForkForms.ContainerControl
            ?? Content as ForkForms.ContainerControl;
        if (container is not null)
            container.SelectNextControl(target, forward: !shift, tabStopOnly: true, nested: true, wrap: true);
    }

    private static ForkForms.MouseButtons MapButton(WasmPointerButton button) => button switch
    {
        WasmPointerButton.Left => ForkForms.MouseButtons.Left,
        WasmPointerButton.Right => ForkForms.MouseButtons.Right,
        WasmPointerButton.Middle => ForkForms.MouseButtons.Middle,
        _ => ForkForms.MouseButtons.None,
    };

    private static (ForkForms.Keys KeyData, char? KeyChar) MapKey(HostKeyEvent key)
    {
        var data = ForkForms.Keys.None;
        if (key.Ctrl) data |= ForkForms.Keys.Control;
        if (key.Shift) data |= ForkForms.Keys.Shift;
        if (key.Alt) data |= ForkForms.Keys.Alt;

        char? keyChar = null;
        ForkForms.Keys code = ForkForms.Keys.None;
        if (key.Key.Length == 1)
        {
            var c = key.Key[0];
            if (char.IsAsciiLetter(c))
            {
                code = ForkForms.Keys.A + (char.ToUpperInvariant(c) - 'A');
                keyChar = key.Shift ? char.ToUpperInvariant(c) : char.ToLowerInvariant(c);
            }
            else if (char.IsAsciiDigit(c))
            {
                code = ForkForms.Keys.D0 + (c - '0');
                keyChar = c;
            }
            else if (c == ' ')
            {
                code = ForkForms.Keys.Space;
                keyChar = ' ';
            }
        }
        else
        {
            code = key.Key switch
            {
                "Enter" => ForkForms.Keys.Enter,
                "Tab" => ForkForms.Keys.Tab,
                "Escape" => ForkForms.Keys.Escape,
                "Backspace" => ForkForms.Keys.Back,
                "Delete" => ForkForms.Keys.Delete,
                "Insert" => ForkForms.Keys.Insert,
                "Home" => ForkForms.Keys.Home,
                "End" => ForkForms.Keys.End,
                "PageUp" => ForkForms.Keys.PageUp,
                "PageDown" => ForkForms.Keys.Next,
                "ArrowLeft" => ForkForms.Keys.Left,
                "ArrowRight" => ForkForms.Keys.Right,
                "ArrowUp" => ForkForms.Keys.Up,
                "ArrowDown" => ForkForms.Keys.Down,
                _ when key.Key.Length == 2 && key.Key[0] == 'F' && int.TryParse(key.Key[1..], out var f) && f is >= 1 and <= 9
                    => ForkForms.Keys.F1 + (f - 1),
                _ => ForkForms.Keys.None,
            };
            keyChar = key.Key switch
            {
                "Enter" => '\r',
                "Tab" => '\t',
                _ => null,
            };
        }
        return (data | code, keyChar);
    }

    private static WpfSize RequireLayoutSize(WpfSize size, string name)
    {
        if (!double.IsFinite(size.Width) || size.Width <= 0 ||
            !double.IsFinite(size.Height) || size.Height <= 0)
            throw new ArgumentOutOfRangeException(name, "Session size must be finite and positive.");
        return size;
    }

    private static InteropDpi NormalizeDpi(InteropDpi dpi) =>
        dpi.Equals(default) ? InteropDpi.Identity : InteropDpi.ForScale(dpi.ScaleX, dpi.ScaleY);
}
