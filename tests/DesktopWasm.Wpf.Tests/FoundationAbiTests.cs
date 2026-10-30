using DesktopWasm.Graphics;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Foundational ABI (Step 36, compat item 1): WPF-identity value types
/// (Point/Vector/Size/Rect/Int32Rect), the brush stack
/// (Brush/SolidColorBrush/Colors/Brushes) and dependency-property
/// metadata/callbacks (PropertyChangedCallback, event args, read-only
/// keys, AddOwner/OverrideMetadata, ClearValue/SetCurrentValue).
/// </summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class FoundationAbiTests
{
    private sealed class Probe : DependencyObject
    {
        public static readonly DependencyProperty LevelProperty =
            DependencyProperty.Register("Level", typeof(double), typeof(Probe), 1.0);

        public List<DependencyPropertyChangedEventArgs> Seen { get; } = new();

        public Probe()
        {
            LevelProperty.AddOwner(GetType());
        }
    }

    private sealed class Hooked : DependencyObject
    {
        public static readonly DependencyProperty TagProperty =
            DependencyProperty.Register("Tag", typeof(string), typeof(Hooked), "base");

        public bool StandardHookRan { get; private set; }

        protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e) =>
            StandardHookRan = true;
    }

    [Fact]
    public void Point_parses_offsets_and_converts()
    {
        Assert.Equal(new Point(1, 2), Point.Parse("1,2"));
        Assert.Equal(new Point(4, 6), new Point(1, 2) + new Vector(3, 4));
        Assert.Equal(new Vector(3, 4), new Point(4, 6) - new Point(1, 2));
        Assert.Equal(new Point(1, 2), Point.Subtract(new Point(4, 6), new Vector(3, 4)));
        var p = new Point(1, 1);
        p.Offset(2, 3);
        Assert.Equal(new Point(3, 4), p);
        Assert.Equal("3,4", p.ToString());
        WpfPoint converted = p;
        Assert.Equal(new WpfPoint(3, 4), converted);
        Assert.Equal(p, (Point)converted);
        Assert.Throws<FormatException>(() => Point.Parse("1"));
    }

    [Fact]
    public void Vector_measures_rotates_and_parses()
    {
        var v = Vector.Parse("3,4");
        Assert.Equal(5.0, v.Length, precision: 9);
        Assert.Equal(25.0, v.LengthSquared, precision: 9);
        v.Normalize();
        Assert.Equal(1.0, v.Length, precision: 9);
        Assert.Equal(90.0, Vector.AngleBetween(new Vector(1, 0), new Vector(0, 1)), precision: 9);
        Assert.Equal(1.0, Vector.CrossProduct(new Vector(1, 0), new Vector(0, 1)), precision: 9);
        Assert.Equal(11.0, Vector.Multiply(new Vector(1, 2), new Vector(3, 4)), precision: 9);
        Assert.Equal(new Vector(4, 6), new Vector(1, 2) + new Vector(3, 4));
        Assert.Equal(new Vector(-1, -2), -new Vector(1, 2));
        Assert.Equal("1,2", new Vector(1, 2).ToString());
        Assert.Throws<FormatException>(() => Vector.Parse("1,2,3"));
    }

    [Fact]
    public void Size_validates_and_converts_explicitly()
    {
        Assert.Equal(new Size(3, 4), Size.Parse("3,4"));
        Assert.True(Size.Empty.IsEmpty);
        Assert.False(new Size(0, 0).IsEmpty);
        Assert.Throws<ArgumentException>(() => new Size(-1, 2));
        Assert.Equal(new Vector(3, 4), (Vector)new Size(3, 4));
        Assert.Equal(new Point(3, 4), (Point)new Size(3, 4));
        WpfSize converted = new Size(3, 4);
        Assert.Equal(new WpfSize(3, 4), converted);
        Assert.Equal(new Size(3, 4), (Size)converted);
        Assert.Throws<FormatException>(() => Size.Parse("3"));
    }

    [Fact]
    public void Rect_contains_inflates_intersects_unions()
    {
        var rect = Rect.Parse("10,20,100,50");
        Assert.Equal(new Point(10, 20), rect.TopLeft);
        Assert.Equal(new Point(110, 70), rect.BottomRight);
        Assert.Equal(new Size(100, 50), rect.Size);
        Assert.True(rect.Contains(new Point(50, 50)));
        Assert.False(rect.Contains(new Point(0, 0)));
        Assert.True(rect.Contains(new Rect(20, 30, 10, 10)));
        Assert.True(rect.Contains(20, 30, 10, 10));
        Assert.True(Rect.Empty.IsEmpty);

        var grown = rect;
        grown.Inflate(5, 5);
        Assert.Equal(new Rect(5, 15, 110, 60), grown);

        var overlap = rect;
        overlap.Intersect(new Rect(50, 50, 100, 100));
        Assert.Equal(new Rect(50, 50, 60, 20), overlap);
        Assert.False(rect.IntersectsWith(new Rect(500, 500, 1, 1)));

        var missing = rect;
        missing.Intersect(new Rect(500, 500, 1, 1));
        Assert.True(missing.IsEmpty);

        var united = Rect.Union(new Rect(0, 0, 10, 10), new Point(100, 100));
        Assert.Equal(new Rect(0, 0, 100, 100), united);

        var moved = new Rect(0, 0, 10, 10);
        moved.Offset(new Vector(5, 5));
        Assert.Equal(new Rect(5, 5, 10, 10), moved);

        WpfRect converted = rect;
        Assert.Equal(new WpfRect(10, 20, 100, 50), converted);
        Assert.Equal(rect, (Rect)converted);
        Assert.Throws<FormatException>(() => Rect.Parse("1,2,3"));
        Assert.Throws<ArgumentException>(() => new Rect(0, 0, -1, 2));
    }

    [Fact]
    public void Int32Rect_parses_and_reports_area()
    {
        var rect = Int32Rect.Parse("1,2,30,40");
        Assert.Equal(new Int32Rect(1, 2, 30, 40), rect);
        Assert.True(rect.HasArea);
        Assert.False(rect.IsEmpty);
        Assert.False(Int32Rect.Empty.HasArea);
        Assert.Equal("1,2,30,40", rect.ToString());
        Assert.Throws<FormatException>(() => Int32Rect.Parse("1,2,3"));
    }

    [Fact]
    public void Brushes_carry_exact_colors_and_freeze()
    {
        Assert.Equal(Color.FromRgb(0xFF, 0x00, 0x00), Colors.Red);
        Assert.Equal(Color.FromRgb(0x00, 0x00, 0x8B), Colors.DarkBlue);
        Assert.Equal(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), Colors.Transparent);
        Assert.Equal(Color.FromRgb(0xF5, 0xF5, 0xF5), Colors.WhiteSmoke);
        Assert.Equal(Color.FromRgb(0x80, 0x80, 0x80), Colors.Gray);

        // Parameterless brush has no color (matches SolidColorBrush()).
        Assert.Equal(default, new SolidColorBrush().Color);
        var red = new SolidColorBrush(Colors.Red);
        Assert.Equal(0xFF0000FFu, red.ToRgba());
        Assert.Equal(1.0, red.Opacity, precision: 9);

        Assert.True(Brushes.White.IsFrozen);
        Assert.Equal(Colors.White, Brushes.White.Color);
        Assert.Throws<InvalidOperationException>(() => Brushes.White.Color = Colors.Black);
        Assert.Throws<InvalidOperationException>(() => Brushes.White.Opacity = 0.5);

        var editable = new SolidColorBrush(Colors.Blue) { Opacity = 0.5 };
        var frozen = Assert.IsType<SolidColorBrush>(editable.GetAsFrozen());
        Assert.True(frozen.IsFrozen);
        Assert.NotSame(editable, frozen);
        Assert.Equal(0.5, frozen.Opacity, precision: 9);
    }

    [Fact]
    public void Freezable_clone_detaches_values()
    {
        var brush = new SolidColorBrush(Colors.Red);
        var clone = Assert.IsType<SolidColorBrush>(brush.Clone());
        Assert.False(clone.IsFrozen);
        Assert.Equal(Colors.Red, clone.Color);
        clone.Color = Colors.Blue;
        Assert.Equal(Colors.Red, brush.Color);

        var frozen = Assert.IsType<SolidColorBrush>(brush.GetAsFrozen());
        Assert.Same(frozen, frozen.GetAsFrozen());
        Assert.True(brush.CanFreeze);
        Assert.False(frozen.CanFreeze);
    }

    [Fact]
    public void Property_callback_fires_with_old_new_and_property()
    {
        var seen = new List<DependencyPropertyChangedEventArgs>();
        var dp = DependencyProperty.Register("Watched", typeof(double), typeof(Probe),
            new PropertyMetadata(0.0, (d, e) => seen.Add(e)));
        var probe = new Probe();
        probe.SetValue(dp, 5.0);
        var change = Assert.Single(seen);
        Assert.Same(dp, change.Property);
        Assert.Equal(0.0, change.OldValue);
        Assert.Equal(5.0, change.NewValue);

        // Equal values do not re-announce.
        probe.SetValue(dp, 5.0);
        Assert.Single(seen);
    }

    [Fact]
    public void Clear_and_unset_revert_to_default_with_notification()
    {
        var seen = new List<DependencyPropertyChangedEventArgs>();
        var dp = DependencyProperty.Register("Clearable", typeof(string), typeof(Probe),
            new PropertyMetadata("dflt", (d, e) => seen.Add(e)));
        var probe = new Probe();
        probe.SetValue(dp, "live");
        Assert.Equal("live", probe.GetValue(dp));
        probe.SetValue(dp, DependencyProperty.UnsetValue);
        Assert.Equal("dflt", probe.GetValue(dp));
        Assert.Equal(2, seen.Count);

        // Clearing an unset property is silent.
        probe.ClearValue(dp);
        Assert.Equal(2, seen.Count);

        probe.SetValue(dp, "again");
        probe.ClearValue(dp);
        Assert.Equal("dflt", probe.GetValue(dp));
    }

    [Fact]
    public void Readonly_registers_once_and_writes_through_key()
    {
        var key = DependencyProperty.RegisterReadOnly("Sealed", typeof(int), typeof(Probe),
            new PropertyMetadata(7));
        var dp = key.DependencyProperty;
        Assert.True(dp.IsReadOnly);
        var probe = new Probe();
        Assert.Equal(7, probe.GetValue(dp));
        Assert.Throws<InvalidOperationException>(() => probe.SetValue(dp, 8));
        probe.SetValue(key, 8);
        Assert.Equal(8, probe.GetValue(dp));
    }

    [Fact]
    public void Attached_register_addowner_and_override_resolve()
    {
        var attached = DependencyProperty.RegisterAttached("Dock", typeof(string), typeof(Probe), "left");
        Assert.Equal("left", new Probe().GetValue(attached));

        var shared = DependencyProperty.Register("Shared", typeof(double), typeof(Probe), 1.0);
        Assert.Same(shared, shared.AddOwner(typeof(Hooked)));
        Assert.Equal(1.0, new Hooked().GetValue(shared));

        var hooked = new Hooked();
        Hooked.TagProperty.OverrideMetadata(typeof(Hooked), new PropertyMetadata("derived"));
        Assert.Equal("derived", hooked.GetValue(Hooked.TagProperty));
        hooked.SetValue(Hooked.TagProperty, "x");
        Assert.True(hooked.StandardHookRan);
    }

    [Fact]
    public void Override_callback_comes_from_effective_metadata()
    {
        var baseSeen = new List<string>();
        var derivedSeen = new List<string>();
        var dp = DependencyProperty.Register("Tuned", typeof(double), typeof(Probe),
            new PropertyMetadata(0.0, (d, e) => baseSeen.Add("base")));
        dp.OverrideMetadata(typeof(Hooked), new PropertyMetadata(0.0, (d, e) => derivedSeen.Add("derived")));
        var probe = new Probe();
        probe.SetValue(dp, 1.0);
        Assert.Equal(["base"], baseSeen);
        Assert.Empty(derivedSeen);
        var hooked = new Hooked();
        hooked.SetValue(dp, 2.0);
        Assert.Equal(["derived"], derivedSeen);
        Assert.Equal(["base"], baseSeen);
    }

    [Fact]
    public void SetCurrentValue_writes_and_invalidate_is_harmless()
    {
        var probe = new Probe();
        probe.SetCurrentValue(Probe.LevelProperty, 9.0);
        Assert.Equal(9.0, probe.GetValue(Probe.LevelProperty));
        probe.InvalidateProperty(Probe.LevelProperty);
        Assert.Equal(9.0, probe.GetValue(Probe.LevelProperty));
    }

    [Fact]
    public void ConfigurationManager_serves_seeded_settings_with_null_default()
    {
        // Directive item P3a: the engine scoped adapter defaults to nulls
        // (missing section on desktop) and serves seeded real values.
        Assert.Null(System.Configuration.ConfigurationManager.AppSettings["Absent"]);
        System.Configuration.ConfigurationManager.SeedAppSettings(new[]
        {
            new KeyValuePair<string, string?>("GitHub", "https://example.test/x"),
        });
        Assert.Equal("https://example.test/x", System.Configuration.ConfigurationManager.AppSettings["GitHub"]);
        Assert.Null(System.Configuration.ConfigurationManager.AppSettings["Absent"]);
    }
}
