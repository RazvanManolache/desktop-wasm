using DesktopWasm.Core;
using DesktopWasm.Graphics;

namespace DesktopWasm.Wpf.Tests;

/// <summary>Effects/hosts slice: Effect family, ContainerVisual, PasswordBox, WebBrowser.</summary>
[ResetAmbientState]
public sealed class EffectsHostsTests
{
    private sealed class Harness
    {
        public WasmInputRouter Router { get; } = new();
        public WpfInputManager Manager { get; }
        public StackPanel Root { get; } = new();
        public PasswordBox Box { get; } = new();

        public Harness()
        {
            Manager = new WpfInputManager(Router);
            Root.AddChild(Box);
            Manager.AddRoot(Root);
            Root.Measure(new WpfSize(800, 600));
            Root.Arrange(new WpfRect(0, 0, 800, 600));
            Box.Arrange(new WpfRect(10, 10, 200, 30));
            Manager.TryFocus(Box);
        }

        public void Key(string key, bool ctrl = false) =>
            Router.DispatchKeyboard(new WasmKeyArgs(new WasmWindowHandle(1), key, true, ctrl, false, false));

        public void Type(string text)
        {
            foreach (var ch in text) Key(ch.ToString());
        }
    }

    [Fact]
    public void Enum_values_match_wpf()
    {
        Assert.Equal(0, (int)RenderingBias.Performance);
        Assert.Equal(1, (int)RenderingBias.Quality);
        Assert.Equal(0, (int)RenderMode.Default);
        Assert.Equal(1, (int)RenderMode.SoftwareOnly);
    }

    [Fact]
    public void DropShadowEffect_carries_verified_defaults()
    {
        var shadow = new DropShadowEffect();
        Assert.Equal(Color.FromArgb(0xFF, 0, 0, 0), shadow.Color);
        Assert.Equal(315, shadow.Direction);
        Assert.Equal(5, shadow.ShadowDepth);
        Assert.Equal(1, shadow.Opacity);
        Assert.Equal(5, shadow.BlurRadius);
        Assert.Equal(RenderingBias.Performance, shadow.RenderingBias);
    }

    [Fact]
    public void DropShadowEffect_mutation_notifies()
    {
        var shadow = new DropShadowEffect();
        var changes = 0;
        shadow.Changed += () => changes++;
        shadow.BlurRadius = 8;
        Assert.Equal(1, changes);
        shadow.BlurRadius = 8;
        Assert.Equal(1, changes);
        Assert.IsType<DropShadowEffect>(shadow.Clone());
    }

    [Fact]
    public void Effect_slot_defaults_null_and_repaints()
    {
        var button = new Button { Content = "Hi" };
        Assert.Null(button.Effect);
        Assert.True(UIElement.EffectProperty.Metadata is FrameworkPropertyMetadata meta && meta.AffectsRender);
        var kinds = new List<InvalidationKind>();
        button.Invalidated += kinds.Add;
        var shadow = new DropShadowEffect();
        button.Effect = shadow;
        Assert.Same(shadow, button.GetValue(UIElement.EffectProperty));
        Assert.Contains(InvalidationKind.Render, kinds);
        kinds.Clear();
        shadow.BlurRadius = 9;
        Assert.Contains(InvalidationKind.Render, kinds);
    }

    [Fact]
    public void Effect_assigning_pulls_attached_bindings()
    {
        var border = new Border { Width = 100, Height = 40 };
        border.SetAttached("Effect.BlurRadius", 7.0);
        border.Effect = new DropShadowEffect();
        Assert.Equal(7.0, Assert.IsType<DropShadowEffect>(border.Effect).BlurRadius);
    }

