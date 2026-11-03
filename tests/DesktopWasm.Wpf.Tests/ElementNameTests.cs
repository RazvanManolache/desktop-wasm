using System.ComponentModel;

namespace DesktopWasm.Wpf.Tests;

[ResetAmbientState]
public sealed class ElementNameTests
{
    public class OuterControl : UserControl
    {
        public static readonly DependencyProperty OuterHeaderProperty =
            DependencyProperty.Register(nameof(OuterHeader), typeof(string), typeof(OuterControl), new PropertyMetadata("DefaultOuter"));

        public string OuterHeader
        {
            get => (string)GetValue(OuterHeaderProperty);
            set => SetValue(OuterHeaderProperty, value);
        }
    }

    public class InnerControl : UserControl
    {
        public static readonly DependencyProperty InnerTitleProperty =
            DependencyProperty.Register(nameof(InnerTitle), typeof(string), typeof(InnerControl), new PropertyMetadata("DefaultInner"));

        public string InnerTitle
        {
            get => (string)GetValue(InnerTitleProperty);
            set => SetValue(InnerTitleProperty, value);
        }
    }

    private sealed class LocationViewModel : INotifyPropertyChanged
    {
        private string _city = string.Empty;
        private string _zip = string.Empty;

        public event PropertyChangedEventHandler? PropertyChanged;

