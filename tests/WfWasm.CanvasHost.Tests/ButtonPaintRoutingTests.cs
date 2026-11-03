using System.Collections.Generic;
using System.Drawing;
using WfWasm.CanvasHost;
using WfWasm.Shim;
using Xunit;
using FormsButton = System.Windows.Forms.Button;
using FormsControl = System.Windows.Forms.Control;
using FormsControlStyles = System.Windows.Forms.ControlStyles;
using FormsDialogResult = System.Windows.Forms.DialogResult;
using FormsForm = System.Windows.Forms.Form;
using FormsIButtonControl = System.Windows.Forms.IButtonControl;
using FormsPaintEventArgs = System.Windows.Forms.PaintEventArgs;
using FormsPanel = System.Windows.Forms.Panel;

// Routing rule for the flat Button fast-path (SfButton style images,
// 2026-10-23): Button itself overrides OnPaint upstream, so managed
// paint must win only when the most-derived paint method is declared
// strictly below Button. Exact Button and non-overriding subclasses
// keep the flat renderer.
//
// PAINT-DIALOG-BUTTONS-FIX (2026-11-01): Paint.NET's dialog buttons (PdnPushButton)
// and custom IButtonControl instances without managed OnPaint overrides route to
// the flat/synthetic button renderer with proper default buttonface fallback colors,
// border styling, and centered text.
public sealed class ButtonPaintRoutingTests
{
    private class SelfPaintButton : FormsButton
    {
        protected override void OnPaint(FormsPaintEventArgs e) { }
    }

    private sealed class PlainButton : FormsButton
    {
    }

    private sealed class GrandchildButton : SelfPaintButton
    {
    }

    private sealed class MockButtonControl : FormsControl, FormsIButtonControl
    {
        private Color _backColor = Color.Empty;
        private Color _foreColor = Color.Empty;

        public MockButtonControl()
        {
            SetStyle(FormsControlStyles.SupportsTransparentBackColor, true);
        }

        public override Color BackColor
        {
            get => _backColor;
            set => _backColor = value;
        }

        public override Color ForeColor
        {
            get => _foreColor;
            set => _foreColor = value;
        }

        public FormsDialogResult DialogResult { get; set; }
        public void NotifyDefault(bool value) { }
        public void PerformClick() { }
    }

    private sealed class PdnPushButton : FormsControl, FormsIButtonControl
    {
        private Color _backColor = Color.Empty;
        private Color _foreColor = Color.Empty;

        public PdnPushButton()
        {
            SetStyle(FormsControlStyles.SupportsTransparentBackColor, true);
        }

        public override Color BackColor
        {
            get => _backColor;
            set => _backColor = value;
        }

        public override Color ForeColor
        {
            get => _foreColor;
            set => _foreColor = value;
        }

        public FormsDialogResult DialogResult { get; set; }
        public void NotifyDefault(bool value) { }
        public void PerformClick() { }
    }

    [Fact]
    public void Exact_Button_Is_Flat()
    {
        Assert.False(WinFormsAppHost.ButtonPaintsItself(typeof(FormsButton)));
    }

    [Fact]
    public void Non_Overriding_Subclass_Is_Flat()
    {
        Assert.False(WinFormsAppHost.ButtonPaintsItself(typeof(PlainButton)));
    }

    [Fact]
    public void OnPaint_Override_Routes_Managed()
    {
        Assert.True(WinFormsAppHost.ButtonPaintsItself(typeof(SelfPaintButton)));
    }

    [Fact]
    public void Inherited_Override_Routes_Managed()
    {
        Assert.True(WinFormsAppHost.ButtonPaintsItself(typeof(GrandchildButton)));
    }

    [Fact]
    public void Non_Button_Type_Is_Flat()
    {
        Assert.False(WinFormsAppHost.ButtonPaintsItself(typeof(object)));
    }

    [Fact]
    public void IsRenderedAsButton_Classifies_Buttons_And_IButtonControl()
    {
        using var plain = new PlainButton();
        using var selfPaint = new SelfPaintButton();
        using var mockIButton = new MockButtonControl();
        using var pdnButton = new PdnPushButton();
        using var panel = new FormsPanel();

        Assert.True(WinFormsAppHost.IsRenderedAsButton(plain));
        Assert.False(WinFormsAppHost.IsRenderedAsButton(selfPaint));
        Assert.True(WinFormsAppHost.IsRenderedAsButton(mockIButton));
        Assert.True(WinFormsAppHost.IsRenderedAsButton(pdnButton));
        Assert.False(WinFormsAppHost.IsRenderedAsButton(panel));
        Assert.False(WinFormsAppHost.IsRenderedAsButton(null));
    }

