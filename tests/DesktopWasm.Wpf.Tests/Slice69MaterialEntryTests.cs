using DesktopWasm.Graphics;
using SkiaSharp;
using System.Globalization;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Slice-69 MaterialDesignDemo entry prerequisites: the scroll chrome the
/// Material themes drive (ScrollBar/ViewportSize/Scroll event, Track value
/// mapping), the custom-popup placement shape, the exception-validation
/// marker, the XmlLanguage culture bridge and the Skia-backed Fonts
/// enumeration the demo's font picker binds. Every numeric, default and
/// validation behavior below was verified live against the 9.0.21
/// WindowsDesktop runtime before it was pinned.
/// </summary>
[ResetAmbientState]
public sealed class Slice69MaterialEntryTests
{
    [Fact]
    public void Popup_primary_axis_numerics_match_wpf()
    {
        Assert.Equal(0, (int)PopupPrimaryAxis.None);
        Assert.Equal(1, (int)PopupPrimaryAxis.Horizontal);
        Assert.Equal(2, (int)PopupPrimaryAxis.Vertical);
    }

    [Fact]
    public void Custom_popup_placement_carries_point_and_axis_with_value_equality()
    {
        var placement = new CustomPopupPlacement(new Point(10, 20), PopupPrimaryAxis.Horizontal);
        Assert.Equal(new Point(10, 20), placement.Point);
        Assert.Equal(PopupPrimaryAxis.Horizontal, placement.PrimaryAxis);

        Assert.Equal(placement, new CustomPopupPlacement(new Point(10, 20), PopupPrimaryAxis.Horizontal));
        Assert.NotEqual(placement, new CustomPopupPlacement(new Point(10, 20), PopupPrimaryAxis.Vertical));
        Assert.NotEqual(placement, new CustomPopupPlacement(new Point(11, 20), PopupPrimaryAxis.Horizontal));
        Assert.True(placement == new CustomPopupPlacement(new Point(10, 20), PopupPrimaryAxis.Horizontal));
        Assert.True(placement != new CustomPopupPlacement(new Point(10, 20), PopupPrimaryAxis.Vertical));
        Assert.Equal(
            new CustomPopupPlacement(new Point(10, 20), PopupPrimaryAxis.Horizontal).GetHashCode(),
            placement.GetHashCode());
    }

    [Fact]
    public void Custom_popup_placement_callback_invokes_with_sizes_and_offset()
    {
        CustomPopupPlacementCallback callback = (popupSize, targetSize, offset) =>
            new[] { new CustomPopupPlacement(new Point(offset.X + popupSize.Width, offset.Y + targetSize.Height), PopupPrimaryAxis.Vertical) };
        var result = callback(new Size(100, 50), new Size(200, 30), new Point(5, 7));
        var single = Assert.Single(result);
        Assert.Equal(new Point(105, 37), single.Point);
        Assert.Equal(PopupPrimaryAxis.Vertical, single.PrimaryAxis);
    }

    [Fact]
    public void Scroll_event_type_numerics_match_wpf()
    {
        Assert.Equal(0, (int)ScrollEventType.EndScroll);
        Assert.Equal(1, (int)ScrollEventType.First);
        Assert.Equal(2, (int)ScrollEventType.LargeDecrement);
        Assert.Equal(3, (int)ScrollEventType.LargeIncrement);
        Assert.Equal(4, (int)ScrollEventType.Last);
        Assert.Equal(5, (int)ScrollEventType.SmallDecrement);
        Assert.Equal(6, (int)ScrollEventType.SmallIncrement);
        Assert.Equal(7, (int)ScrollEventType.ThumbPosition);
        Assert.Equal(8, (int)ScrollEventType.ThumbTrack);
    }

    [Fact]
    public void Scroll_event_args_carry_type_and_new_value()
    {
        var args = new ScrollEventArgs(ScrollEventType.ThumbTrack, 42.5);
        Assert.Equal(ScrollEventType.ThumbTrack, args.ScrollEventType);
        Assert.Equal(42.5, args.NewValue);
    }

