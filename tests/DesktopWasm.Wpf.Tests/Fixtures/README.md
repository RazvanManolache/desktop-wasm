# Conversion fixtures (real-world WPF XAML, MIT-licensed)

- [`CalculatorView.xaml`](CalculatorView.xaml) — byte-identical view from
  [CalculatorWpfMVVM](https://github.com/dariuszdbr/CalculatorWpfMVVM) by
  Dariusz Dąbrowski (MIT License, 2018). Single-window MVVM calculator:
  nested `Grid` with `RowDefinitions`/`ColumnDefinitions` (star/`Auto`),
  digit/operator `Button`s with `Style`/`StaticResource`, `TextBox` display
  with `{Binding}`, `Viewbox`, `Row="1"` shorthand, `Grid.ColumnSpan`.
- `MiniTD` ([mennowo/MiniTD](https://github.com/mennowo/MiniTD), MIT) was
  reviewed but not vendored: `Menu`/`MenuItem`, `ResourceDictionary` merged
  dictionaries, `DynamicResource`, `InputBindings`, and custom `vw:` views
  are all still beyond the parser — see `ConversionGapTests`.

Do not "fix" these fixtures to fit the engine: they pin the real-world
boundary. When the engine learns a construct, move its coverage from
`ConversionGapTests` (expects rejection) into a positive layout test.
