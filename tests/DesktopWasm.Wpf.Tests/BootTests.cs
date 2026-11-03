using System.Reflection;
using DesktopWasm.Wpf;

[CollectionDefinition("ApplicationState", DisableParallelization = true)]
public sealed class ApplicationStateCollection
{
}

namespace DesktopWasm.Wpf.Tests
{

[Collection("ApplicationState")]

/// <summary>
/// LoadComponent hydration and Application boot rules (the converted-app
/// entry surface), using hand-written stand-ins for generated code.
/// </summary>
[ResetAmbientState]
public sealed class BootTests : IDisposable
{
    private readonly Application? _previous = Application.Current;

    public void Dispose() => Application.Current = _previous;

    [Fact]
    public void Boot_restores_prior_synchronization_context()
    {
        // Browser shape: the host thread enters Boot with no ambient
        // context, the engine installs its dispatcher context for converted
        // startup, and Boot must leave no trace — otherwise every host await
        // after Boot posts to an unpumped dispatcher queue and the session
        // hangs with no error (MaterialDesign/calculator post-boot stall).
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null);
        try
        {
            var entry = EmitWindowlessApp();
            Assert.Throws<InvalidOperationException>(() => WpfAppBoot.Boot(entry));
            Assert.Null(SynchronizationContext.Current);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    private static System.Reflection.Assembly EmitWindowlessApp()
    {
        var assembly = System.Reflection.Emit.AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName("WindowlessApp_" + Guid.NewGuid().ToString("N")),
            System.Reflection.Emit.AssemblyBuilderAccess.Run);
        var module = assembly.DefineDynamicModule("WindowlessAppModule");
        var type = module.DefineType("WindowlessApp",
            TypeAttributes.Public | TypeAttributes.Class, typeof(Application));
        type.DefineMethod("InitializeComponent",
            MethodAttributes.Public, typeof(void), Type.EmptyTypes).GetILGenerator().Emit(System.Reflection.Emit.OpCodes.Ret);
        type.CreateType();
        return assembly;
    }

    [Fact]
    public void NewApplicationBecomesCurrent()
    {
        Application.Current = null;
        var app = new BootProbe.ProbeApp();
        Assert.Same(app, Application.Current);
    }

    [Fact]
    public void LoadComponentHydratesExistingWindow()
    {
        Application.Current = new BootProbe.ProbeApp();
        var window = new BootProbe.ProbeMain();
        Assert.Equal("Boot", window.Title);
        Assert.Equal(400, window.Width);
        Assert.Equal(300, window.Height);
        var grid = Assert.IsType<Grid>(window.Content);
        var text = Assert.IsType<TextBlock>(Assert.Single(grid.LogicalChildren));
        Assert.Equal("Boot Hi", text.Text);
    }

    [Fact]
    public void LoadComponentHydratesApplicationStartupUri()
    {
        var app = new BootProbe.ProbeApp();
        app.InitializeComponent();
        Assert.Equal(new Uri("BootWindow.xaml", UriKind.Relative), app.StartupUri);
    }

    [Fact]
    public void LoadComponentIgnoresCompatNamespaceByUriNotPrefix()
    {
        // Visual Studio binds the markup-compatibility namespace to generated
        // prefixes (HandyControlDemo App.xaml: d1P1:Ignorable); real WPF honors
        // mc:Ignorable by namespace URI, never by the literal prefix.
        var app = new BootProbe.CompatApp();
        app.InitializeComponent();
        Assert.Equal(new Uri("BootWindow.xaml", UriKind.Relative), app.StartupUri);
    }

    [Fact]
    public void LoadComponentHydratesApplicationShutdownMode()
    {
        var app = new BootProbe.ShutdownProbeApp();
        app.InitializeComponent();
        Assert.Equal(ShutdownMode.OnMainWindowClose, app.ShutdownMode);
        Assert.Equal(new Uri("BootWindow.xaml", UriKind.Relative), app.StartupUri);
    }

    [Fact]
    public void LoadComponentRejectsUnknownShutdownMode()
    {
        var app = new BootProbe.BadShutdownProbeApp();
        var ex = Assert.Throws<XamlLoadException>(() => app.InitializeComponent());
        Assert.Contains("ShutdownMode", ex.Message);
    }