    [Fact]
    public void Hard_shadow_paints_offset_rect_before_content()
    {
        var border = new Border { Width = 100, Height = 40 };
        border.Effect = new DropShadowEffect
        {
            Direction = 0, ShadowDepth = 5, BlurRadius = 0,
            Opacity = 1, Color = Color.FromRgb(0, 0, 0),
        };
        var commands = WasmMilCore.RenderHeadless(border, new WpfSize(800, 600));
        var first = Assert.IsType<DrawCommand.FillRect>(commands[0]);
        var r = border.RenderRect;
        Assert.Equal(100, r.Width);
        Assert.Equal(40, r.Height);
        Assert.Equal(r.X + 5, first.X);
        Assert.Equal(r.Y, first.Y);
        Assert.Equal(r.Width, first.W);
        Assert.Equal(r.Height, first.H);
        Assert.Equal(0x000000FFu, first.Rgba);
    }

    [Fact]
    public void Blurred_shadow_paints_falling_alpha_rings()
    {
        var border = new Border { Width = 100, Height = 40 };
        border.Effect = new DropShadowEffect
        {
            Direction = 0, ShadowDepth = 5, BlurRadius = 4,
            Opacity = 1, Color = Color.FromRgb(0, 0, 0),
        };
        var rects = WasmMilCore.RenderHeadless(border, new WpfSize(800, 600))
            .OfType<DrawCommand.FillRect>().Take(2).ToList();
        var r = border.RenderRect;
        Assert.Equal(2, rects.Count);
        // Outer ring spreads further with lower alpha; inner ring tighter and darker.
        Assert.Equal(r.X + 5 - 4, rects[0].X);
        Assert.Equal(r.Y - 4, rects[0].Y);
        Assert.Equal(108, rects[0].W);
        Assert.Equal(0x00000055u, rects[0].Rgba);
        Assert.Equal(r.X + 5 - 2, rects[1].X);
        Assert.Equal(104, rects[1].W);
        Assert.Equal(0x000000AAu, rects[1].Rgba);
    }

    [Fact]
    public void Transparent_effect_paints_no_shadow()
    {
        var border = new Border { Width = 100, Height = 40 };
        border.Effect = new DropShadowEffect { Opacity = 0, ShadowDepth = 5, BlurRadius = 0 };
        var commands = WasmMilCore.RenderHeadless(border, new WpfSize(800, 600));
        Assert.DoesNotContain(commands.OfType<DrawCommand.FillRect>(),
            f => f.X == 5 && f.W == 100 && f.H == 40);
    }

    [Fact]
    public void Qualified_effect_setter_resolves_through_inheritance()
    {
        // <Setter Property="TextBlock.Effect" .../> resolves like WPF.
        Assert.Same(UIElement.EffectProperty,
            DependencyProperty.FromName(typeof(TextBlock), "Effect"));
    }

