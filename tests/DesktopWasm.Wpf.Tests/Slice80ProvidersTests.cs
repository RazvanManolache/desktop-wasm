using System.Collections;
using System.ComponentModel;

namespace DesktopWasm.Wpf.Tests;

// Slice-80 HandyControl lib ObjectDataProvider/TextSearch pair: the two
// remaining convertible lib rows (handycontrol conversion-failed/4/71, split
// 0/11/60, all images sha256-verified; TextSearch + ObjectDataProvider are
// the lib's 2 of 11). Every behavior below is live-verified against the .NET
// 10 WindowsDesktop reference assemblies plus a real-WPF STA probe: Text and
// TextPath default to String.Empty (never null); each provider setter
// re-queries synchronously; BeginInit/EndInit and DeferRefresh collapse to a
// single trailing query; a missing method lands in Error as
// MissingMethodException ("Method 'FullType.Name' not found.") with Data
// null and DataChanged still raised; IsInitialLoadEnabled=false gates
// InitialLoad; ShouldSerialize* predicates track set state.
[ResetAmbientState]
public sealed class Slice80ProvidersTests
{
    private sealed class Stringy
    {
        public string? Label { get; set; }

        public string Make(object? arg) => "made:" + arg;
    }

    // Mirrors the converted HandyControl.Data.EnumDataProvider IL exactly:
    // ctor presets MethodName to GetValues; set_Type appends the enum Type
    // to MethodParameters and points ObjectType at Enum; set_UseAttributes
    // switches ObjectType between this type and Enum.
    private sealed class EnumHarness : ObjectDataProvider
    {
        private Type? _type;
        private bool _useAttributes;

        public EnumHarness() => MethodName = "GetValues";

        public Type? Type
        {
            get => _type;
            set
            {
                _type = value;
                MethodParameters.Add(value);
                ObjectType = typeof(Enum);
            }
        }

        public bool UseAttributes
        {
            get => _useAttributes;
            set
            {
                _useAttributes = value;
                ObjectType = value ? GetType() : typeof(Enum);
            }
        }
    }

    [Fact]
    public void Provider_defaults_match_the_framework()
    {
        var provider = new ObjectDataProvider();
        Assert.True(provider is DataSourceProvider);
        Assert.True(provider.IsInitialLoadEnabled);
        Assert.False(provider.IsAsynchronous);
        Assert.Null(provider.Data);
        Assert.Null(provider.Error);
        Assert.NotNull(provider.ConstructorParameters);
        Assert.NotNull(provider.MethodParameters);
        Assert.Empty(provider.ConstructorParameters.Cast<object?>());
        Assert.Empty(provider.MethodParameters.Cast<object?>());
        Assert.False(provider.ShouldSerializeConstructorParameters());
        Assert.False(provider.ShouldSerializeMethodParameters());
        Assert.False(provider.ShouldSerializeObjectInstance());
        Assert.False(provider.ShouldSerializeObjectType());
        Assert.True(typeof(DataSourceProvider).IsAbstract);
        Assert.True(typeof(INotifyPropertyChanged).IsAssignableFrom(typeof(DataSourceProvider)));
        Assert.True(typeof(ISupportInitialize).IsAssignableFrom(typeof(DataSourceProvider)));
    }

    [Fact]
    public void Instance_method_query_flows_with_events()
    {
        var provider = new ObjectDataProvider();
        var changed = 0;
        var notified = new List<string?>();
        provider.DataChanged += (_, _) => changed++;
        ((INotifyPropertyChanged)provider).PropertyChanged += (_, e) => notified.Add(e.PropertyName);
        provider.ObjectInstance = new Stringy();
        // No method yet: Data is the instance itself, like real WPF.
        Assert.Same(provider.ObjectInstance, provider.Data);
        Assert.Equal(1, changed);
        Assert.Equal(["Data"], notified);
        provider.MethodName = "Make";
        // Arity mismatch now: Data null, no throw (MissingMethod path).
        Assert.Null(provider.Data);
        Assert.Equal(2, changed);
        provider.MethodParameters.Add(42);
        Assert.Equal("made:42", provider.Data);
        Assert.Null(provider.Error);
        Assert.Equal(3, changed);
        Assert.Equal(["Data", "Data", "Data"], notified);
        provider.Refresh();
        Assert.Equal("made:42", provider.Data);
        Assert.Equal(4, changed);
    }

    [Fact]
    public void Missing_method_lands_in_Error_with_the_framework_message()
    {
        var provider = new ObjectDataProvider();
        var changed = 0;
        provider.DataChanged += (_, _) => changed++;
        provider.ObjectType = typeof(string);
        Assert.Equal(1, changed);
        provider.MethodName = "NoSuchMethod";
        Assert.Null(provider.Data);
        Assert.Equal(2, changed);
        var error = Assert.IsType<MissingMethodException>(provider.Error);
        Assert.Equal("Method 'System.String.NoSuchMethod' not found.", error.Message);
    }