    [Fact]
    public void LoadComponentRejectsClassMismatch()
    {
        Application.Current = new BootProbe.ProbeApp();
        var other = new Window();
        var ex = Assert.Throws<XamlLoadException>(() =>
            Application.LoadComponent(other, new Uri("Boot/BootWindow.xaml", UriKind.Relative)));
        Assert.Contains("BootProbe.ProbeMain", ex.Message);
    }

    [Fact]
    public void RunHasNoBrowserEquivalent()
    {
        var app = new BootProbe.ProbeApp();
        Assert.Throws<InvalidOperationException>(() => app.Run());
    }

    [Fact]
    public void Window_location_changed_wires_without_firing()
    {
        // Demo MainWindow shape: converted code unsubscribes
        // LocationChanged on close. The host owns bounds, so the retained
        // event wires like WPF but never fires from engine moves.
        var window = new Window();
        var fired = 0;
        EventHandler handler = (_, _) => fired++;
        window.LocationChanged += handler;
        window.Left = 10;
        window.Top = 20;
        Assert.Equal(0, fired);
        window.LocationChanged -= handler;
        window.Left = 30;
        Assert.Equal(0, fired);
    }

    [Fact]
    public void LoadComponentWiresFieldsAndEvents()
    {
        Application.Current = new BootProbe.ProbeApp();
        var window = new BootProbe.WiredMain();
        Assert.NotNull(window.Fire);
        Assert.NotNull(window.Label);
        Assert.Equal("Go", window.Fire.Content);
        window.Fire.RaiseClick();
        Assert.Equal(1, window.Fires);
        Assert.Same(window.Fire, window.LastSender);
    }

    [Fact]
    public void LoadComponentRejectsUnknownHandler()
    {
        Application.Current = new BootProbe.ProbeApp();
        Assert.Throws<XamlLoadException>(() => new BootProbe.BadMethodMain());
    }

    [Fact]
    public void LoadComponentRejectsMissingField()
    {
        Application.Current = new BootProbe.ProbeApp();
        Assert.Throws<XamlLoadException>(() => new BootProbe.BadFieldMain());
    }

    [Fact]
    public void LoadComponentHydratesUserControlRootAttributes()
    {
        Application.Current = new BootProbe.ProbeApp();
        var box = new BootProbe.SizedBox();
        Assert.Equal(198, box.Width);
        Assert.Equal(220, box.Height);
        Assert.IsType<Grid>(box.Content);
    }

    [Fact]
    public void LoadComponentHydratesComplexUserControlAndWiresNestedDocumentElements()
    {
        Application.Current = new BootProbe.ProbeApp();
        var control = new BootProbe.ComplexUserControl();
        Assert.Equal(350, control.Width);
        Assert.Equal(250, control.Height);

        // Verify nested element hierarchy
        var grid = Assert.IsType<Grid>(control.Content);
        Assert.Same(control.RootGrid, grid);

        var panel = Assert.IsType<StackPanel>(Assert.Single(grid.LogicalChildren));
        Assert.Same(control.ContainerPanel, panel);

        Assert.NotNull(control.InputBox);
        Assert.Equal("Sample Text", control.InputBox.Text);

        Assert.NotNull(control.ActionButton);
        Assert.Equal("Click Me", control.ActionButton.Content);

        Assert.NotNull(control.NestedBox);
        Assert.IsType<UserControl>(control.NestedBox);

        Assert.NotNull(control.StatusLabel);
        Assert.Equal("All Green", control.StatusLabel.Text);
        Assert.Same(control.StatusLabel, control.NestedBox.Content);

        // Verify layout passes propagate through nested UserControl
        control.Measure(new WpfSize(400, 300));
        control.Arrange(new WpfRect(0, 0, 400, 300));
        Assert.True(control.DesiredSize.Width > 0);
        Assert.True(control.DesiredSize.Height > 0);
    }

    [Fact]
    public void LoadComponentResolvesCustomControlsWithoutBootScope()
    {
        // Page navigations load after Boot's source scoping expired
        // (gallery AllSamplesPage PageHeader): the component's own assembly
        // scopes custom-element lookup.
        Application.Current = new BootProbe.ProbeApp();
        XamlLoader.CustomElementSource = null;
        var window = new BootProbe.CustomMain();
        Assert.NotNull(window.Box);
        Assert.IsType<BootProbe.FancyBox>(window.Box);
    }