    [Fact]
    public void ContainerVisual_hosts_children_and_replays()
    {
        var container = new ContainerVisual();
        var drawing = new DrawingVisual();
        drawing.RenderOpen().DrawRectangle(2, 3, 10, 10, 0xFF0000FF);
        container.Children.Add(drawing);
        Assert.Same(container, drawing.VisualParent);
        Assert.Equal(1, container.VisualChildCount);
        Assert.Equal(1, VisualTreeHelper.GetChildrenCount(container));
        Assert.Same(drawing, VisualTreeHelper.GetChild(container, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => VisualTreeHelper.GetChild(container, 1));
        var dc = new DrawingContext();
        container.Render(dc);
        var rect = Assert.IsType<DrawCommand.FillRect>(Assert.Single(dc.Commands));
        Assert.Equal(2, rect.X);
        Assert.Equal(3, rect.Y);
    }

    [Fact]
    public void DrawingVisual_inherits_the_container_effect_slot()
    {
        var drawing = new DrawingVisual();
        var shadow = new DropShadowEffect { BlurRadius = 2 };
        drawing.Effect = shadow;
        Assert.Same(shadow, drawing.Effect);
        Assert.Same(shadow, drawing.GetValue(UIElement.EffectProperty));
        var container = new ContainerVisual();
        container.Effect = shadow;
        Assert.Same(shadow, container.Effect);
    }

    [Fact]
    public void ContainerVisual_renders_through_render_target()
    {
        var container = new ContainerVisual();
        var drawing = new DrawingVisual();
        drawing.RenderOpen().DrawRectangle(0, 0, 10, 10, 0xFF0000FF);
        container.Children.Add(drawing);
        var target = new RenderTargetBitmap(10, 10, 96, 96, PixelFormats.Bgra32);
        target.Render(container);
        Assert.Contains(target.RequirePixels().Pixels, b => b != 0);
    }

    [Fact]
    public void PasswordBox_defaults_match_wpf()
    {
        var box = new PasswordBox();
        Assert.Equal('*', box.PasswordChar);
        Assert.Equal(0, box.MaxLength);
        Assert.Equal(string.Empty, box.Password);
        Assert.True(box.Focusable);
    }

    [Fact]
    public void PasswordBox_typing_masks_and_notifies()
    {
        var harness = new Harness();
        var changes = 0;
        harness.Box.PasswordChanged += (_, _) => changes++;
        harness.Type("s3cret");
        Assert.Equal("s3cret", harness.Box.Password);
        Assert.Equal(6, changes);
        Assert.Equal("******", harness.Box.MaskedText);
        Assert.Equal(6, harness.Box.SecurePassword.Length);
        var commands = WasmMilCore.RenderHeadless(harness.Root, new WpfSize(800, 600));
        Assert.DoesNotContain(commands.OfType<DrawCommand.DrawText>(), t => t.Text.Contains("s3cret"));
        Assert.Contains(commands.OfType<DrawCommand.DrawText>(), t => t.Text == "******");
    }

    [Fact]
    public void PasswordBox_enforces_max_length_and_selection_edits()
    {
        var box = new PasswordBox { MaxLength = 4 };
        box.Password = "toolong";
        Assert.Equal("tool", box.Password);
        box.Password = "ab";
        box.SelectAll();
        Assert.Equal("ab", box.SelectedText);
        box.SelectedText = "cd";
        Assert.Equal("cd", box.Password);
        box.Clear();
        Assert.Equal(string.Empty, box.Password);
    }

    [Fact]
    public void PasswordBox_xaml_attributes_parse()
    {
        var root = XamlLoader.Load(
            "<PasswordBox Password=\"secret\" PasswordChar=\"#\" MaxLength=\"10\" />").Root;
        var box = Assert.IsType<PasswordBox>(root);
        Assert.Equal("secret", box.Password);
        Assert.Equal('#', box.PasswordChar);
        Assert.Equal(10, box.MaxLength);
    }

    [Fact]
    public void Effect_property_element_with_literals_parses()
    {
        var root = XamlLoader.Load(
            "<Border Width=\"100\" Height=\"40\">" +
            "<Border.Effect><DropShadowEffect Direction=\"0\" ShadowDepth=\"5\" BlurRadius=\"0\" " +
            "Color=\"#FF000000\" Opacity=\"1\"/></Border.Effect></Border>").Root;
        var border = Assert.IsType<Border>(root);
        var shadow = Assert.IsType<DropShadowEffect>(border.Effect);
        Assert.Equal(0, shadow.Direction);
        Assert.Equal(5, shadow.ShadowDepth);
        Assert.Equal(0, shadow.BlurRadius);
        var commands = WasmMilCore.RenderHeadless(border, new WpfSize(800, 600));
        var first = Assert.IsType<DrawCommand.FillRect>(commands[0]);
        var r = border.RenderRect;
        Assert.Equal(r.X + 5, first.X);
        Assert.Equal(100, first.W);
    }

    [Fact]
    public void Effect_property_element_with_bindings_lands_and_updates()
    {
        var root = XamlLoader.Load(
            "<Grid><Border Width=\"100\" Height=\"40\">" +
            "<Border.Effect><DropShadowEffect BlurRadius=\"{Binding Size}\" " +
            "Opacity=\"{Binding Strength}\"/></Border.Effect></Border></Grid>").Root;
        var border = Assert.IsType<Border>(Assert.IsType<Grid>(root).LogicalChildren[0]);
        var shadow = Assert.IsType<DropShadowEffect>(border.Effect);
        border.DataContext = new EffectSource { Size = 9.0, Strength = 0.5 };
        Assert.Equal(9.0, shadow.BlurRadius);
        Assert.Equal(0.5, shadow.Opacity);
        var first = WasmMilCore.RenderHeadless(border, new WpfSize(800, 600))
            .OfType<DrawCommand.FillRect>().First();
        Assert.Equal(100 + 20, first.W);
    }

    [Fact]
    public void DropShadowEffect_resource_entry_parses()
    {
        var root = XamlLoader.Load(
            "<Grid xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"><Grid.Resources>" +
            "<DropShadowEffect x:Key=\"Shadow.Background\" ShadowDepth=\"0\" Color=\"#FF2A2A2A\" " +
            "Opacity=\".6\" BlurRadius=\"3\" RenderingBias=\"Quality\"/>" +
            "</Grid.Resources></Grid>").Root;
        var grid = Assert.IsType<Grid>(root);
        Assert.True(grid.Resources.TryGetValue("Shadow.Background", out var value));
        var shadow = Assert.IsType<DropShadowEffect>(value);
        Assert.Equal(0, shadow.ShadowDepth);
        Assert.Equal(Color.FromArgb(0xFF, 0x2A, 0x2A, 0x2A), shadow.Color);
        Assert.Equal(0.6, shadow.Opacity);
        Assert.Equal(3, shadow.BlurRadius);
        Assert.Equal(RenderingBias.Quality, shadow.RenderingBias);
    }

    [Fact]
    public void WebBrowser_journal_navigates_and_raises()
    {
        var browser = new WebBrowser();
        Assert.Null(browser.Source);
        Assert.Null(browser.Document);
        Assert.False(browser.CanGoBack);
        var seen = new List<Uri?>();
        browser.Navigated += (_, e) => seen.Add(e.Uri);
        browser.Navigate(new Uri("https://example.com/"));
        Assert.Equal(new Uri("https://example.com/"), browser.Source);
        browser.NavigateToString("<p>hi</p>");
        Assert.Equal(new Uri("about:blank"), browser.Source);
        Assert.Equal("<p>hi</p>", browser.HtmlDocument);
        Assert.True(browser.CanGoBack);
        Assert.False(browser.CanGoForward);
        browser.GoBack();
        Assert.Equal(new Uri("https://example.com/"), browser.Source);
        Assert.True(browser.CanGoForward);
        browser.GoForward();
        Assert.Equal(new Uri("about:blank"), browser.Source);
        Assert.Equal(4, seen.Count);
        browser.Refresh();
        Assert.Equal(5, seen.Count);
        Assert.Throws<NotSupportedException>(() => browser.InvokeScript("alert"));
    }

    [Fact]
    public void WebBrowser_xaml_source_parses_and_paints()
    {
        var root = XamlLoader.Load("<WebBrowser Source=\"https://example.com/\" />").Root;
        var browser = Assert.IsType<WebBrowser>(root);
        Assert.Equal(new Uri("https://example.com/"), browser.Source);
        var commands = WasmMilCore.RenderHeadless(browser, new WpfSize(800, 600));
        Assert.Contains(commands.OfType<DrawCommand.DrawText>(),
            t => t.Text.Contains("example.com"));
    }

    [Fact]
    public void ProcessRenderMode_roundtrips()
    {
        var saved = RenderOptions.ProcessRenderMode;
        try
        {
            RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
            Assert.Equal(RenderMode.SoftwareOnly, RenderOptions.ProcessRenderMode);
        }
        finally
        {
            RenderOptions.ProcessRenderMode = saved;
        }
    }

    public sealed class EffectSource
    {
        public double Size { get; set; }
        public double Strength { get; set; }
    }
}
