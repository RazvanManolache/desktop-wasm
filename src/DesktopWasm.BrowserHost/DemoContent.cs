namespace DesktopWasm.BrowserHost;

/// <summary>Demo content for the browser host page.</summary>
public static class DemoContent
{
    public const string Xaml = """
        <Window Title="Demo">
          <StackPanel>
            <Button Content="OK"/>
            <TextBlock Text="Hello"/>
          </StackPanel>
        </Window>
        """;

    /// <summary>Keypad shaped like the CalculatorWpfMVVM view (supported subset).</summary>
    public const string KeypadXaml = """
        <Window Title="Keypad">
          <StackPanel>
            <Border Background="#EEEEEE" Width="300" HorizontalAlignment="Center" Thickness="0">
              <TextBlock Text="0" FontSize="28"/>
            </Border>
            <Grid Width="300" HorizontalAlignment="Center">
              <Grid.RowDefinitions>
                <RowDefinition Height="64"/>
                <RowDefinition Height="64"/>
                <RowDefinition Height="64"/>
                <RowDefinition Height="64"/>
              </Grid.RowDefinitions>
              <Grid.ColumnDefinitions>
                <ColumnDefinition/>
                <ColumnDefinition/>
                <ColumnDefinition/>
              </Grid.ColumnDefinitions>
              <Button Content="7" FontSize="20" Grid.Row="0" Grid.Column="0"/>
              <Button Content="8" FontSize="20" Grid.Row="0" Grid.Column="1"/>
              <Button Content="9" FontSize="20" Grid.Row="0" Grid.Column="2"/>
              <Button Content="4" FontSize="20" Grid.Row="1" Grid.Column="0"/>
              <Button Content="5" FontSize="20" Grid.Row="1" Grid.Column="1"/>
              <Button Content="6" FontSize="20" Grid.Row="1" Grid.Column="2"/>
              <Button Content="1" FontSize="20" Grid.Row="2" Grid.Column="0"/>
              <Button Content="2" FontSize="20" Grid.Row="2" Grid.Column="1"/>
              <Button Content="3" FontSize="20" Grid.Row="2" Grid.Column="2"/>
              <Button Content="C" FontSize="20" Grid.Row="3" Grid.Column="0"/>
              <Button Content="0" FontSize="20" Grid.Row="3" Grid.Column="1"/>
              <Button Content="=" FontSize="20" Grid.Row="3" Grid.Column="2"/>
            </Grid>
          </StackPanel>
        </Window>
        """;

    /// <summary>Binding showcase: TextBlocks bound to a view-model behind the window.</summary>
    public const string BindingXaml = """
        <Window Title="Binding">
          <StackPanel>
            <TextBlock Text="{Binding Name}" FontSize="24"/>
            <TextBlock Text="{Binding Age}"/>
            <TextBox Text="{Binding Name, Mode=TwoWay}"/>
            <Button Content="Birthday!" Command="{Binding BirthdayCommand}"/>
          </StackPanel>
        </Window>
        """;

    /// <summary>Theme pack loaded by the resources demo through Source=.</summary>
    public const string ThemeXaml = """
        <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
          <String x:Key="Greet">Themed greeting</String>
          <SolidColorBrush x:Key="Accent" Color="#FF0066CC"/>
          <Double x:Key="TitleSize">20</Double>
        </ResourceDictionary>
        """;

    /// <summary>Resources showcase: inline dictionary plus a pack Source merge.</summary>
    public const string ResourcesXaml = """
        <Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Title="Resources">
          <Window.Resources>
            <ResourceDictionary>
              <String x:Key="Hi">Hello from resources</String>
              <SolidColorBrush x:Key="Warm" Color="#FFE08000"/>
              <ResourceDictionary.MergedDictionaries>
                <ResourceDictionary Source="Theme.xaml"/>
              </ResourceDictionary.MergedDictionaries>
            </ResourceDictionary>
          </Window.Resources>
          <StackPanel>
            <TextBlock Text="{StaticResource Greet}" FontSize="{StaticResource TitleSize}" Foreground="{StaticResource Accent}"/>
            <TextBlock Text="{StaticResource Hi}" Foreground="{StaticResource Warm}"/>
            <Button Content="{StaticResource Greet}" Background="{StaticResource Accent}"/>
          </StackPanel>
        </Window>
        """;
}