    [Fact]
    public void LoadComponentInstantiatesCustomControls()
    {
        Application.Current = new BootProbe.ProbeApp();
        XamlLoader.CustomElementSource = typeof(BootProbe.FancyBox).Assembly;
        try
        {
            var window = new BootProbe.CustomMain();
            Assert.NotNull(window.Box);
            Assert.IsType<BootProbe.FancyBox>(window.Box);
            Assert.Equal("Fancy", window.Box.Text);
        }
        finally
        {
            XamlLoader.CustomElementSource = null;
        }
    }

    [Fact]
    public void ControlTemplateResolvesPrefixedConvertedTargetType()
    {
        // WPFGallery ControlExample shape: Template setter whose
        // ControlTemplate declares the converted TargetType explicitly.
        Application.Current = new BootProbe.ProbeApp();
        XamlLoader.CustomElementSource = typeof(BootProbe.FancyBox).Assembly;
        try
        {
            var dict = XamlLoader.LoadResourceDictionary(
                "<ResourceDictionary xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
                "xmlns:local=\"clr-namespace:BootProbe\">" +
                "<Style TargetType=\"{x:Type local:FancyBox}\">" +
                "<Setter Property=\"Template\"><Setter.Value>" +
                "<ControlTemplate TargetType=\"{x:Type local:FancyBox}\"><TextBox />" +
                "</ControlTemplate></Setter.Value></Setter></Style>" +
                "</ResourceDictionary>");
            Assert.True(dict.TryGetValue(typeof(BootProbe.FancyBox), out var found));
            var style = Assert.IsType<Style>(found);
            var box = new BootProbe.FancyBox();
            style.ApplyTo(box);
            var template = Assert.IsType<ControlTemplate>(box.Template);
            Assert.Same(typeof(BootProbe.FancyBox), template.TargetType);
            Assert.IsType<TextBox>(template.VisualTree);
        }
        finally
        {
            XamlLoader.CustomElementSource = null;
        }
    }

    [Fact]
    public void CustomContentPropertyRoutesChildrenAndDottedText()
    {
        // WPFGallery ControlExample shape: [ContentProperty(ExampleContent)]
        // routes the sample child into the named slot, and dotted text like
        // <ControlExample.XamlCode> lands on the CLR property.
        Application.Current = new BootProbe.ProbeApp();
        XamlLoader.CustomElementSource = typeof(BootProbe.FancyBox).Assembly;
        try
        {
            var root = (StackPanel)XamlLoader.Load(
                "<StackPanel xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
                "xmlns:local=\"clr-namespace:BootProbe\">" +
                "<local:ExampleHost><Button Content=\"Hi\" /></local:ExampleHost>" +
                "</StackPanel>").Root;
            var host = Assert.IsType<BootProbe.ExampleHost>(Assert.Single(root.LogicalChildren));
            var button = Assert.IsType<Button>(host.Example);
            Assert.Equal("Hi", button.Content);

            var dotted = (StackPanel)XamlLoader.Load(
                "<StackPanel xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
                "xmlns:local=\"clr-namespace:BootProbe\">" +
                "<local:ExampleHost><local:ExampleHost.Note>deep</local:ExampleHost.Note></local:ExampleHost>" +
                "</StackPanel>").Root;
            var noted = Assert.IsType<BootProbe.ExampleHost>(Assert.Single(dotted.LogicalChildren));
            Assert.Equal("deep", noted.Note);
        }
        finally
        {
            XamlLoader.CustomElementSource = null;
        }
    }

