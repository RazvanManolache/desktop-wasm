namespace DesktopWasm.Wpf;

// Brush stack (PresentationCore subset, shim, not port): Brush,
// SolidColorBrush, Colors and Brushes with the member surface converted apps
// call (Opacity, Color, the named colors/brushes ScreenToGif and WPFGallery
// reference). The hierarchy collapses Animatable/DispatcherObject (engine
// Freezable is the freeze point; dispatcher adapters are later work), and
// Transform/RelativeTransform stay precise gaps until the 2D geometry stack
// lands. Brushes.* returns frozen shared instances, like real WPF.

/// <summary>Base brush (matches System.Windows.Media.Brush shape).</summary>
public abstract class Brush : Animatable
{
    private double _opacity = 1.0;

    /// <summary>Opacity multiplier (default 1.0).</summary>
    public double Opacity
    {
        get => _opacity;
        set
        {
            ThrowIfFrozen();
            _opacity = value;
        }
    }
}

/// <summary>Solid fill brush (matches System.Windows.Media.SolidColorBrush).</summary>
public sealed class SolidColorBrush : Brush
{
    /// <summary>Color dependency property (matches SolidColorBrush.ColorProperty;
    /// vendor theme transitions animate it via BeginAnimation).</summary>
    public static readonly DependencyProperty ColorProperty =
        DependencyProperty.Register("Color", typeof(Color), typeof(SolidColorBrush),
            new PropertyMetadata(default(Color)));

    /// <summary>No color (matches the parameterless constructor).</summary>
    public SolidColorBrush() { }

    public SolidColorBrush(Color color) => SetValue(ColorProperty, color);

    public Color Color
    {
        get => (Color)GetValue(ColorProperty);
        set => SetValue(ColorProperty, value);
    }

    /// <summary>Engine render encoding (0xRRGGBBAA, the shared convention).</summary>
    internal uint ToRgba() =>
        ((uint)Color.R << 24) | ((uint)Color.G << 16) | ((uint)Color.B << 8) | Color.A;
}

/// <summary>Named colors (matches System.Windows.Media.Colors).</summary>
public static class Colors
{
    public static Color Transparent => Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF);
    public static Color Black => Color.FromRgb(0x00, 0x00, 0x00);
    public static Color White => Color.FromRgb(0xFF, 0xFF, 0xFF);
    public static Color Red => Color.FromRgb(0xFF, 0x00, 0x00);
    public static Color Green => Color.FromRgb(0x00, 0x80, 0x00);
    public static Color Lime => Color.FromRgb(0x00, 0xFF, 0x00);
    public static Color Blue => Color.FromRgb(0x00, 0x00, 0xFF);
    public static Color Yellow => Color.FromRgb(0xFF, 0xFF, 0x00);
    public static Color Cyan => Color.FromRgb(0x00, 0xFF, 0xFF);
    public static Color Magenta => Color.FromRgb(0xFF, 0x00, 0xFF);
    public static Color Gray => Color.FromRgb(0x80, 0x80, 0x80);
    public static Color DarkGray => Color.FromRgb(0xA9, 0xA9, 0xA9);
    public static Color LightGray => Color.FromRgb(0xD3, 0xD3, 0xD3);
    public static Color DarkBlue => Color.FromRgb(0x00, 0x00, 0x8B);
    public static Color DarkRed => Color.FromRgb(0x8B, 0x00, 0x00);
    public static Color DarkGreen => Color.FromRgb(0x00, 0x64, 0x00);
    public static Color Navy => Color.FromRgb(0x00, 0x00, 0x80);
    public static Color Orange => Color.FromRgb(0xFF, 0xA5, 0x00);
    public static Color Purple => Color.FromRgb(0x80, 0x00, 0x80);
    public static Color Brown => Color.FromRgb(0xA5, 0x2A, 0x2A);
    public static Color Pink => Color.FromRgb(0xFF, 0xC0, 0xCB);
    public static Color CornflowerBlue => Color.FromRgb(0x64, 0x95, 0xED);
    public static Color LightBlue => Color.FromRgb(0xAD, 0xD8, 0xE6);
    public static Color LightGreen => Color.FromRgb(0x90, 0xEE, 0x90);
    public static Color LightYellow => Color.FromRgb(0xFF, 0xFF, 0xE0);
    public static Color Gold => Color.FromRgb(0xFF, 0xD7, 0x00);
    public static Color Silver => Color.FromRgb(0xC0, 0xC0, 0xC0);
    public static Color SlateGray => Color.FromRgb(0x70, 0x80, 0x90);
    public static Color DarkSlateGray => Color.FromRgb(0x2F, 0x4F, 0x4F);
    public static Color WhiteSmoke => Color.FromRgb(0xF5, 0xF5, 0xF5);
}

/// <summary>Shared frozen brushes (matches System.Windows.Media.Brushes).</summary>
public static class Brushes
{
    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    public static SolidColorBrush Transparent { get; } = Frozen(Colors.Transparent);
    public static SolidColorBrush Black { get; } = Frozen(Colors.Black);
    public static SolidColorBrush White { get; } = Frozen(Colors.White);
    public static SolidColorBrush Red { get; } = Frozen(Colors.Red);
    public static SolidColorBrush Green { get; } = Frozen(Colors.Green);
    public static SolidColorBrush Lime { get; } = Frozen(Colors.Lime);
    public static SolidColorBrush Blue { get; } = Frozen(Colors.Blue);
    public static SolidColorBrush Yellow { get; } = Frozen(Colors.Yellow);
    public static SolidColorBrush Cyan { get; } = Frozen(Colors.Cyan);
    public static SolidColorBrush Magenta { get; } = Frozen(Colors.Magenta);
    public static SolidColorBrush Gray { get; } = Frozen(Colors.Gray);
    public static SolidColorBrush DarkGray { get; } = Frozen(Colors.DarkGray);
    public static SolidColorBrush LightGray { get; } = Frozen(Colors.LightGray);
    public static SolidColorBrush DarkBlue { get; } = Frozen(Colors.DarkBlue);
    public static SolidColorBrush DarkRed { get; } = Frozen(Colors.DarkRed);
    public static SolidColorBrush DarkGreen { get; } = Frozen(Colors.DarkGreen);
    public static SolidColorBrush Navy { get; } = Frozen(Colors.Navy);
    public static SolidColorBrush Orange { get; } = Frozen(Colors.Orange);
    public static SolidColorBrush Purple { get; } = Frozen(Colors.Purple);
    public static SolidColorBrush Brown { get; } = Frozen(Colors.Brown);
    public static SolidColorBrush Pink { get; } = Frozen(Colors.Pink);
    public static SolidColorBrush CornflowerBlue { get; } = Frozen(Colors.CornflowerBlue);
    public static SolidColorBrush LightBlue { get; } = Frozen(Colors.LightBlue);
    public static SolidColorBrush LightGreen { get; } = Frozen(Colors.LightGreen);
    public static SolidColorBrush LightYellow { get; } = Frozen(Colors.LightYellow);
    public static SolidColorBrush Gold { get; } = Frozen(Colors.Gold);
    public static SolidColorBrush Silver { get; } = Frozen(Colors.Silver);
    public static SolidColorBrush SlateGray { get; } = Frozen(Colors.SlateGray);
    public static SolidColorBrush DarkSlateGray { get; } = Frozen(Colors.DarkSlateGray);
    public static SolidColorBrush WhiteSmoke { get; } = Frozen(Colors.WhiteSmoke);
}
