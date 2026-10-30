using System.Collections;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Slice-83 FreezableCollection&lt;T&gt;/Enumerator regression tests: the
/// HandyControl lib keeps one nested-type row for the compiler-generated
/// foreach machinery inside its own Interactivity behavior system
/// (AttachableCollection&lt;T&gt;.OnCollectionChanged,
/// Behavior/TriggerAction/Trigger collections OnAttached/OnDetaching,
/// TriggerBase.InvokeActions — verified member-by-member against the
/// upstream net10.0-windows HandyControl.dll with Mono.Cecil, all
/// get_Current/MoveNext/constrained/local positions over
/// FreezableCollection`1/Enumerator instantiations). The engine now declares
/// the nested struct with the live 10.0.13 PresentationCore shape plus
/// version-stamped invalidation, and the converter maps nested types on
/// generic outers, so no Application state is touched and these stay outside
/// the serialized ApplicationState collection.
/// </summary>
[ResetAmbientState]
public sealed class Slice83FreezableEnumeratorTests
{
    [Fact]
    public void Nested_enumerator_matches_live_wpf_shape()
    {
        var nested = typeof(FreezableCollection<>).GetNestedType("Enumerator");
        Assert.NotNull(nested);
        Assert.True(nested!.IsValueType);
        var closed = nested.MakeGenericType(typeof(VisualState));
        Assert.True(typeof(IEnumerator<VisualState>).IsAssignableFrom(closed));
        Assert.True(typeof(IEnumerator).IsAssignableFrom(closed));
        Assert.True(typeof(IDisposable).IsAssignableFrom(closed));
        Assert.NotNull(closed.GetMethod("get_Current", Type.EmptyTypes));
        Assert.Equal(typeof(VisualState), closed.GetMethod("get_Current", Type.EmptyTypes)!.ReturnType);
        Assert.NotNull(closed.GetMethod("MoveNext", Type.EmptyTypes));
        Assert.Equal(typeof(bool), closed.GetMethod("MoveNext", Type.EmptyTypes)!.ReturnType);
        Assert.NotNull(closed.GetMethod("Reset", Type.EmptyTypes));
        Assert.NotNull(typeof(FreezableCollection<VisualState>).GetMethod("GetEnumerator", Type.EmptyTypes));
        Assert.Equal(closed,
            typeof(FreezableCollection<VisualState>).GetMethod("GetEnumerator", Type.EmptyTypes)!.ReturnType);
    }

    [Fact]
    public void Foreach_iterates_in_order()
    {
        var states = new FreezableCollection<VisualState>
        {
            new VisualState { Name = "On" },
            new VisualState { Name = "Off" },
        };
        var seen = new List<string?>();
        foreach (var state in states)
            seen.Add(state.Name);
        Assert.Equal(["On", "Off"], seen);
    }

    [Fact]
    public void Struct_enumerator_supports_reset_and_explicit_current()
    {
        var states = new FreezableCollection<VisualState>
        {
            new VisualState { Name = "On" },
            new VisualState { Name = "Off" },
        };
        using var enumerator = states.GetEnumerator();
        Assert.True(enumerator.MoveNext());
        Assert.Same(states[0], enumerator.Current);
        Assert.True(enumerator.MoveNext());
        enumerator.Reset();
        Assert.True(enumerator.MoveNext());
        Assert.Same(states[0], enumerator.Current);
        Assert.Equal(states[0], ((IEnumerator)enumerator).Current);
        Assert.False(new FreezableCollection<VisualState>().GetEnumerator().MoveNext());
    }

    [Fact]
    public void Current_throws_before_start_and_after_end()
    {
        var states = new FreezableCollection<VisualState> { new VisualState { Name = "On" } };
        using var enumerator = states.GetEnumerator();
        Assert.Throws<InvalidOperationException>(() => enumerator.Current);
        Assert.True(enumerator.MoveNext());
        Assert.False(enumerator.MoveNext());
        Assert.Throws<InvalidOperationException>(() => enumerator.Current);
    }

    [Fact]
    public void Mutation_during_enumeration_throws_like_wpf()
    {
        var states = new FreezableCollection<VisualState>
        {
            new VisualState { Name = "On" },
            new VisualState { Name = "Off" },
        };
        using var enumerator = states.GetEnumerator();
        Assert.True(enumerator.MoveNext());
        states.Add(new VisualState { Name = "Late" });
        Assert.Throws<InvalidOperationException>(() => enumerator.MoveNext());
        using var resetter = states.GetEnumerator();
        Assert.True(resetter.MoveNext());
        states.RemoveAt(0);
        Assert.Throws<InvalidOperationException>(() => resetter.Reset());
        using var setter = states.GetEnumerator();
        Assert.True(setter.MoveNext());
        states[0] = new VisualState { Name = "Swapped" };
        Assert.Throws<InvalidOperationException>(() => setter.MoveNext());
    }

    [Fact]
    public void Fresh_enumerator_after_mutation_sees_new_items()
    {
        var states = new FreezableCollection<VisualState> { new VisualState { Name = "On" } };
        using var first = states.GetEnumerator();
        Assert.True(first.MoveNext());
        states.Add(new VisualState { Name = "Off" });
        var seen = new List<string?>();
        foreach (var state in states)
            seen.Add(state.Name);
        Assert.Equal(["On", "Off"], seen);
    }

    [Fact]
    public void Interface_enumeration_still_flows()
    {
        IEnumerable<VisualState> states = new FreezableCollection<VisualState>
        {
            new VisualState { Name = "On" },
        };
        Assert.Single(states);
        Assert.Equal("On", ((IList)states)[0] is VisualState named ? named.Name : null);
    }
}
