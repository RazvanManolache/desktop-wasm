namespace DesktopWasm.Wpf;

/// <summary>A positioned glyph run (WPF GlyphRun equivalent).</summary>
public sealed record GlyphRun(string Text, string FontCss, WpfPoint Origin);