    [Fact]
    public void TemplateBoundPresenterKeepsExplicitContent()
    {
        // WPFGallery ControlExample shape: a template ContentPresenter bound
        // to a non-Content slot must keep the bound child. The parent-content
        // snapshot must not reset it to the (absent) templated-parent Content.
        var previousApp = Application.Current;
        Application.Current = new BootProbe.ProbeApp();
        XamlLoader.CustomElementSource = typeof(BootProbe.ExampleHost).Assembly;
        try
        {
            var dict = XamlLoader.LoadResourceDictionary(
                "<ResourceDictionary xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
                "xmlns:local=\"clr-namespace:BootProbe\">" +
                "<Style TargetType=\"{x:Type local:ExampleHost}\">" +
                "<Setter Property=\"Template\"><Setter.Value>" +
                "<ControlTemplate TargetType=\"{x:Type local:ExampleHost}\">" +
                "<ContentPresenter Content=\"{TemplateBinding Example}\" />" +
                "</ControlTemplate></Setter.Value></Setter></Style>" +
                "</ResourceDictionary>");
            var style = Assert.IsType<Style>(dict[typeof(BootProbe.ExampleHost)]);

            var preset = new BootProbe.ExampleHost { Example = new Button { Content = "Hi" } };
            style.ApplyTo(preset);
            var presetPresenter = Assert.IsType<ContentPresenter>(preset.TemplateRoot);
            Assert.Same(preset.Example, presetPresenter.Content);

            var late = new BootProbe.ExampleHost();
            style.ApplyTo(late);
            late.Example = new Button { Content = "Late" };
            late.ApplyTemplate();
            var latePresenter = Assert.IsType<ContentPresenter>(late.TemplateRoot);
            Assert.Same(late.Example, latePresenter.Content);
        }
        finally
        {
            XamlLoader.CustomElementSource = null;
            Application.Current = previousApp;
        }
    }

    [Fact]
    public void ListBoxItemTemplateBindingContentTemplateResolves()
    {
        // MaterialDesign drawer shape: the item container style presents
        // through ContentTemplate, which the row shell must expose like
        // ContentControl so generation resolves instead of throwing.
        Application.Current = new BootProbe.ProbeApp();
        try
        {
            var dict = XamlLoader.LoadResourceDictionary(
                "<ResourceDictionary xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\">" +
                "<Style TargetType=\"ListBoxItem\">" +
                "<Setter Property=\"Template\"><Setter.Value>" +
                "<ControlTemplate TargetType=\"ListBoxItem\">" +
                "<ContentPresenter ContentTemplate=\"{TemplateBinding ContentTemplate}\" " +
                "ContentTemplateSelector=\"{TemplateBinding ContentTemplateSelector}\" />" +
                "</ControlTemplate></Setter.Value></Setter></Style>" +
                "</ResourceDictionary>");
            var style = Assert.IsType<Style>(dict[typeof(ListBoxItem)]);
            var row = new ListBoxItem();
            style.ApplyTo(row);
            var presenter = Assert.IsType<ContentPresenter>(row.TemplateRoot);
            Assert.Null(presenter.ContentTemplate);
            Assert.Null(presenter.ContentTemplateSelector);
        }
        finally
        {
            Application.Current = _previous;
        }
    }

    [Fact]
    public void ImplicitStyleResolvesPrefixedConvertedTargetType()
    {
        // WPFGallery ControlExample shape: implicit style keyed by a
        // prefixed converted TargetType ({x:Type local:FancyBox}).
        Application.Current = new BootProbe.ProbeApp();
        XamlLoader.CustomElementSource = typeof(BootProbe.FancyBox).Assembly;
        try
        {
            var dict = XamlLoader.LoadResourceDictionary(
                "<ResourceDictionary xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
                "xmlns:local=\"clr-namespace:BootProbe\">" +
                "<Style TargetType=\"{x:Type local:FancyBox}\"><Setter Property=\"Text\" Value=\"Hi\" /></Style>" +
                "</ResourceDictionary>");
            Assert.True(dict.TryGetValue(typeof(BootProbe.FancyBox), out var found));
            var style = Assert.IsType<Style>(found);
            Assert.Same(typeof(BootProbe.FancyBox), style.TargetType);
            var box = new BootProbe.FancyBox();
            style.ApplyTo(box);
            Assert.Equal("Hi", box.Text);
        }
        finally
        {
            XamlLoader.CustomElementSource = null;
        }
    }
}
}

namespace BootProbe
{
    /// <summary>Stand-in for generated App codebehind.</summary>
    public sealed class ProbeApp : Application
    {
        public void InitializeComponent()
        {
            StartupUri = new Uri("BootWindow.xaml", UriKind.Relative);
            LoadComponent(this, new Uri("Boot/App.xaml", UriKind.Relative));
        }
    }

