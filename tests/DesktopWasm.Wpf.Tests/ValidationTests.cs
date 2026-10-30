using System.ComponentModel;
using System.Globalization;
using System.Text.RegularExpressions;
using DesktopWasm.Graphics;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Gallery TextBoxPage slice: the alphabetic ValidationRule must really run
/// on source push, block bad updates, track error state and notify
/// subscribers. Mirrors the original binding fixture (TwoWay,
/// UpdateSourceTrigger=PropertyChanged, Binding.ValidationRules).
/// </summary>
[ResetAmbientState]
public sealed class ValidationTests
{
    private sealed class AlphaRule : ValidationRule
    {
        public override ValidationResult Validate(object? value, CultureInfo cultureInfo)
        {
            var input = value as string;
            if (string.IsNullOrEmpty(input)) return ValidationResult.ValidResult;
            return Regex.IsMatch(input, "^[a-zA-Z]+$")
                ? ValidationResult.ValidResult
                : new ValidationResult(false, "Only English alphabetic characters (a-z, A-Z) are allowed.");
        }
    }

    private sealed class Person : INotifyPropertyChanged
    {
        private string _name = string.Empty;
        public event PropertyChangedEventHandler? PropertyChanged;
        public string Name
        {
            get => _name;
            set { if (_name == value) return; _name = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name))); }
        }
    }

    private static (TextBox Box, Person Vm) Harness(bool notify = false, ValidationRule? rule = null)
    {
        var vm = new Person();
        var box = new TextBox();
        var binding = new Binding("Name")
        {
            Source = vm,
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
            NotifyOnValidationError = notify,
        };
        binding.ValidationRules.Add(rule ?? new AlphaRule());
        box.SetBinding("Text", binding);
        return (box, vm);
    }

    [Fact]
    public void Invalid_input_blocks_source_update_and_sets_error_state()
    {
        var (box, vm) = Harness();

        box.Text = "Ann3";

        Assert.Equal(string.Empty, vm.Name);
        Assert.True(Validation.GetHasError(box));
        var error = Assert.Single(Validation.GetErrors(box));
        Assert.Equal("Only English alphabetic characters (a-z, A-Z) are allowed.", error.ErrorContent);
        Assert.Same(box.ActiveBindings[0], error.BindingInError);
        Assert.IsType<AlphaRule>(error.RuleInError);
    }

    [Fact]
    public void Valid_input_pushes_and_clears_prior_errors()
    {
        var (box, vm) = Harness();
        box.Text = "Ann3";
        Assert.True(Validation.GetHasError(box));

        box.Text = "Ann";

        Assert.Equal("Ann", vm.Name);
        Assert.False(Validation.GetHasError(box));
        Assert.Empty(Validation.GetErrors(box));
    }

    [Fact]
    public void Error_events_fire_only_with_notify_flag()
    {
        var silent = Harness(notify: false);
        var announced = Harness(notify: true);
        var silentFires = 0;
        var seen = new List<(ValidationErrorEventAction Action, object? Content)>();
        Validation.AddErrorHandler(silent.Box, (_, _) => silentFires++);
        Validation.AddErrorHandler(announced.Box, (_, e) =>
        {
            var ve = Assert.IsType<ValidationErrorEventArgs>(e);
            seen.Add((ve.Action, ve.Error.ErrorContent));
        });

        silent.Box.Text = "Nope1";
        announced.Box.Text = "Nope1";

        Assert.True(Validation.GetHasError(silent.Box));
        Assert.Equal(0, silentFires);
        var added = Assert.Single(seen);
        Assert.Equal(ValidationErrorEventAction.Added, added.Action);
        Assert.Equal("Only English alphabetic characters (a-z, A-Z) are allowed.", added.Content);

        announced.Box.Text = "Ok";
        Assert.Equal(2, seen.Count);
        Assert.Equal(ValidationErrorEventAction.Removed, seen[1].Action);
    }

    [Fact]
    public void Error_event_bubbles_to_ancestor_subscribers()
    {
        var (box, _) = Harness(notify: true);
        var window = new Window { Content = box };
        var bubbled = new List<ValidationErrorEventArgs>();
        window.AddHandler(Validation.ErrorEvent, new RoutedEventHandler((_, e) =>
            bubbled.Add((ValidationErrorEventArgs)e)));

        box.Text = "Bad1";

        var args = Assert.Single(bubbled);
        Assert.Same(box, args.Source);
        Assert.Same(Validation.ErrorEvent, args.RoutedEvent);
        Assert.Equal(ValidationErrorEventAction.Added, args.Action);
    }

    [Fact]
    public void RemoveErrorHandler_stops_notifications_but_keeps_state()
    {
        var (box, _) = Harness(notify: true);
        var fires = 0;
        RoutedEventHandler handler = (_, _) => fires++;
        Validation.AddErrorHandler(box, handler);
        box.Text = "Bad1";
        Assert.Equal(1, fires);

        Validation.RemoveErrorHandler(box, handler);
        box.Text = "Good";
        box.Text = "Bad2";

        Assert.Equal(1, fires);
        Assert.True(Validation.GetHasError(box));
    }

    [Fact]
    public void ValidatesOnTargetUpdated_runs_when_source_lands()
    {
        var vm = new Person { Name = "Bad9" };
        var flagged = new AlphaRule { ValidatesOnTargetUpdated = true };
        var plain = new AlphaRule();
        var flaggedBox = new TextBox();
        // Explicit trigger: the programmatic target set must not push (and
        // validate) through the source path; only the target-updated run counts.
        var flaggedBinding = new Binding("Name") { Source = vm, UpdateSourceTrigger = UpdateSourceTrigger.Explicit };
        flaggedBinding.ValidationRules.Add(flagged);
        flaggedBox.SetBinding("Text", flaggedBinding);
        var plainBox = new TextBox();
        var plainBinding = new Binding("Name") { Source = vm, UpdateSourceTrigger = UpdateSourceTrigger.Explicit };
        plainBinding.ValidationRules.Add(plain);
        plainBox.SetBinding("Text", plainBinding);

        Assert.Equal("Bad9", flaggedBox.Text);
        Assert.Equal("Bad9", plainBox.Text);
        Assert.True(Validation.GetHasError(flaggedBox));
        Assert.False(Validation.GetHasError(plainBox));
    }

    [Fact]
    public void Multiple_rules_report_multiple_errors()
    {
        var vm = new Person();
        var box = new TextBox();
        var binding = new Binding("Name")
        {
            Source = vm,
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
        };
        binding.ValidationRules.Add(new AlphaRule());
        binding.ValidationRules.Add(new AlwaysWrongRule());
        box.SetBinding("Text", binding);

        box.Text = "Bad1";

        Assert.Equal(string.Empty, vm.Name);
        Assert.Equal(2, Validation.GetErrors(box).Count);
    }

    private sealed class AlwaysWrongRule : ValidationRule
    {
        public override ValidationResult Validate(object? value, CultureInfo cultureInfo) =>
            new(false, "always wrong");
    }

    [Fact]
    public void Throwing_rule_records_exception_error_and_blocks_update()
    {
        var vm = new Person();
        var box = new TextBox();
        var binding = new Binding("Name")
        {
            Source = vm,
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
        };
        binding.ValidationRules.Add(new ExplodingRule());
        box.SetBinding("Text", binding);

        box.Text = "Anything";

        Assert.Equal(string.Empty, vm.Name);
        var error = Assert.Single(Validation.GetErrors(box));
        Assert.NotNull(error.Exception);
        Assert.IsType<InvalidOperationException>(error.Exception);
    }

    private sealed class ExplodingRule : ValidationRule
    {
        public override ValidationResult Validate(object? value, CultureInfo cultureInfo) =>
            throw new InvalidOperationException("boom");
    }

    private sealed class UpperConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value?.ToString() ?? string.Empty;
        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value?.ToString()?.ToUpperInvariant() ?? string.Empty;
    }

    private sealed class ConvertedStageRule : ValidationRule
    {
        public ConvertedStageRule() => ValidationStep = ValidationStep.ConvertedProposedValue;
        public override ValidationResult Validate(object? value, CultureInfo cultureInfo) =>
            Equals(value, "BAD")
                ? new ValidationResult(false, "converted to BAD")
                : ValidationResult.ValidResult;
    }

    [Fact]
    public void ConvertedProposedValue_rule_sees_post_conversion_value()
    {
        var vm = new Person();
        var box = new TextBox();
        var binding = new Binding("Name")
        {
            Source = vm,
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
            Converter = new UpperConverter(),
        };
        binding.ValidationRules.Add(new ConvertedStageRule());
        box.SetBinding("Text", binding);

        // Raw "bad" passes the raw stage (no raw rules); the converter makes
        // it "BAD", which the converted-stage rule rejects.
        box.Text = "bad";

        Assert.Equal(string.Empty, vm.Name);
        Assert.True(Validation.GetHasError(box));
        Assert.Equal("converted to BAD", Validation.GetErrors(box)[0].ErrorContent);
    }

    private sealed class PostWriteRule : ValidationRule
    {
        public PostWriteRule() => ValidationStep = ValidationStep.UpdatedValue;
        public override ValidationResult Validate(object? value, CultureInfo cultureInfo) =>
            new(false, "post-write note");
    }

    [Fact]
    public void UpdatedValue_rule_reports_without_rolling_back_the_write()
    {
        var vm = new Person();
        var box = new TextBox();
        var binding = new Binding("Name")
        {
            Source = vm,
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
        };
        binding.ValidationRules.Add(new PostWriteRule());
        box.SetBinding("Text", binding);

        box.Text = "Ann";

        Assert.Equal("Ann", vm.Name);
        Assert.True(Validation.GetHasError(box));
    }

    [Fact]
    public void TextBox_paints_red_border_while_invalid()
    {
        var (box, _) = Harness();
        box.Measure(new WpfSize(200, 40));
        box.Arrange(new WpfRect(0, 0, 200, 40));

        var clean = WasmMilCore.RenderHeadless(box, new WpfSize(200, 40));
        var cleanBorder = clean.OfType<DrawCommand.FillRect>().First();
        Assert.Equal(0x707070FFu, cleanBorder.Rgba);

        box.Text = "Bad1";
        var stained = WasmMilCore.RenderHeadless(box, new WpfSize(200, 40));
        var stainedBorder = stained.OfType<DrawCommand.FillRect>().First();
        Assert.Equal(TextBox.ErrorBorderRgba, stainedBorder.Rgba);
        Assert.Equal(0xE81123FFu, stainedBorder.Rgba);

        box.Text = "Good";
        var healed = WasmMilCore.RenderHeadless(box, new WpfSize(200, 40));
        Assert.Equal(0x707070FFu, healed.OfType<DrawCommand.FillRect>().First().Rgba);
    }

    [Fact]
    public void GetErrors_returns_live_content_across_transitions()
    {
        var (box, _) = Harness();
        var live = Validation.GetErrors(box);
        Assert.Empty(live);

        box.Text = "Bad1";
        Assert.Single(live);
        box.Text = "Ok";
        Assert.Empty(live);
    }

    [Fact]
    public void ValidResult_and_step_defaults_match_contract()
    {
        Assert.True(ValidationResult.ValidResult.IsValid);
        Assert.Null(ValidationResult.ValidResult.ErrorContent);
        Assert.Equal(ValidationStep.RawProposedValue, new AlphaRule().ValidationStep);
        Assert.False(new AlphaRule().ValidatesOnTargetUpdated);
    }

    [Fact]
    public void Loader_wires_gallery_shaped_binding_validation_rules()
    {
        var previous = XamlLoader.CustomElementSource;
        XamlLoader.CustomElementSource = typeof(AlphaRule).Assembly;
        try
        {
            var result = XamlLoader.Load("""
                <Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                  <TextBox x:Name="Validated" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                    <TextBox.Text>
                      <Binding Path="Name" UpdateSourceTrigger="PropertyChanged">
                        <Binding.ValidationRules>
                          <LoaderAlphaRule />
                        </Binding.ValidationRules>
                      </Binding>
                    </TextBox.Text>
                  </TextBox>
                </Window>
                """);
            var box = Assert.IsType<TextBox>(Assert.IsType<Window>(result.Root).Content);
            var expression = Assert.Single(box.ActiveBindings);
            Assert.Equal("Text", expression.TargetProperty);
            var rule = Assert.Single(expression.Binding.ValidationRules);
            Assert.IsType<LoaderAlphaRule>(rule);

            var vm = new Person();
            box.DataContext = vm;
            box.Text = "Bad1";
            Assert.Equal(string.Empty, vm.Name);
            Assert.True(Validation.GetHasError(box));
            box.Text = "Good";
            Assert.Equal("Good", vm.Name);
            Assert.False(Validation.GetHasError(box));
        }
        finally
        {
            XamlLoader.CustomElementSource = previous;
        }
    }

    [Fact]
    public void Loader_tolerates_hoisted_namespace_on_prefixed_gallery_rule()
    {
        // Gallery Text/TextBoxPage (slice-33): fragment re-parsing hoists the
        // in-scope xmlns:helpers declaration onto the prefixed rule element,
        // so <helpers:AlphabeticValidationRule /> arrives carrying a namespace
        // declaration. That declaration is not a rule argument: the loader
        // resolves the parameterless rule by short name and runs it.
        var previous = XamlLoader.CustomElementSource;
        XamlLoader.CustomElementSource = typeof(LoaderAlphaRule).Assembly;
        try
        {
            var result = XamlLoader.Load("""
                <Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" xmlns:helpers="clr-namespace:WPFGallery.Helpers">
                  <TextBox x:Name="Validated">
                    <TextBox.Text>
                      <Binding Path="Name" UpdateSourceTrigger="PropertyChanged">
                        <Binding.ValidationRules>
                          <helpers:LoaderAlphaRule />
                        </Binding.ValidationRules>
                      </Binding>
                    </TextBox.Text>
                  </TextBox>
                </Window>
                """);
            var box = Assert.IsType<TextBox>(Assert.IsType<Window>(result.Root).Content);
            var rule = Assert.Single(Assert.Single(box.ActiveBindings).Binding.ValidationRules);
            Assert.IsType<LoaderAlphaRule>(rule);

            var vm = new Person();
            box.DataContext = vm;
            box.Text = "Bad1";
            Assert.Equal(string.Empty, vm.Name);
            Assert.True(Validation.GetHasError(box));
            box.Text = "Good";
            Assert.Equal("Good", vm.Name);
            Assert.False(Validation.GetHasError(box));
        }
        finally
        {
            XamlLoader.CustomElementSource = previous;
        }
    }

    [Fact]
    public void Loader_still_rejects_rule_with_real_attributes_or_children()
    {
        var previous = XamlLoader.CustomElementSource;
        XamlLoader.CustomElementSource = typeof(LoaderAlphaRule).Assembly;
        try
        {
            var withAttribute = Assert.Throws<XamlLoadException>(() => XamlLoader.Load("""
                <Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                  <TextBox>
                    <TextBox.Text>
                      <Binding Path="Name">
                        <Binding.ValidationRules>
                          <LoaderAlphaRule Level="2" />
                        </Binding.ValidationRules>
                      </Binding>
                    </TextBox.Text>
                  </TextBox>
                </Window>
                """));
            Assert.Contains("parameterless", withAttribute.Message);

            var withChild = Assert.Throws<XamlLoadException>(() => XamlLoader.Load("""
                <Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                  <TextBox>
                    <TextBox.Text>
                      <Binding Path="Name">
                        <Binding.ValidationRules>
                          <LoaderAlphaRule><LoaderAlphaRule /></LoaderAlphaRule>
                        </Binding.ValidationRules>
                      </Binding>
                    </TextBox.Text>
                  </TextBox>
                </Window>
                """));
            Assert.Contains("parameterless", withChild.Message);
        }
        finally
        {
            XamlLoader.CustomElementSource = previous;
        }
    }

    public sealed class LoaderAlphaRule : ValidationRule
    {
        public override ValidationResult Validate(object? value, CultureInfo cultureInfo) =>
            value as string == "Good" ? ValidationResult.ValidResult : new ValidationResult(false, "want Good");
    }
}
