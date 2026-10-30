namespace DesktopWasm.Wpf.Tests;

[Collection("ApplicationState")]

/// <summary>
/// Native-WPF parity (directive item 1): merged-dictionary last-wins,
/// validate-before-coerce rejection, and read-only ClearValue enforcement.
/// Each behavior is triangulated against the native oracle in
/// E:/tmp/wpf-review-20261029/ParityProbe (native vs shim in one exe);
/// these in-tree tests pin the shim side of that comparison so the
/// parity holds without the external probe.
/// </summary>
[ResetAmbientState]
public sealed class NativeParityTests
{
    private sealed class Owner : DependencyObject { }

    [Fact]
    public void Merged_duplicate_key_last_added_wins_like_native()
    {
        var dict = new ResourceDictionary();
        var first = new ResourceDictionary { ["key"] = "first" };
        var second = new ResourceDictionary { ["key"] = "second" };
        var third = new ResourceDictionary { ["key"] = "third" };
        dict.MergedDictionaries.Add(first);
        dict.MergedDictionaries.Add(second);
        Assert.Equal("second", dict["key"]);
        dict.MergedDictionaries.Add(third);
        Assert.Equal("third", dict["key"]);

        // Local entries still beat every merge.
        dict.Add("key", "local");
        Assert.Equal("local", dict["key"]);
        Assert.True(dict.Contains("key"));
        Assert.False(dict.Contains("absent"));
    }

    [Fact]
    public void Invalid_value_rejected_before_coerce_like_native()
    {
        int coerceCalls = 0;
        var dp = DependencyProperty.Register("ParityValidated", typeof(int), typeof(Owner),
            new PropertyMetadata(0, null, (_, v) => { coerceCalls++; return Math.Max(0, (int)v); }),
            v => (int)v >= 0);
        var o = new Owner();

        var ex = Assert.Throws<ArgumentException>(() => o.SetValue(dp, -3));
        Assert.Contains("ParityValidated", ex.Message, StringComparison.Ordinal);
        Assert.Equal(0, coerceCalls);
        Assert.Equal(0, o.GetValue(dp));

        // Valid values still flow through coercion.
        o.SetValue(dp, 5);
        Assert.Equal(5, o.GetValue(dp));
        Assert.Equal(1, coerceCalls);
    }

    [Fact]
    public void Validate_runs_before_coerce_with_single_notification()
    {
        var order = new List<string>();
        var seen = new List<DependencyPropertyChangedEventArgs>();
        var dp = DependencyProperty.Register("ParityOrdered", typeof(double), typeof(Owner),
            new PropertyMetadata(5.0, (d, e) => { order.Add("changed"); seen.Add(e); },
                (d, v) => { order.Add("coerce"); return Math.Min(10.0, (double)v!); }),
            v => { order.Add("validate"); return (double)v! >= 0; });
        var o = new Owner();

        // Native validates the default at registration (oracle 2026-10-29: the
        // callback fires during Register), so only post-registration calls are pinned.
        order.Clear();

        Assert.Throws<ArgumentException>(() => o.SetValue(dp, -1.0));
        Assert.Equal(new[] { "validate" }, order);
        Assert.Empty(seen);
        Assert.Equal(5.0, o.GetValue(dp));

        order.Clear();
        o.SetValue(dp, 50.0);
        Assert.Equal(new[] { "validate", "coerce", "changed" }, order);
        Assert.Equal(10.0, o.GetValue(dp));
        var change = Assert.Single(seen);
        Assert.Equal(5.0, change.OldValue);
        Assert.Equal(10.0, change.NewValue);
    }

    [Fact]
    public void Unprivileged_readonly_clear_throws_and_preserves_like_native()
    {
        var seen = new List<DependencyPropertyChangedEventArgs>();
        var key = DependencyProperty.RegisterReadOnly("ParitySealed", typeof(int), typeof(Owner),
            new PropertyMetadata(0, (d, e) => seen.Add(e)));
        var dp = key.DependencyProperty;
        var o = new Owner();
        o.SetValue(key, 7);
        Assert.Equal(1, seen.Count);

        Assert.Throws<InvalidOperationException>(() => o.ClearValue(dp));
        Assert.Equal(7, o.GetValue(dp));
        Assert.Equal(1, seen.Count);
    }