        public string City
        {
            get => _city;
            set { if (_city == value) return; _city = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(City))); }
        }

        public string Zip
        {
            get => _zip;
            set { if (_zip == value) return; _zip = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Zip))); }
        }
    }

    [Fact]
    public void Markup_names_are_readable_by_original_application_event_handlers()
    {
        var loaded = XamlLoader.Load("""
            <StackPanel xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Button x:Name="BPlus" Content="+"/>
              <Button Name="BEquals" Content="="/>
            </StackPanel>
            """);
        Assert.Equal("BPlus", Assert.IsType<Button>(loaded.Names["BPlus"]).Name);
        Assert.Equal("BEquals", Assert.IsType<Button>(loaded.Names["BEquals"]).Name);
        var button = new Button();
        Assert.Equal(string.Empty, button.Name);
        button.SetValue(FrameworkElement.NameProperty, "Direct");
        Assert.Equal("Direct", button.Name);
    }

    [Fact]
    public void UserControl_RelativeSource_Self_FindAncestor_and_Level_resolution()
    {
        var outer = new OuterControl { OuterHeader = "OuterH", Tag = "OuterTag" };
        var grid = new Grid();
        outer.Content = grid;

        var inner = new InnerControl { InnerTitle = "InnerT", Tag = "InnerTag" };
        grid.AddChild(inner);

        var stack = new StackPanel();
        inner.Content = stack;

        var tbSelf = new TextBlock { Tag = "SelfTag" };
        tbSelf.SetBinding("Text", new Binding("Tag") { RelativeSource = RelativeSource.Self });

        var tbInner = new TextBlock();
        tbInner.SetBinding("Text", new Binding(nameof(InnerControl.InnerTitle))
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(InnerControl), 1)
        });

        var tbOuter = new TextBlock();
        tbOuter.SetBinding("Text", new Binding(nameof(OuterControl.OuterHeader))
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(OuterControl), 1)
        });

        var tbLevel1 = new TextBlock();
        tbLevel1.SetBinding("Text", new Binding("Tag")
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(UserControl), 1)
        });

        var tbLevel2 = new TextBlock();
        tbLevel2.SetBinding("Text", new Binding("Tag")
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(UserControl), 2)
        });

        stack.AddChild(tbSelf);
        stack.AddChild(tbInner);
        stack.AddChild(tbOuter);
        stack.AddChild(tbLevel1);
        stack.AddChild(tbLevel2);

        outer.Measure(new WpfSize(300, 300));
        outer.Arrange(new WpfRect(0, 0, 300, 300));

        // Initial resolutions
        Assert.Equal("SelfTag", tbSelf.Text);
        Assert.Equal("InnerT", tbInner.Text);
        Assert.Equal("OuterH", tbOuter.Text);
        Assert.Equal("InnerTag", tbLevel1.Text);
        Assert.Equal("OuterTag", tbLevel2.Text);

        // Dynamic dependency property updates on ancestors
        outer.OuterHeader = "NewOuterH";
        Assert.Equal("NewOuterH", tbOuter.Text);

        inner.InnerTitle = "NewInnerT";
        Assert.Equal("NewInnerT", tbInner.Text);
    }

    [Fact]
    public void UserControl_ElementName_bindings_across_nested_scopes()
    {
        var outer = new OuterControl();
        var grid = new Grid();
        outer.Content = grid;

        var outerBox = new TextBox { Name = "OuterBox", Text = "OuterInitial" };
        grid.AddChild(outerBox);

        var inner = new InnerControl();
        grid.AddChild(inner);

        var stack = new StackPanel();
        inner.Content = stack;

        var innerBox = new TextBox { Name = "InnerBox", Text = "InnerInitial" };
        stack.AddChild(innerBox);

        var boundToInner = new TextBlock();
        boundToInner.SetBinding("Text", new Binding("Text") { ElementName = "InnerBox" });
        stack.AddChild(boundToInner);

        var boundToOuter = new TextBlock();
        boundToOuter.SetBinding("Text", new Binding("Text") { ElementName = "OuterBox" });
        stack.AddChild(boundToOuter);

        var twoWayInner = new TextBox();
        var twoWayExpr = twoWayInner.SetBinding("Text", new Binding("Text") { ElementName = "InnerBox", Mode = BindingMode.TwoWay });
        stack.AddChild(twoWayInner);

        outer.Measure(new WpfSize(400, 400));
        outer.Arrange(new WpfRect(0, 0, 400, 400));

        // Initial resolutions
        Assert.Equal("InnerInitial", boundToInner.Text);
        Assert.Equal("OuterInitial", boundToOuter.Text);
        Assert.Equal("InnerInitial", twoWayInner.Text);

        // Updates to inner element propagate
        innerBox.Text = "InnerChanged";
        Assert.Equal("InnerChanged", boundToInner.Text);
        Assert.Equal("InnerChanged", twoWayInner.Text);

        // Updates to outer element propagate
        outerBox.Text = "OuterChanged";
        Assert.Equal("OuterChanged", boundToOuter.Text);

        // TwoWay updates push back to source element
        twoWayInner.Text = "PushedFromTwoWay";
        twoWayExpr.UpdateSource();
        Assert.Equal("PushedFromTwoWay", innerBox.Text);
        Assert.Equal("PushedFromTwoWay", boundToInner.Text);
    }

    [Fact]
    public void UserControl_RelativeSource_and_ElementName_with_nested_INPC_DataContexts()
    {
        var outerVm = new LocationViewModel { City = "Seattle", Zip = "98101" };
        var innerVm = new LocationViewModel { City = "Tacoma", Zip = "98402" };

        var outer = new OuterControl { DataContext = outerVm };
        var grid = new Grid();
        outer.Content = grid;

        var inner = new InnerControl { DataContext = innerVm };
        grid.AddChild(inner);

        var stack = new StackPanel();
        inner.Content = stack;

        var tbOuterCity = new TextBlock();
        tbOuterCity.SetBinding("Text", new Binding("DataContext.City")
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(OuterControl), 1)
        });

        var tbInnerZip = new TextBlock();
        tbInnerZip.SetBinding("Text", new Binding("DataContext.Zip")
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(InnerControl), 1)
        });

        stack.AddChild(tbOuterCity);
        stack.AddChild(tbInnerZip);

        outer.Measure(new WpfSize(300, 300));
        outer.Arrange(new WpfRect(0, 0, 300, 300));

        Assert.Equal("Seattle", tbOuterCity.Text);
        Assert.Equal("98402", tbInnerZip.Text);

        // INPC changes in independent view models notify targets correctly
        outerVm.City = "Bellevue";
        Assert.Equal("Bellevue", tbOuterCity.Text);

        innerVm.Zip = "98405";
        Assert.Equal("98405", tbInnerZip.Text);
    }

    [Fact]
    public void XamlLoader_forward_referencing_ElementName_in_nested_UserControl()
    {
        var loaded = XamlLoader.Load("""
            <UserControl>
              <Grid>
                <StackPanel Name="InnerPanel">
                  <TextBlock Name="Consumer" Text="{Binding Text, ElementName=Supplier}"/>
                  <TextBox Name="Supplier" Text="SuppliedByForwardRef"/>
                </StackPanel>
              </Grid>
            </UserControl>
            """);

        var root = (UserControl)loaded.Root;
        var consumer = (TextBlock)loaded.Names["Consumer"];
        var supplier = (TextBox)loaded.Names["Supplier"];

        root.Measure(new WpfSize(300, 300));
        root.Arrange(new WpfRect(0, 0, 300, 300));

        Assert.Equal("SuppliedByForwardRef", consumer.Text);

        supplier.Text = "SupplierUpdated";
        Assert.Equal("SupplierUpdated", consumer.Text);
    }

    [Fact]
    public void XamlLoader_RelativeSource_Self_and_FindAncestor_levels_in_nested_UserControls()
    {
        var loaded = XamlLoader.Load("""
            <UserControl Tag="OuterTag">
              <Grid>
                <UserControl Tag="InnerTag">
                  <StackPanel>
                    <TextBlock Name="TbSelf" Tag="SelfValue" Text="{Binding Tag, RelativeSource={RelativeSource Self}}" />
                    <TextBlock Name="TbInner" Text="{Binding Tag, RelativeSource={RelativeSource Mode=FindAncestor, AncestorType={x:Type UserControl}, AncestorLevel=1}}" />
                    <TextBlock Name="TbOuter" Text="{Binding Tag, RelativeSource={RelativeSource Mode=FindAncestor, AncestorType={x:Type UserControl}, AncestorLevel=2}}" />
                  </StackPanel>
                </UserControl>
              </Grid>
            </UserControl>
            """);

        var root = (UserControl)loaded.Root;
        var tbSelf = (TextBlock)loaded.Names["TbSelf"];
        var tbInner = (TextBlock)loaded.Names["TbInner"];
        var tbOuter = (TextBlock)loaded.Names["TbOuter"];

        root.Measure(new WpfSize(300, 300));
        root.Arrange(new WpfRect(0, 0, 300, 300));

        Assert.Equal("SelfValue", tbSelf.Text);
        Assert.Equal("InnerTag", tbInner.Text);
        Assert.Equal("OuterTag", tbOuter.Text);

        tbSelf.Tag = "SelfUpdated";
        Assert.Equal("SelfUpdated", tbSelf.Text);
    }

    [Fact]
    public void XamlLoader_nested_UserControl_ElementName_and_RelativeSource_combined()
    {
        var loaded = XamlLoader.Load("""
            <UserControl Tag="ControlRoot">
              <StackPanel>
                <TextBox Name="SharedInput" Text="SharedData" />
                <UserControl Tag="SubControl">
                  <StackPanel>
                    <TextBlock Name="BoundToShared" Text="{Binding Text, ElementName=SharedInput}" />
                    <TextBlock Name="BoundToAncestor" Text="{Binding Tag, RelativeSource={RelativeSource Mode=FindAncestor, AncestorType={x:Type UserControl}, AncestorLevel=2}}" />
                  </StackPanel>
                </UserControl>
              </StackPanel>
            </UserControl>
            """);

        var root = (UserControl)loaded.Root;
        var input = (TextBox)loaded.Names["SharedInput"];
        var boundShared = (TextBlock)loaded.Names["BoundToShared"];
        var boundAncestor = (TextBlock)loaded.Names["BoundToAncestor"];

        root.Measure(new WpfSize(300, 300));
        root.Arrange(new WpfRect(0, 0, 300, 300));

        Assert.Equal("SharedData", boundShared.Text);
        Assert.Equal("ControlRoot", boundAncestor.Text);

        input.Text = "UpdatedSharedData";
        Assert.Equal("UpdatedSharedData", boundShared.Text);
    }

    [Fact]
    public void FindName_resolves_element_from_attached_NameScope_when_not_in_logical_children()
    {
        var root = new Grid();
        var child = new TextBlock { Name = "ChildBlock" };
        root.AddChild(child);

        // Custom layout / floating tool element not present in root.LogicalChildren
        var unparentedTool = new Button { Name = "FloatingTool", Content = "ToolContent" };
        Assert.Null(unparentedTool.Parent);

        var scope = new NameScope();
        scope.RegisterName("FloatingTool", unparentedTool);
        NameScope.SetNameScope(root, scope);

        // Root resolves unparented element via NameScope fallback
        Assert.Same(unparentedTool, root.FindName("FloatingTool"));

        // Descendant element walks up to root and resolves via NameScope fallback
        Assert.Same(unparentedTool, child.FindName("FloatingTool"));

        // Normal logical child resolution remains intact
        Assert.Same(child, root.FindName("ChildBlock"));
        Assert.Same(child, child.FindName("ChildBlock"));
    }

    [Fact]
    public void FindName_prefers_logical_descendant_over_NameScope_when_names_collide()
    {
        var root = new Grid();
        var inTree = new Button { Name = "CollideName", Content = "InTree" };
        root.AddChild(inTree);

        var outOfTree = new Button { Name = "CollideName", Content = "OutOfTree" };
        var scope = new NameScope();
        scope.RegisterName("CollideName", outOfTree);
        NameScope.SetNameScope(root, scope);

        // Logical descendant must take precedence over NameScope registration
        var resolved = root.FindName("CollideName");
        Assert.Same(inTree, resolved);
        Assert.NotSame(outOfTree, resolved);
    }

    [Fact]
    public void ElementName_binding_resolves_unparented_element_from_attached_NameScope()
    {
        var root = new Grid();
        var consumer = new TextBlock();
        consumer.SetBinding("Text", new Binding("Content") { ElementName = "ExternalButton" });
        root.AddChild(consumer);

        var externalButton = new Button { Name = "ExternalButton", Content = "CustomToolPayload" };
        var scope = new NameScope();
        scope.RegisterName("ExternalButton", externalButton);
        NameScope.SetNameScope(root, scope);

        root.Measure(new WpfSize(300, 300));
        root.Arrange(new WpfRect(0, 0, 300, 300));

        Assert.Equal("CustomToolPayload", consumer.Text);

        externalButton.Content = "UpdatedCustomPayload";
        Assert.Equal("UpdatedCustomPayload", consumer.Text);
    }

    [Fact]
    public void NameScope_RegisterName_after_attachment_triggers_binding_refresh()
    {
        var root = new Grid();
        var consumer = new TextBlock();
        consumer.SetBinding("Text", new Binding("Content") { ElementName = "DynamicButton" });
        root.AddChild(consumer);

        var scope = new NameScope();
        NameScope.SetNameScope(root, scope);

        Assert.Equal(string.Empty, consumer.Text);

        var dynamicButton = new Button { Name = "DynamicButton", Content = "RegisteredLate" };
        scope.RegisterName("DynamicButton", dynamicButton);

        Assert.Equal("RegisteredLate", consumer.Text);
    }
}