    [Fact]
    public void Exception_validation_rule_is_an_always_valid_marker()
    {
        var rule = new ExceptionValidationRule();
        Assert.Equal(ValidationStep.RawProposedValue, rule.ValidationStep);
        Assert.False(rule.ValidatesOnTargetUpdated);
        Assert.Same(ValidationResult.ValidResult,
            rule.Validate("text", CultureInfo.InvariantCulture));
        Assert.Same(ValidationResult.ValidResult,
            rule.Validate(new InvalidOperationException("boom"), CultureInfo.InvariantCulture));
        Assert.Same(ValidationResult.ValidResult,
            rule.Validate(null, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Exception_validation_rule_participates_in_binding_rules()
    {
        var model = new Slice69Model { Name = "original" };
        var editor = new TextBox();
        editor.SetBinding("Text", new Binding { Path = "Name", Source = model, ValidationRules = { new ExceptionValidationRule() } });
        editor.Text = "edited";
        Assert.Equal("edited", model.Name);
        Assert.False(Validation.GetHasError(editor));
    }

    [Fact]
    public void Xml_language_canonicalizes_and_caches()
    {
        var language = XmlLanguage.GetLanguage("en-US");
        Assert.Equal("en-us", language.IetfLanguageTag);
        Assert.Equal("en-US", language.GetSpecificCulture().Name);
        Assert.Equal("en-US", language.GetEquivalentCulture().Name);
        Assert.Same(language, XmlLanguage.GetLanguage("en-US"));
        Assert.Same(language, XmlLanguage.GetLanguage("en-us"));
        Assert.Equal("en-us", language.ToString());
        Assert.False(language.Equals(XmlLanguage.Empty));
    }

    [Fact]
    public void Xml_language_empty_and_null_edges_match_wpf()
    {
        Assert.Equal("", XmlLanguage.Empty.IetfLanguageTag);
        Assert.Throws<ArgumentNullException>(() => XmlLanguage.GetLanguage(null!));
        // The empty tag parses (like WPF); only null is rejected.
        Assert.Equal("", XmlLanguage.GetLanguage("").IetfLanguageTag);
    }

    [Fact]
    public void Fonts_system_families_enumerate_live_platform_fonts()
    {
        var families = Fonts.SystemFontFamilies;
        Assert.NotNull(families);
        Assert.All(families, f => Assert.False(string.IsNullOrEmpty(f.Source)));
        Assert.Equal(families.Count, families.Select(f => f.Source).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        var faces = Fonts.SystemTypefaces;
        Assert.NotNull(faces);
        Assert.All(faces, t => Assert.False(string.IsNullOrEmpty(t.FontFamily.Source)));
    }

    [Fact]
    public void Fonts_location_lookup_reads_real_font_files()
    {
        var work = Directory.CreateTempSubdirectory("slice69-fonts");
        try
        {
            Assert.Empty(Fonts.GetFontFamilies(work.FullName));
            Assert.Empty(Fonts.GetTypefaces(work.FullName));

            var systemFonts = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
            var source = Directory.Exists(systemFonts)
                ? Directory.GetFiles(systemFonts, "*.ttf").OrderBy(f => f, StringComparer.OrdinalIgnoreCase).FirstOrDefault()
                : null;
            Assert.True(source is not null, "No system .ttf font found to prove file-backed lookup.");
            var target = System.IO.Path.Combine(work.FullName, "probe-font.ttf");
            File.Copy(source!, target);

            var oracle = SkiaSharp.SKTypeface.FromFile(target);
            Assert.NotNull(oracle);
            try
            {
                var families = Fonts.GetFontFamilies(work.FullName);
                Assert.Contains(families, f => f.Source == oracle!.FamilyName);
                var faces = Fonts.GetTypefaces(work.FullName);
                var face = Assert.Single(faces);
                Assert.Equal(oracle!.FamilyName, face.FontFamily.Source);
            }
            finally
            {
                oracle?.Dispose();
            }
        }
        finally
        {
            Directory.Delete(work.FullName, recursive: true);
        }
    }

    [Fact]
    public void Fonts_rejects_null_and_pack_locations_precisely()
    {
        Assert.Throws<ArgumentNullException>(() => Fonts.GetFontFamilies((string)null!));
        Assert.Throws<ArgumentNullException>(() => Fonts.GetTypefaces((Uri)null!));
        Assert.Throws<NotSupportedException>(() =>
            Fonts.GetFontFamilies(new Uri("pack://application:,,,/Fonts/#Segoe UI")));
        var missing = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "slice69-no-such-dir-" + Guid.NewGuid().ToString("N"));
        Assert.Empty(Fonts.GetFontFamilies(missing));
    }

    [Fact]
    public void Scrollbar_defaults_match_wpf()
    {
        var bar = new ScrollBar();
        Assert.Equal(Orientation.Vertical, bar.Orientation);
        Assert.Equal(0, bar.ViewportSize);
        Assert.Null(bar.Track);
        Assert.Equal(0, bar.Minimum);
        Assert.Equal(1, bar.Maximum);
        Assert.Equal(0, bar.Value);
        Assert.Equal(0.1, bar.SmallChange);
        Assert.Equal(1, bar.LargeChange);
        Assert.NotNull(ScrollBar.ScrollEvent);
        Assert.Same(ScrollBar.ScrollEvent, ScrollBar.ScrollEvent);
    }

    [Fact]
    public void Scrollbar_viewport_size_rejects_negatives_and_nan()
    {
        var bar = new ScrollBar();
        Assert.Throws<ArgumentException>(() => bar.ViewportSize = -5);
        Assert.Throws<ArgumentException>(() => bar.ViewportSize = double.NaN);
        Assert.Equal(0, bar.ViewportSize);
        bar.ViewportSize = 120;
        Assert.Equal(120, bar.ViewportSize);
    }

    [Fact]
    public void Scrollbar_value_change_reports_end_scroll_after_value_changed()
    {
        var bar = new ScrollBar { Minimum = 0, Maximum = 100, ViewportSize = 10 };
        var order = new List<string>();
        ScrollEventArgs? scroll = null;
        bar.ValueChanged += (_, _) => order.Add("value");
        bar.Scroll += (_, e) => { order.Add("scroll"); scroll = e; };
        bar.Value = 40;
        Assert.Equal(new[] { "value", "scroll" }, order);
        Assert.NotNull(scroll);
        Assert.Equal(ScrollEventType.EndScroll, scroll!.ScrollEventType);
        Assert.Equal(40, scroll.NewValue);
        Assert.Same(bar, scroll.Source);
        Assert.Same(ScrollBar.ScrollEvent, scroll.RoutedEvent);

        // Coerced no-op sets raise nothing.
        order.Clear();
        bar.Value = 40;
        Assert.Empty(order);
    }

    [Fact]
    public void Scrollbar_styled_values_round_trip_through_the_dp_store()
    {
        var bar = new ScrollBar();
        Assert.Same(ScrollBar.OrientationProperty, DependencyProperty.FromName(typeof(ScrollBar), "Orientation"));
        Assert.Same(ScrollBar.ViewportSizeProperty, DependencyProperty.FromName(typeof(ScrollBar), "ViewportSize"));
        bar.SetValue(ScrollBar.OrientationProperty, Orientation.Horizontal);
        Assert.Equal(Orientation.Horizontal, bar.Orientation);
        Assert.Throws<ArgumentException>(() => bar.SetValue(ScrollBar.ViewportSizeProperty, -1.0));
    }

    [Fact]
    public void Track_defaults_value_mapping_and_direction_match_wpf_shapes()
    {
        var track = new Track();
        Assert.Equal(Orientation.Horizontal, track.Orientation);
        Assert.Equal(0, track.Minimum);
        Assert.Equal(1, track.Maximum);
        Assert.Equal(0, track.Value);
        Assert.True(double.IsNaN(track.ViewportSize));
        Assert.False(track.IsDirectionReversed);
        Assert.Null(track.Thumb);
        Assert.Null(track.DecreaseRepeatButton);
        Assert.Null(track.IncreaseRepeatButton);

        // Value clamps into range.
        track.Maximum = 100;
        track.Value = 250;
        Assert.Equal(100, track.Value);
        track.Value = -10;
        Assert.Equal(0, track.Value);

        // Horizontal point mapping over an arranged rect.
        track.Measure(new WpfSize(200, 20));
        track.Arrange(new WpfRect(0, 0, 200, 20));
        Assert.Equal(0, track.ValueFromPoint(new Point(0, 10)));
        Assert.Equal(50, track.ValueFromPoint(new Point(100, 10)), precision: 6);
        Assert.Equal(100, track.ValueFromPoint(new Point(200, 10)));
        Assert.Equal(10, track.ValueFromDistance(20, 0), precision: 6);
        Assert.Equal(0, track.ValueFromDistance(0, 20), precision: 6);

        // Reversed ends flip the mapping.
        track.IsDirectionReversed = true;
        Assert.Equal(100, track.ValueFromPoint(new Point(0, 10)));
        Assert.Equal(0, track.ValueFromPoint(new Point(200, 10)));
        Assert.Equal(-10, track.ValueFromDistance(20, 0), precision: 6);
    }

    [Fact]
    public void Track_arranges_parts_proportionally()
    {
        var track = new Track { Minimum = 0, Maximum = 100, Value = 25, ViewportSize = 100 };
        var decrease = new RepeatButton();
        var thumb = new Thumb();
        var increase = new RepeatButton();
        track.DecreaseRepeatButton = decrease;
        track.Thumb = thumb;
        track.IncreaseRepeatButton = increase;
        track.Measure(new WpfSize(200, 20));
        track.Arrange(new WpfRect(0, 0, 200, 20));

        // Viewport 100 over extent 200: thumb takes half the 200px track.
        Assert.Equal(100, thumb.RenderRect.Width, precision: 6);
        // Value 25 over range 100: decrease takes a quarter of the free 100px.
        Assert.Equal(25, decrease.RenderRect.Width, precision: 6);
        Assert.Equal(0, decrease.RenderRect.X, precision: 6);
        Assert.Equal(25, thumb.RenderRect.X, precision: 6);
        Assert.Equal(125, increase.RenderRect.X, precision: 6);
        Assert.Equal(75, increase.RenderRect.Width, precision: 6);
    }

    [Fact]
    public void Track_vertical_mapping_follows_orientation()
    {
        var track = new Track { Orientation = Orientation.Vertical, Minimum = 0, Maximum = 100 };
        track.Measure(new WpfSize(20, 200));
        track.Arrange(new WpfRect(0, 0, 20, 200));
        Assert.Equal(0, track.ValueFromPoint(new Point(10, 0)), precision: 6);
        Assert.Equal(100, track.ValueFromPoint(new Point(10, 200)), precision: 6);
        Assert.Equal(20, track.ValueFromDistance(0, 40), precision: 6);
    }

    [Fact]
    public void Scrollbar_paints_groove_and_value_positioned_thumb_in_skia()
    {
        var bar = new ScrollBar { Minimum = 0, Maximum = 100, Value = 50, ViewportSize = 25, Width = 17, Height = 200 };
        var window = new Window { Content = bar };
        using var surface = new SkiaSurface(100, 220);
        var mil = new WasmMilCore(surface);
        var commands = mil.Render(window, new WpfSize(100, 220), new WpfRect(0, 0, 100, 220));
        mil.CommitChannel(commands);
        using var bitmap = SKBitmap.FromImage(surface.Snapshot());
        var slot = bar.RenderRect;
        Assert.True(slot.Width > 0 && slot.Height > 0, "scrollbar laid out empty");
        var groove = bitmap.GetPixel(
            Math.Clamp((int)(slot.X + slot.Width / 2), 0, 99),
            Math.Clamp((int)(slot.Y + 3), 0, 219));
        Assert.True(groove.Red > 190 && groove.Green > 190 && groove.Blue > 190, $"groove pixel {groove} is not light gray");
        var thumb = bar.ThumbRect();
        var ink = bitmap.GetPixel(
            Math.Clamp((int)(thumb.X + thumb.Width / 2), 0, 99),
            Math.Clamp((int)(thumb.Y + thumb.Height / 2), 0, 219));
        Assert.True(ink.Red > 200 && ink.Green > 200 && ink.Blue > 200, $"thumb pixel {ink} is not the white thumb fill");
    }

    private sealed class Slice69Model
    {
        public string Name { get; set; } = "";
    }
}

/// <summary>
/// Slice-69 dispatcher facade: MaterialDesignThemes.Wpf calls
/// DispatcherExtensions.Invoke(Dispatcher, Action) through the
/// System.Windows.Presentation facade scope (net8.0-windows member
/// evidence). The helpers forward to the matching Dispatcher members over
/// the rebound single-threaded pump.
/// </summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class Slice69DispatcherExtensionsTests
{
    public Slice69DispatcherExtensionsTests()
    {
        WasmDispatcher.Current = new WasmDispatcher(new DesktopWasm.Core.WasmDispatcherPump());
    }

    [Fact]
    public void Invoke_runs_the_action_synchronously()
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        var ran = 0;
        dispatcher.Invoke(() => ran = 41 + 1);
        Assert.Equal(42, ran);
        dispatcher.Invoke(() => ran++, DispatcherPriority.Background);
        Assert.Equal(43, ran);
        Assert.Throws<ArgumentNullException>(() => DispatcherExtensions.Invoke(null!, () => { }));
        Assert.Throws<ArgumentNullException>(() => DispatcherExtensions.Invoke(dispatcher, null!));
    }

    [Fact]
    public void BeginInvoke_posts_and_invoke_async_returns_the_operation()
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        var ran = 0;
        var posted = DispatcherExtensions.BeginInvoke(dispatcher, () => ran = 7);
        Assert.IsAssignableFrom<DispatcherOperation>(posted);
        Assert.Equal(DispatcherOperationStatus.Pending, posted.Status);
        Assert.True(posted.Wait(TimeSpan.FromSeconds(5)));
        Assert.Equal(7, ran);

        var async = DispatcherExtensions.InvokeAsync(dispatcher, () => ran = 8);
        Assert.True(async.Wait(TimeSpan.FromSeconds(5)));
        Assert.Equal(8, ran);

        var prioritized = DispatcherExtensions.BeginInvoke(dispatcher, () => ran = 9, DispatcherPriority.Background);
        Assert.Equal(DispatcherPriority.Background, prioritized.Priority);
        Assert.True(prioritized.Wait(TimeSpan.FromSeconds(5)));
        Assert.Equal(9, ran);

        Assert.Throws<ArgumentNullException>(() => DispatcherExtensions.BeginInvoke(null!, () => { }));
    }
}