    [Fact]
    public void Wrong_typed_write_rejected_like_native()
    {
        int coerceCalls = 0;
        var seen = new List<DependencyPropertyChangedEventArgs>();
        var dp = DependencyProperty.Register("ParityWrongType", typeof(int), typeof(Owner),
            new PropertyMetadata(0, (d, e) => seen.Add(e), (_, v) => { coerceCalls++; return v; }),
            v => (int)v >= 0);
        var o = new Owner();

        // Ordinary write: ArgumentException, no coercion, no notification, state kept.
        Assert.Throws<ArgumentException>(() => o.SetValue(dp, "bad"));
        Assert.Equal(0, coerceCalls);
        Assert.Empty(seen);
        Assert.Equal(0, o.GetValue(dp));

        // Null into a value-type property: same rejection, state kept.
        Assert.Throws<ArgumentException>(() => o.SetValue(dp, null));
        Assert.Equal(0, coerceCalls);
        Assert.Empty(seen);
        Assert.Equal(0, o.GetValue(dp));

        // Current-value path validates the same way.
        Assert.Throws<ArgumentException>(() => o.SetCurrentValue(dp, "bad"));
        Assert.Equal(0, coerceCalls);
        Assert.Empty(seen);
        Assert.Equal(0, o.GetValue(dp));

        // Null into a reference-type property stays accepted.
        var rp = DependencyProperty.Register("ParityNullRef", typeof(string), typeof(Owner),
            new PropertyMetadata("dflt"));
        o.SetValue(rp, null);
        Assert.Null(o.GetValue(rp));
    }

    [Fact]
    public void Authorized_key_write_coerces_and_validates_like_native()
    {
        int coerceCalls = 0;
        var seen = new List<DependencyPropertyChangedEventArgs>();
        var key = DependencyProperty.RegisterReadOnly("ParityKeyCoerced", typeof(int), typeof(Owner),
            new PropertyMetadata(0, (d, e) => seen.Add(e), (_, v) => { coerceCalls++; return Math.Max(0, (int)v); }));
        var dp = key.DependencyProperty;
        var o = new Owner();

        // Privileged write runs the coerce hook: -3 lands as 0 with one notification.
        o.SetValue(key, 7);
        Assert.Equal(1, coerceCalls);
        o.SetValue(key, -3);
        Assert.Equal(0, o.GetValue(dp));
        Assert.Equal(2, coerceCalls);
        Assert.Equal(2, seen.Count);
        Assert.Equal(7, seen[1].OldValue);
        Assert.Equal(0, seen[1].NewValue);

        // Wrong-typed and null key writes are rejected; state and callbacks untouched.
        Assert.Throws<ArgumentException>(() => o.SetValue(key, "bad"));
        Assert.Throws<ArgumentException>(() => o.SetValue(key, null));
        Assert.Equal(2, coerceCalls);
        Assert.Equal(2, seen.Count);
        Assert.Equal(0, o.GetValue(dp));
    }

    [Fact]
    public void Authorized_readonly_clear_resets_and_notifies()
    {
        var seen = new List<DependencyPropertyChangedEventArgs>();
        var key = DependencyProperty.RegisterReadOnly("ParityClearable", typeof(int), typeof(Owner),
            new PropertyMetadata(0, (d, e) => seen.Add(e)));
        var dp = key.DependencyProperty;
        var o = new Owner();
        o.SetValue(key, 7);
        Assert.Equal(7, o.GetValue(dp));

        o.ClearValue(key);
        Assert.Equal(0, o.GetValue(dp));
        Assert.Equal(2, seen.Count);

        // Keyed UnsetValue routes to the authorized clear, not the guard.
        o.SetValue(key, 9);
        o.SetValue(key, DependencyProperty.UnsetValue);
        Assert.Equal(0, o.GetValue(dp));
    }

    [Fact]
    public void Invalid_default_rejected_at_registration_like_native()
    {
        var ex = Assert.Throws<ArgumentException>(() => DependencyProperty.Register("ParityBadDefault",
            typeof(int), typeof(Owner), new PropertyMetadata(-1), v => (int)v >= 0));
        Assert.Contains("ParityBadDefault", ex.Message, StringComparison.Ordinal);

        // Failed registration leaves no residue: the name registers cleanly after.
        var dp = DependencyProperty.Register("ParityBadDefault", typeof(int), typeof(Owner),
            new PropertyMetadata(0), v => (int)v >= 0);
        var o = new Owner();
        Assert.Equal(0, o.GetValue(dp));
        Assert.Throws<ArgumentException>(() => o.SetValue(dp, -1));
    }

    [Fact]
    public void Null_registration_arguments_rejected_like_native()
    {
        static ValidateValueCallback AnyValidator() => v => true;

        var exName = Assert.Throws<ArgumentNullException>(() => DependencyProperty.Register(null!,
            typeof(int), typeof(Owner), new PropertyMetadata(0), AnyValidator()));
        Assert.Equal("name", exName.ParamName);

        var exType = Assert.Throws<ArgumentNullException>(() => DependencyProperty.Register("ParityNullArg",
            null!, typeof(Owner), new PropertyMetadata(0), AnyValidator()));
        Assert.Equal("propertyType", exType.ParamName);

        var exOwner = Assert.Throws<ArgumentNullException>(() => DependencyProperty.Register("ParityNullArg",
            typeof(int), null!, new PropertyMetadata(0), AnyValidator()));
        Assert.Equal("ownerType", exOwner.ParamName);

        // Rejected registrations leave no residue: valid registration works after.
        var dp = DependencyProperty.Register("ParityNullArg", typeof(int), typeof(Owner),
            new PropertyMetadata(3), AnyValidator());
        Assert.Equal(3, new Owner().GetValue(dp));
    }
}
