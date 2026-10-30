using DesktopWasm.Core;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// End-to-end proof that the converted CalculatorDemo binary (staged under
/// ConvertedApps, served on demand as apps/wpf-calculator) boots through the
/// shim and answers real routed pointer input: 1 2 + 3 = shows 15.
/// </summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class ConvertedCalcInteractionTests
{
    private sealed class CalcHarness
    {
        public WasmInputRouter Router { get; } = new();
        public WpfInputManager Manager { get; }
        public Window Window { get; }
        public TextBox Display { get; }
        public IReadOnlyList<UIElement> Elements { get; }

        public CalcHarness()
        {
            Window = BootStagedCalculator();
            Manager = new WpfInputManager(Router);
            Manager.AddRoot(Window);
            Window.Measure(new WpfSize(800, 600));
            Window.Arrange(new WpfRect(0, 0, 800, 600));
            Elements = Descendants(Window).ToList();
            Display = Elements.OfType<TextBox>().First();
        }

        public void Press(string key)
        {
            var button = Elements.OfType<Button>().Single(b => Equals(b.Content, key));
            var rect = button.ArrangedRect;
            var handle = new WasmWindowHandle(1);
            Router.DispatchPointer(new WasmPointerArgs(handle, (float)(rect.X + rect.Width / 2), (float)(rect.Y + rect.Height / 2), WasmPointerButton.Left, true)
                { Kind = WasmPointerKind.Down, PointerId = 1 });
            Router.DispatchPointer(new WasmPointerArgs(handle, (float)(rect.X + rect.Width / 2), (float)(rect.Y + rect.Height / 2), WasmPointerButton.Left, false)
                { Kind = WasmPointerKind.Up, PointerId = 1 });
        }
    }

    [Fact]
    public void Converted_calculator_answers_keypad_clicks_with_result()
    {
        var calc = new CalcHarness();
        foreach (var key in new[] { "1", "2", "+", "3", "=" })
            calc.Press(key);
        Assert.Equal("15", calc.Display.Text);
    }

    [Fact]
    public void Converted_calculator_keeps_menu_and_display_wiring()
    {
        var calc = new CalcHarness();
        Assert.Contains(calc.Elements.OfType<MenuItem>(), m => Equals(m.Header, "File"));
        Assert.NotEmpty(calc.Display.Text);
        calc.Press("5");
        calc.Press("C");
        Assert.Equal("0", calc.Display.Text);
    }

    private static Window BootStagedCalculator()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(System.IO.Path.Combine(directory.FullName, "DesktopWasm.slnx"))) directory = directory.Parent;
        Assert.NotNull(directory);
        var image = File.ReadAllBytes(System.IO.Path.Combine(directory.FullName, "src", "DesktopWasm.BrowserHost", "ConvertedApps", "CalculatorDemo", "CalculatorDemo.dll"));
        return WpfAppBoot.Boot(image);
    }

    private static IEnumerable<UIElement> Descendants(UIElement element)
    {
        yield return element;
        if (element is FrameworkElement framework)
            foreach (var child in framework.LogicalChildren)
                foreach (var item in Descendants(child)) yield return item;
    }
}