    [Fact]
    public void Button_BackColor_Fallback_And_Pressed_State()
    {
        using var button = new MockButtonControl();

        button.BackColor = Color.Empty;
        Assert.Equal(3789677055u, WinFormsAppHost.GetButtonBackColor(button, isPressed: false));
        Assert.Equal(3437557759u, WinFormsAppHost.GetButtonBackColor(button, isPressed: true));

        button.BackColor = Color.Transparent;
        Assert.Equal(3789677055u, WinFormsAppHost.GetButtonBackColor(button, isPressed: false));

        button.BackColor = Color.FromArgb(200, 100, 50);
        uint expectedCustom = ((uint)200 << 24) | ((uint)100 << 16) | ((uint)50 << 8) | 255u;
        Assert.Equal(expectedCustom, WinFormsAppHost.GetButtonBackColor(button, isPressed: false));
    }

    [Fact]
    public void Button_ForeColor_Fallback()
    {
        using var button = new MockButtonControl();

        button.ForeColor = Color.Empty;
        Assert.Equal(255u, WinFormsAppHost.GetButtonForeColor(button)); // Black 0x000000FF

        button.ForeColor = Color.FromArgb(10, 20, 30);
        uint expectedColor = ((uint)10 << 24) | ((uint)20 << 16) | ((uint)30 << 8) | 255u;
        Assert.Equal(expectedColor, WinFormsAppHost.GetButtonForeColor(button));
    }

    [Fact]
    public void RenderControls_Renders_PdnPushButton_With_Bounds_And_Centered_Text()
    {
        using var host = new WinFormsAppHost();
        using var form = new FormsForm { Visible = true };
        using var okBtn = new PdnPushButton
        {
            Left = 10,
            Top = 20,
            Width = 80,
            Height = 24,
            Text = "OK",
            BackColor = Color.Transparent,
            ForeColor = Color.Black,
            Visible = true
        };
        form.Controls.Add(okBtn);

        var canvas = new TestRecordingCanvas();
        host.RenderControls(canvas, form.Controls, parentAbsX: 100, parentAbsY: 200);

        // Expected absolute coordinates:
        // AbsX = 100 + 10 = 110, AbsY = 200 + 20 = 220
        // Width = 80, Height = 24
        Assert.Single(canvas.RoundedRectangles);
        var rect = canvas.RoundedRectangles[0];
        Assert.Equal(110f, rect.x);
        Assert.Equal(220f, rect.y);
        Assert.Equal(80f, rect.w);
        Assert.Equal(24f, rect.h);
        Assert.Equal(3f, rect.radius);
        Assert.Equal(3789677055u, rect.rgba);

        // Borders
        Assert.Equal(4, canvas.Lines.Count);
        foreach (var line in canvas.Lines)
        {
            Assert.Equal(2913840639u, line.rgba);
        }

        // Centered Text
        Assert.Single(canvas.Texts);
        var text = canvas.Texts[0];
        Assert.Equal("OK", text.text);
        Assert.Equal(110f + 40f, text.x); // Center X = 150
        Assert.Equal(220f + 12f, text.y); // Center Y = 232
        Assert.Equal(TextHAlign.Center, text.hAlign);
        Assert.Equal(TextVAlign.Middle, text.vAlign);
    }

    private sealed class TestRecordingCanvas : ICanvas
    {
        public readonly List<(float x, float y, float w, float h, float radius, uint rgba)> RoundedRectangles = new();
        public readonly List<(float x1, float y1, float x2, float y2, float width, uint rgba)> Lines = new();
        public readonly List<(string text, float x, float y, string fontCss, uint rgba, TextHAlign hAlign, TextVAlign vAlign)> Texts = new();

        public void FillRectangle(float x, float y, float width, float height, uint rgba) { }
        public void FillRoundedRectangle(float x, float y, float width, float height, float radius, uint rgba)
        {
            RoundedRectangles.Add((x, y, width, height, radius, rgba));
        }
        public void FillGradientVertical(float x, float y, float width, float height, uint topRgba, uint bottomRgba) { }
        public void DrawLine(float x1, float y1, float x2, float y2, float width, uint rgba)
        {
            Lines.Add((x1, y1, x2, y2, width, rgba));
        }
        public void DrawEllipse(float x, float y, float width, float height, float lineWidth, uint outlineRgba, uint? fillRgba = null) { }
        public void DrawText(string text, float x, float y, string fontCss, uint rgba) { }
        public void DrawImage(byte[] rgba, int width, int height, float x, float y, float dw, float dh) { }
        public void DrawTextAligned(string text, float x, float y, string fontCss, uint rgba, TextHAlign hAlign, TextVAlign vAlign, int mnemonicIndex = -1,
            float clipX = 0, float clipY = 0, float clipWidth = 0, float clipHeight = 0)
        {
            Texts.Add((text, x, y, fontCss, rgba, hAlign, vAlign));
        }
    }
}
