using System.ComponentModel;
using DesktopWasm.Graphics;

namespace DesktopWasm.Wpf.Tests;

/// <summary>SimpleBinding v1: DataContext inheritance, {Binding} push, INPC updates.</summary>
[ResetAmbientState]
public sealed class BindingTests
{
    private sealed class Address
    {
        public string City { get; set; } = string.Empty;
    }

    private sealed class Person : INotifyPropertyChanged
    {
        private string _name = string.Empty;
        private int _age;

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Name
        {
            get => _name;
            set { if (_name == value) return; _name = value; Changed(nameof(Name)); }
        }

        public int Age
        {
            get => _age;
            set { if (_age == value) return; _age = value; Changed(nameof(Age)); }
        }

        public Address Address { get; } = new();

        private void Changed(string property) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));

        public override string ToString() => $"{Name} ({Age})";
    }

    private sealed class Plain
    {
        public string? Title { get; set; }
    }

    [Fact]
    public void SetBinding_pushes_initial_value()
    {
        var text = new TextBlock();
        text.SetBinding("Text", new Binding("Name") { Source = new Person { Name = "Ada" } });
        Assert.Equal("Ada", text.Text);
    }

    [Fact]
    public void Inpc_change_flows_to_target()
    {
        var vm = new Person { Name = "Ada", Age = 36 };
        var name = new TextBlock();
        var age = new TextBlock();
        name.SetBinding("Text", new Binding("Name") { Source = vm });
        age.SetBinding("Text", new Binding("Age") { Source = vm });

        vm.Name = "Grace";
        vm.Age = 85;

        Assert.Equal("Grace", name.Text);
        Assert.Equal("85", age.Text); // int converts to string
    }

    [Fact]
    public void Unrelated_property_change_leaves_other_targets_alone()
    {
        var vm = new Person { Name = "Ada", Age = 36 };
        var name = new TextBlock();
        name.SetBinding("Text", new Binding("Name") { Source = vm });
        vm.Age = 37;
        Assert.Equal("Ada", name.Text);
    }

    [Fact]
    public void DataContext_inherits_through_parents()
    {
        var window = new Window { DataContext = new Person { Name = "Ada" } };
        var panel = new StackPanel();
        var text = new TextBlock();
        window.Content = panel;
        panel.AddChild(text);

        text.SetBinding("Text", new Binding("Name"));

        Assert.Equal("Ada", text.Text);
    }

    [Fact]
    public void DataContext_set_after_binding_still_resolves()
    {
        var text = new TextBlock();
        text.SetBinding("Text", new Binding("Name"));
        Assert.Equal(string.Empty, text.Text);

        text.DataContext = new Person { Name = "Ada" };
        Assert.Equal("Ada", text.Text);
    }

    [Fact]
    public void Child_added_after_DataContext_picks_it_up()
    {
        var window = new Window { DataContext = new Person { Name = "Ada" } };
        var panel = new StackPanel();
        window.Content = panel;

        var text = new TextBlock();
        text.SetBinding("Text", new Binding("Name")); // no context yet
        panel.AddChild(text); // now inherits the window's context

        Assert.Equal("Ada", text.Text);
    }

    [Fact]
    public void DataContext_swap_reevaluates_and_unsubscribes_old_source()
    {
        var oldVm = new Person { Name = "Ada" };
        var newVm = new Person { Name = "Grace" };
        var window = new Window { DataContext = oldVm };
        var text = new TextBlock();
        window.Content = text;
        text.SetBinding("Text", new Binding("Name"));

        window.DataContext = newVm;
        Assert.Equal("Grace", text.Text);

        oldVm.Name = "Nobody";
        Assert.Equal("Grace", text.Text); // stale source no longer pushes
        newVm.Name = "Hopper";
        Assert.Equal("Hopper", text.Text);
    }

    [Fact]
    public void Missing_path_leaves_target_untouched()
    {
        var text = new TextBlock { Text = "keep" };
        text.SetBinding("Text", new Binding("Nope") { Source = new Person() });
        Assert.Equal("keep", text.Text);
    }

    [Fact]
    public void Null_DataContext_leaves_target_untouched()
    {
        var text = new TextBlock { Text = "keep" };
        text.SetBinding("Text", new Binding("Name"));
        Assert.Equal("keep", text.Text);
    }

    [Fact]
    public void Dotted_path_reads_nested_property()
    {
        var vm = new Person();
        vm.Address.City = "Iasi";
        var text = new TextBlock();
        text.SetBinding("Text", new Binding("Address.City") { Source = vm });
        Assert.Equal("Iasi", text.Text);
    }

    [Fact]
    public void OneTime_does_not_subscribe()
    {
        var vm = new Person { Name = "Ada" };
        var text = new TextBlock();
        var expr = text.SetBinding("Text", new Binding("Name") { Source = vm, Mode = BindingMode.OneTime });
        Assert.Equal("Ada", text.Text);
        vm.Name = "Grace";
        Assert.Equal("Ada", text.Text); // pushed once, never subscribed
        Assert.Same(vm, expr.Source);
    }

    [Fact]
    public void Detach_stops_updates()
    {
        var vm = new Person { Name = "Ada" };
        var text = new TextBlock();
        var expr = text.SetBinding("Text", new Binding("Name") { Source = vm });
        expr.Detach();
        vm.Name = "Grace";
        Assert.Equal("Ada", text.Text);
        Assert.Empty(text.ActiveBindings);
    }

    [Fact]
    public void Plain_source_without_inpc_binds_once()
    {
        var text = new TextBlock();
        text.SetBinding("Text", new Binding("Title") { Source = new Plain { Title = "Hi" } });
        Assert.Equal("Hi", text.Text);
    }

    [Fact]
    public void Empty_path_binds_the_source_itself()
    {
        var text = new TextBlock();
        text.SetBinding("Text", new Binding("") { Source = new Person { Name = "Ada", Age = 36 } });
        Assert.Equal("Ada (36)", text.Text);
    }

    [Fact]
    public void Unknown_target_property_throws()
    {
        var text = new TextBlock();
        Assert.Throws<InvalidOperationException>(() => text.SetBinding("Nope", new Binding("Name")));
    }

    [Fact]
    public void TwoWay_pushes_target_edits_to_source()
    {
        var vm = new Person { Name = "Ada" };
        var text = new TextBlock();
        var expr = text.SetBinding("Text", new Binding("Name") { Source = vm, Mode = BindingMode.TwoWay });

        // Simulates an editor pushing after a user edit.
        text.Text = "Grace";
        expr.UpdateSource();
        Assert.Equal("Grace", vm.Name);

        // OneWay bindings never push back.
        var oneWay = new TextBlock();
        oneWay.SetBinding("Text", new Binding("Name") { Source = vm });
        oneWay.Text = "Nobody";
        Assert.Equal("Grace", vm.Name);
    }

    [Fact]
    public void TwoWay_skips_equal_values_breaking_update_cycles()
    {
        var vm = new Person { Name = "Ada" };
        var changes = 0;
        vm.PropertyChanged += (_, _) => changes++;
        var text = new TextBlock();
        var expr = text.SetBinding("Text", new Binding("Name") { Source = vm, Mode = BindingMode.TwoWay });
        expr.UpdateSource(); // source already holds "Ada": no write, no event
        Assert.Equal(0, changes);
    }

    [Fact]
    public void Rendered_frame_reflects_bound_updates()
    {
        var vm = new Person { Name = "Ada" };
        var window = new Window { DataContext = vm };
        var text = new TextBlock();
        window.Content = text;
        text.SetBinding("Text", new Binding("Name"));

        static string Drawn(Window w) => string.Concat(
            WasmMilCore.RenderHeadless(w, new WpfSize(800, 600))
                .OfType<DrawCommand.DrawText>().Select(d => d.Text));
        Assert.Equal("Ada", Drawn(window));
        vm.Name = "Grace";
        Assert.Equal("Grace", Drawn(window));
    }

    [Fact]
    public void Xaml_binding_loads_and_updates()
    {
        var root = (Window)XamlLoader.Load("""
            <Window><StackPanel><TextBlock Text="{Binding Name}"/></StackPanel></Window>
            """).Root;
        var text = (TextBlock)((StackPanel)root.Content!).LogicalChildren[0];
        var vm = new Person { Name = "Ada" };
        root.DataContext = vm;
        Assert.Equal("Ada", text.Text);
        vm.Name = "Grace";
        Assert.Equal("Grace", text.Text);
    }

    [Fact]
    public void Xaml_binding_forms()
    {
        var root = (Window)XamlLoader.Load("""
            <Window><StackPanel>
              <TextBlock Text="{Binding Path=Name, Mode=OneTime}"/>
              <TextBlock Text="{Binding}"/>
            </StackPanel></Window>
            """).Root;
        var panel = (StackPanel)root.Content!;
        root.DataContext = new Person { Name = "Ada", Age = 36 };
        Assert.Equal("Ada", ((TextBlock)panel.LogicalChildren[0]).Text);
        Assert.Equal("Ada (36)", ((TextBlock)panel.LogicalChildren[1]).Text);
    }

    [Fact]
    public void Xaml_rejects_non_binding_extensions()
    {
        Assert.Throws<XamlLoadException>(() => XamlLoader.Load(
            """<Window><TextBlock Text="{StaticResource x}"/></Window>"""));
    }

    [Fact]
    public void Xaml_rejects_bad_binding_options()
    {
        Assert.Throws<XamlLoadException>(() => XamlLoader.Load(
            """<Window><TextBlock Text="{Binding Foo=Bar}"/></Window>"""));
        Assert.Throws<XamlLoadException>(() => XamlLoader.Load(
            """<Window><TextBlock Text="{Binding Name, Mode=Sideways}"/></Window>"""));
        Assert.Throws<XamlLoadException>(() => XamlLoader.Load(
            """<Window><TextBlock Text="{Binding Name, UpdateSourceTrigger=Sideways}"/></Window>"""));
        Assert.Throws<XamlLoadException>(() => XamlLoader.Load(
            """<Window><TextBlock Text="{Binding Name, Converter=x}"/></Window>"""));
        Assert.Throws<XamlLoadException>(() => XamlLoader.Load(
            """<Window><TextBlock Nope="{Binding Name}"/></Window>"""));
        Assert.Throws<XamlLoadException>(() => XamlLoader.Load(
            """<Window><TextBlock Text="{Binding Name"/></Window>"""));
    }

    [Fact]
    public void Xaml_accepts_step43_binding_options()
    {
        // ElementName/UpdateSourceTrigger/FallbackValue/TargetNullValue/StringFormat (Step 43).
        var root = (Window)XamlLoader.Load("""
            <Window><StackPanel>
              <TextBox Name="src" Text="hi"/>
              <TextBlock Text="{Binding Text, ElementName=src, UpdateSourceTrigger=Explicit}"/>
              <TextBlock Text="{Binding Nope, FallbackValue=fb}"/>
              <TextBlock Text="{Binding Missing, TargetNullValue=tn}"/>
            </StackPanel></Window>
            """).Root;
        var panel = (StackPanel)root.Content!;
        Assert.Equal("hi", ((TextBlock)panel.LogicalChildren[1]).Text);
        Assert.Equal("fb", ((TextBlock)panel.LogicalChildren[2]).Text);
    }

    [Fact]
    public void Xaml_binding_string_format_strips_xaml_escape()
    {
        // WPFGallery PageHeader shape: StringFormat with the '{}' escape plus
        // a nested RelativeSource (validated, source recorded by path).
        var root = (Window)XamlLoader.Load("""
            <Window><StackPanel>
              <TextBlock Text="{Binding Title, StringFormat='{}{0} Page'}"/>
            </StackPanel></Window>
            """).Root;
        var panel = (StackPanel)root.Content!;
        var text = (TextBlock)panel.LogicalChildren[0];
        text.DataContext = new FormatProbe("Home");
        Assert.Equal("Home Page", text.Text);
    }

    private sealed class FormatProbe
    {
        public FormatProbe(string title) => Title = title;
        public string Title { get; }
    }
}
