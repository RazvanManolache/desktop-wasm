using System.ComponentModel;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// F-binding-01 red-first repro (PUSH-1106-wpf): the Birthday scenario.
/// A view-model with Age=36 plus a BirthdayCommand (Age++) drives a
/// XAML-bound TextBlock (Text="{Binding Age}") and Button
/// (Command="{Binding BirthdayCommand}"). Executing the command must push
/// Age 36-&gt;37 to the bound text, like the served binding app acceptance.
/// </summary>
[ResetAmbientState]
public sealed class BirthdayCommandTests
{
    private sealed class PartyViewModel : INotifyPropertyChanged
    {
        private int _age = 36;

        public PartyViewModel()
        {
            BirthdayCommand = new RelayCommand(_ => Age++, _ => Age < 40);
            PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(Age)) BirthdayCommand.RaiseCanExecuteChanged();
            };
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public int Age
        {
            get => _age;
            set { if (_age == value) return; _age = value; PropertyChanged?.Invoke(this, new(nameof(Age))); }
        }

        public RelayCommand BirthdayCommand { get; }
    }

    private static (Window Root, TextBlock AgeText, Button Birthday) LoadBirthdayWindow()
    {
        var root = (Window)XamlLoader.Load("""
            <Window><StackPanel>
              <TextBlock Text="{Binding Age}"/>
              <Button Content="Birthday!" Command="{Binding BirthdayCommand}"/>
            </StackPanel></Window>
            """).Root;
        var panel = (StackPanel)root.Content!;
        var ageText = (TextBlock)panel.LogicalChildren[0];
        var birthday = (Button)panel.LogicalChildren[1];
        return (root, ageText, birthday);
    }

    [Fact]
    public void Birthday_command_binding_resolves_from_DataContext()
    {
        var (root, ageText, birthday) = LoadBirthdayWindow();
        var vm = new PartyViewModel();
        root.DataContext = vm;

        Assert.Equal("36", ageText.Text);
        Assert.Same(vm.BirthdayCommand, birthday.Command);
    }

    [Fact]
    public void Birthday_click_pushes_Age_36_to_37()
    {
        var (root, ageText, birthday) = LoadBirthdayWindow();
        root.DataContext = new PartyViewModel();

        birthday.RaiseClick();

        Assert.Equal("37", ageText.Text);
    }

    [Fact]
    public void Birthday_button_disables_at_40_via_CanExecute()
    {
        var (root, _, birthday) = LoadBirthdayWindow();
        var vm = new PartyViewModel();
        root.DataContext = vm;

        Assert.True(birthday.IsEffectivelyEnabled);
        vm.Age = 40;
        Assert.False(birthday.IsEffectivelyEnabled);
    }
}