    /// <summary>Stand-in for generated App codebehind carrying a
    /// ShutdownMode attribute (joint TestApp app.xaml shape).</summary>
    public sealed class ShutdownProbeApp : Application
    {
        public void InitializeComponent() =>
            LoadComponent(this, new Uri("Boot/ShutdownApp.xaml", UriKind.Relative));
    }

    /// <summary>Stand-in for generated App codebehind with an unknown
    /// ShutdownMode value (must fail precise, never silent).</summary>
    public sealed class BadShutdownProbeApp : Application
    {
        public void InitializeComponent() =>
            LoadComponent(this, new Uri("Boot/BadShutdownApp.xaml", UriKind.Relative));
    }

    /// <summary>Stand-in for generated App codebehind with VS-generated
    /// markup-compatibility prefixes (d1P1:Ignorable).</summary>
    public sealed class CompatApp : Application
    {
        public void InitializeComponent()
        {
            StartupUri = new Uri("BootWindow.xaml", UriKind.Relative);
            LoadComponent(this, new Uri("Boot/CompatApp.xaml", UriKind.Relative));
        }
    }

    /// <summary>Stand-in for generated Window codebehind.</summary>
    public sealed class ProbeMain : Window
    {
        public ProbeMain() => InitializeComponent();

        public void InitializeComponent() =>
            Application.LoadComponent(this, new Uri("Boot/BootWindow.xaml", UriKind.Relative));
    }

    /// <summary>Stand-in with x:Name fields and a Click handler.</summary>
    public sealed class WiredMain : Window
    {
        internal Button? Fire;
        internal TextBlock? Label;
        public int Fires;
        public object? LastSender;

        public WiredMain() => InitializeComponent();

        public void InitializeComponent() =>
            Application.LoadComponent(this, new Uri("Boot/Wired.xaml", UriKind.Relative));

        private void OnFire(object sender, RoutedEventArgs e)
        {
            Fires++;
            LastSender = sender;
        }
    }

    /// <summary>Stand-in whose XAML names an unknown handler.</summary>
    public sealed class BadMethodMain : Window
    {
        internal Button? Fire;

        public BadMethodMain() => InitializeComponent();

        public void InitializeComponent() =>
            Application.LoadComponent(this, new Uri("Boot/WiredBadMethod.xaml", UriKind.Relative));
    }

    /// <summary>Stand-in whose XAML names a field the class lacks.</summary>
    public sealed class BadFieldMain : Window
    {
        public BadFieldMain() => InitializeComponent();

        public void InitializeComponent() =>
            Application.LoadComponent(this, new Uri("Boot/WiredBadField.xaml", UriKind.Relative));
    }

    /// <summary>Custom control stand-in (internal like MyTextBox).</summary>
    internal sealed class FancyBox : TextBox
    {
    }

    /// <summary>Stand-in UserControl with root sizing attributes.</summary>
    public sealed class SizedBox : UserControl
    {
        public SizedBox() => InitializeComponent();

        public void InitializeComponent() =>
            Application.LoadComponent(this, new Uri("Boot/SizedControl.xaml", UriKind.Relative));
    }

    /// <summary>Stand-in with a ContentProperty (gallery ControlExample shape).</summary>
    [DesktopWasm.Wpf.ContentProperty("Example")]
    public sealed class ExampleHost : Control
    {
        public object? Example { get; set; }
        public string? Note { get; set; }
    }

    /// <summary>Stand-in hosting a local: custom control.</summary>
    public sealed class CustomMain : Window
    {
        internal FancyBox? Box;

        public CustomMain() => InitializeComponent();

        public void InitializeComponent() =>
            Application.LoadComponent(this, new Uri("Boot/Custom.xaml", UriKind.Relative));
    }

    /// <summary>Stand-in complex UserControl with nested document elements and names.</summary>
    public sealed class ComplexUserControl : UserControl
    {
        public Grid? RootGrid;
        public StackPanel? ContainerPanel;
        public TextBox? InputBox;
        public Button? ActionButton;
        public UserControl? NestedBox;
        public TextBlock? StatusLabel;

        public ComplexUserControl() => InitializeComponent();

        public void InitializeComponent() =>
            Application.LoadComponent(this, new Uri("Boot/ComplexControl.xaml", UriKind.Relative));
    }
}