    [Fact]
    public void Enum_harness_drives_the_converted_static_shape()
    {
        // Exact converted HandyControl sequence: ctor presets GetValues,
        // set_Type appends the enum type and points at Enum.
        var provider = new EnumHarness();
        var changed = 0;
        provider.DataChanged += (_, _) => changed++;
        provider.Type = typeof(DayOfWeek);
        Assert.Null(provider.Error);
        var days = Assert.IsAssignableFrom<Array>(provider.Data);
        Assert.Equal(7, days.Length);
        Assert.Equal([DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Tuesday],
            days.Cast<DayOfWeek>().Take(3));
        Assert.True(changed >= 1);
        Assert.True(provider.ShouldSerializeMethodParameters());
        Assert.True(provider.ShouldSerializeObjectType());
        Assert.False(provider.ShouldSerializeConstructorParameters());
        Assert.False(provider.ShouldSerializeObjectInstance());
    }

    [Fact]
    public void Open_generic_static_invocation_propagates_like_the_framework()
    {
        // Real-WPF parity pin: with zero parameters the binder resolves the
        // open generic Enum.GetValues<TEnum>() and Invoke late-binds, so the
        // InvalidOperationException escapes the setter instead of landing in
        // Error. The converted lib always carries its parameter by EndInit,
        // so this only documents the quirk.
        var provider = new EnumHarness();
        Assert.Throws<InvalidOperationException>(() => provider.ObjectType = typeof(Enum));
    }

    [Fact]
    public void BeginInit_collapses_to_a_single_trailing_query()
    {
        var provider = new EnumHarness();
        var changed = 0;
        provider.DataChanged += (_, _) => changed++;
        ((ISupportInitialize)provider).BeginInit();
        provider.Type = typeof(DayOfWeek);
        Assert.Null(provider.Data);
        Assert.Equal(0, changed);
        ((ISupportInitialize)provider).EndInit();
        Assert.Equal(1, changed);
        Assert.Equal(7, Assert.IsAssignableFrom<Array>(provider.Data).Length);
        Assert.Null(provider.Error);
    }

    [Fact]
    public void DeferRefresh_runs_one_query_on_dispose()
    {
        var provider = new ObjectDataProvider();
        var changed = 0;
        provider.DataChanged += (_, _) => changed++;
        using (provider.DeferRefresh())
        {
            provider.ObjectInstance = new Stringy();
            provider.MethodName = "Make";
            provider.MethodParameters.Add(1);
            Assert.Null(provider.Data);
            Assert.Equal(0, changed);
        }
        Assert.Equal("made:1", provider.Data);
        Assert.Equal(1, changed);
    }

    [Fact]
    public void InitialLoad_is_gated_but_Refresh_is_not()
    {
        var provider = new ObjectDataProvider { IsInitialLoadEnabled = false };
        provider.ObjectInstance = new Stringy();
        provider.MethodName = "Make";
        Assert.Null(provider.Data);
        provider.InitialLoad();
        Assert.Null(provider.Data);
        provider.Refresh();
        Assert.Null(provider.Data);
        Assert.IsType<MissingMethodException>(provider.Error);
    }

    [Fact]
    public void TextSearch_defaults_are_empty_not_null()
    {
        var element = new DependencyObject();
        Assert.Equal(string.Empty, TextSearch.GetText(element));
        Assert.Equal(string.Empty, TextSearch.GetTextPath(element));
        Assert.NotNull(TextSearch.GetText(element));
        Assert.NotNull(TextSearch.GetTextPath(element));
    }

    [Fact]
    public void TextSearch_round_trips_and_rejects_null_elements()
    {
        var element = new DependencyObject();
        TextSearch.SetText(element, "Abc");
        Assert.Equal("Abc", TextSearch.GetText(element));
        TextSearch.SetTextPath(element, "Name");
        Assert.Equal("Name", TextSearch.GetTextPath(element));
        Assert.Same(TextSearch.TextProperty, DependencyProperty.FromName(typeof(TextSearch), "Text"));
        Assert.Same(TextSearch.TextPathProperty, DependencyProperty.FromName(typeof(TextSearch), "TextPath"));
        Assert.Throws<ArgumentNullException>(() => TextSearch.GetText(null!));
        Assert.Throws<ArgumentNullException>(() => TextSearch.SetText(null!, "x"));
        Assert.Throws<ArgumentNullException>(() => TextSearch.GetTextPath(null!));
        Assert.Throws<ArgumentNullException>(() => TextSearch.SetTextPath(null!, "x"));
    }

    [Fact]
    public void TextSearch_binding_mirrors_the_converted_GetString_shape()
    {
        // Exact converted HandyControl.Tools.Helper.BindingHelper.GetString
        // shape: a fresh DependencyObject, a TwoWay path binding with the
        // value as Source, SetBinding onto TextSearch.TextProperty, read
        // back, then ClearBinding.
        var item = new Stringy { Label = "Volume" };
        var target = new DependencyObject();
        var binding = new Binding("Label") { Mode = BindingMode.TwoWay, Source = item };
        BindingOperations.SetBinding(target, TextSearch.TextProperty, binding);
        Assert.Equal("Volume", target.GetValue(TextSearch.TextProperty));
        BindingOperations.ClearBinding(target, TextSearch.TextProperty);
        Assert.Equal(string.Empty, target.GetValue(TextSearch.TextProperty));
    }
}
