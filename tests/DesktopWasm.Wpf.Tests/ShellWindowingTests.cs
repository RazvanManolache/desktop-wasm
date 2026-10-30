using DesktopWasm.Core;
using DesktopWasm.Graphics;
using SkiaSharp;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// ScreenToGif shell/windowing slice repro (fails before the slice).
/// Original call sites:
/// - ScreenToGif/Windows/NewRecorder.xaml + Recorder game: Window
///   SizeToContent="WidthAndHeight" plus Window.TaskbarItemInfo carrying a
///   ThumbButtonInfoCollection (Record/Snap/Pause/Stop/Discard buttons with
///   Command/CommandTarget/Description/DismissWhenClicked/Visibility and a
///   DrawingImage ImageSource).
/// - ScreenToGif/Windows/Editor.xaml.cs: TaskbarItemInfo.ProgressState =
///   Indeterminate/Normal/None plus ProgressValue writes; NewRecorder.xaml.cs
///   + Recorder.xaml.cs: TaskbarItemInfo.Overlay = null/new DrawingImage.
/// - ScreenToGif/Windows/Other/{DownloadDialog,Insert,Localization,
///   VideoSource,Options}.xaml.cs: SizeToContent = Width/Manual writes.
/// - ScreenToGif/Windows/Other/VideoSource.xaml: GridSplitter in its own
///   column (Grid.Column="1" Width="4" plus a BorderBrush).
/// Pointer dispatch runs through process-wide input statics, so this joins
/// the serialized ApplicationState collection like the other interaction
/// tests.
/// </summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class ShellWindowingTests
{
    private static (WasmInputRouter Router, WpfInputManager Manager) Harness(UIElement content)
    {
        var window = new Window { Content = content };
        var router = new WasmInputRouter();
        var manager = new WpfInputManager(router);
        manager.AddRoot(window);
        var mil = new WasmMilCore(new DesktopWasm.Graphics.SkiaSurface(800, 600));
        mil.Render(window, new WpfSize(800, 600), new WpfRect(0, 0, 800, 600));
        return (router, manager);
    }

    private static readonly WasmWindowHandle Handle = new(1);

    private static void Down(WasmInputRouter router, double x, double y) =>
        router.DispatchPointer(new WasmPointerArgs(Handle, x, y, WasmPointerButton.Left, true));

    private static void Move(WasmInputRouter router, double x, double y) =>
        router.DispatchPointer(new WasmPointerArgs(Handle, x, y, WasmPointerButton.Left, true)
        { Kind = WasmPointerKind.Move });

    private static void Up(WasmInputRouter router, double x, double y) =>
        router.DispatchPointer(new WasmPointerArgs(Handle, x, y, WasmPointerButton.Left, false));

    [Fact]
    public void Enum_values_match_wpf()
    {
        Assert.Equal(0, (int)SizeToContent.Manual);
        Assert.Equal(1, (int)SizeToContent.Width);
        Assert.Equal(2, (int)SizeToContent.Height);
        Assert.Equal(3, (int)SizeToContent.WidthAndHeight);

        Assert.Equal(0, (int)TaskbarItemProgressState.None);
        Assert.Equal(1, (int)TaskbarItemProgressState.Indeterminate);
        Assert.Equal(2, (int)TaskbarItemProgressState.Normal);
        Assert.Equal(3, (int)TaskbarItemProgressState.Error);
        Assert.Equal(4, (int)TaskbarItemProgressState.Paused);

        Assert.Equal(0, (int)GridResizeDirection.Auto);
        Assert.Equal(1, (int)GridResizeDirection.Columns);
        Assert.Equal(2, (int)GridResizeDirection.Rows);

        Assert.Equal(0, (int)GridResizeBehavior.BasedOnAlignment);
        Assert.Equal(1, (int)GridResizeBehavior.CurrentAndNext);
        Assert.Equal(2, (int)GridResizeBehavior.PreviousAndCurrent);
        Assert.Equal(3, (int)GridResizeBehavior.PreviousAndNext);
    }

    [Fact]
    public void GridSplitter_defaults_match_wpf()
    {
        var splitter = new GridSplitter();
        Assert.Equal(GridResizeDirection.Auto, splitter.ResizeDirection);
        Assert.Equal(GridResizeBehavior.BasedOnAlignment, splitter.ResizeBehavior);
        Assert.False(splitter.ShowsPreview);
        Assert.Null(splitter.PreviewStyle);
        Assert.Equal(10.0, splitter.KeyboardIncrement);
        Assert.Equal(1.0, splitter.DragIncrement);
        Assert.True(splitter.Focusable);
        Assert.Equal(HorizontalAlignment.Right, splitter.HorizontalAlignment);
        Assert.Same(Cursors.SizeWE, splitter.Cursor);

        splitter.ResizeDirection = GridResizeDirection.Rows;
        Assert.Same(Cursors.SizeNS, splitter.Cursor);

        Assert.NotNull(GridSplitter.ResizeDirectionProperty);
        Assert.NotNull(GridSplitter.ResizeBehaviorProperty);
        Assert.NotNull(GridSplitter.ShowsPreviewProperty);
        Assert.NotNull(GridSplitter.DragIncrementProperty);
        Assert.NotNull(GridSplitter.KeyboardIncrementProperty);
    }

    [Fact]
    public void ThumbButtonInfo_defaults_match_wpf()
    {
        var button = new ThumbButtonInfo();
        Assert.Equal(string.Empty, button.Description);
        Assert.False(button.DismissWhenClicked);
        Assert.Null(button.ImageSource);
        Assert.True(button.IsBackgroundVisible);
        Assert.True(button.IsEnabled);
        Assert.True(button.IsInteractive);
        Assert.Equal(Visibility.Visible, button.Visibility);
        Assert.Null(button.Command);
        Assert.Null(button.CommandParameter);
        Assert.Null(button.CommandTarget);
        Assert.IsAssignableFrom<ICommandSource>(button);

        Assert.NotNull(ThumbButtonInfo.CommandProperty);
        Assert.NotNull(ThumbButtonInfo.DescriptionProperty);
        Assert.NotNull(ThumbButtonInfo.ImageSourceProperty);
        Assert.NotNull(ThumbButtonInfo.VisibilityProperty);
    }

    [Fact]
    public void ThumbButton_click_runs_command_when_enabled()
    {
        var button = new ThumbButtonInfo();
        var clicks = 0;
        var runs = 0;
        button.Click += (_, _) => clicks++;
        button.Command = new RelayCommand(_ => runs++);

        button.InvokeClick();
        Assert.Equal(1, clicks);
        Assert.Equal(1, runs);

        button.IsEnabled = false;
        button.InvokeClick();
        Assert.Equal(1, clicks);
        Assert.Equal(1, runs);

        button.IsEnabled = true;
        button.Command = new RelayCommand(_ => runs++, _ => false);
        button.InvokeClick();
        Assert.Equal(2, clicks);
        Assert.Equal(1, runs);
    }

    [Fact]
    public void TaskbarItemInfo_defaults_and_progress_coercion()
    {
        var info = new TaskbarItemInfo();
        Assert.Equal(TaskbarItemProgressState.None, info.ProgressState);
        Assert.Equal(0.0, info.ProgressValue);
        Assert.Null(info.Overlay);
        Assert.Equal(string.Empty, info.Description);
        Assert.Equal(new Thickness(0), info.ThumbnailClipMargin);
        Assert.NotNull(info.ThumbButtonInfos);
        Assert.Empty(info.ThumbButtonInfos);

        info.ProgressValue = 2.5;
        Assert.Equal(1.0, info.ProgressValue);
        info.ProgressValue = -1;
        Assert.Equal(0.0, info.ProgressValue);

        info.Description = "Encoding…";
        info.ProgressState = TaskbarItemProgressState.Normal;
        info.ProgressValue = 0.45;
        Assert.Equal("Encoding…", info.Description);
        Assert.Equal(TaskbarItemProgressState.Normal, info.ProgressState);
        Assert.Equal(0.45, info.ProgressValue);

        Assert.NotNull(TaskbarItemInfo.ProgressStateProperty);
        Assert.NotNull(TaskbarItemInfo.ProgressValueProperty);
        Assert.NotNull(TaskbarItemInfo.OverlayProperty);
        Assert.NotNull(TaskbarItemInfo.ThumbButtonInfosProperty);
    }

    [Fact]
    public void Window_taskbar_slot_is_lazy_and_size_parses()
    {
        var window = new Window();
        Assert.Equal(SizeToContent.Manual, window.SizeToContent);
        Assert.NotNull(Window.SizeToContentProperty);
        Assert.NotNull(Window.TaskbarItemInfoProperty);

        // Editor.xaml.cs writes progress without ever assigning the slot.
        Assert.NotNull(window.TaskbarItemInfo);
        window.TaskbarItemInfo.ProgressState = TaskbarItemProgressState.Indeterminate;
        Assert.Equal(TaskbarItemProgressState.Indeterminate, window.TaskbarItemInfo.ProgressState);

        window.SizeToContent = SizeToContent.WidthAndHeight;
        Assert.Equal(SizeToContent.WidthAndHeight, window.SizeToContent);
    }

    [Fact]
    public void Xaml_parses_recorder_taskbar_shape()
    {
        const string xaml = """
            <Window SizeToContent="WidthAndHeight" Title="Recorder" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Window.TaskbarItemInfo>
                <TaskbarItemInfo>
                  <TaskbarItemInfo.ThumbButtonInfos>
                    <ThumbButtonInfoCollection>
                      <ThumbButtonInfo x:Name="RecordThumbInfo" Description="Record" DismissWhenClicked="True">
                        <ThumbButtonInfo.ImageSource>
                          <DrawingImage/>
                        </ThumbButtonInfo.ImageSource>
                      </ThumbButtonInfo>
                      <ThumbButtonInfo Visibility="Hidden"/>
                      <ThumbButtonInfo Description="Discard" DismissWhenClicked="True" IsEnabled="False"/>
                    </ThumbButtonInfoCollection>
                  </TaskbarItemInfo.ThumbButtonInfos>
                </TaskbarItemInfo>
              </Window.TaskbarItemInfo>
              <Grid/>
            </Window>
            """;
        var loaded = XamlLoader.Load(xaml);
        var window = Assert.IsType<Window>(loaded.Root);
        Assert.Equal(SizeToContent.WidthAndHeight, window.SizeToContent);

        var infos = window.TaskbarItemInfo.ThumbButtonInfos;
        Assert.Equal(3, infos.Count);
        Assert.Equal("Record", infos[0].Description);
        Assert.True(infos[0].DismissWhenClicked);
        Assert.NotNull(infos[0].ImageSource);
        Assert.Equal(Visibility.Hidden, infos[1].Visibility);
        Assert.False(infos[2].IsEnabled);

        Assert.Same(infos[0], Assert.IsType<ThumbButtonInfo>(loaded.Names["RecordThumbInfo"]));
    }

    [Fact]
    public void Xaml_rejects_bad_shell_values()
    {
        Assert.Throws<XamlLoadException>(() => XamlLoader.Load(
            "<Window SizeToContent=\"Sideways\"/>"));
        Assert.Throws<XamlLoadException>(() => XamlLoader.Load(
            "<Window><Window.TaskbarItemInfo><TaskbarItemInfo ProgressState=\"Blinking\"/></Window.TaskbarItemInfo></Window>"));
        Assert.Throws<XamlLoadException>(() => XamlLoader.Load(
            "<Window><Window.TaskbarItemInfo><TaskbarItemInfo><TaskbarItemInfo.ThumbButtonInfos><ThumbButtonInfoCollection><ThumbButtonInfo Nope=\"1\"/></ThumbButtonInfoCollection></TaskbarItemInfo.ThumbButtonInfos></TaskbarItemInfo></Window.TaskbarItemInfo></Window>"));
        Assert.Throws<XamlLoadException>(() => XamlLoader.Load(
            "<Window><Window.TaskbarItemInfo><TaskbarItemInfo><TaskbarItemInfo.ThumbButtonInfos><ThumbButtonInfoCollection><ThumbButtonInfo Command=\"{Binding RecordCommand}\"/></ThumbButtonInfoCollection></TaskbarItemInfo.ThumbButtonInfos></TaskbarItemInfo></Window.TaskbarItemInfo></Window>"));
        Assert.Throws<XamlLoadException>(() => XamlLoader.Load(
            "<Grid><GridSplitter ResizeDirection=\"Diagonal\"/></Grid>"));
    }

    private static Grid ThreeColumnGrid(GridResizeBehavior behavior = GridResizeBehavior.PreviousAndNext)
    {
        var grid = new Grid { Width = 800, Height = 600 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Pixel(200) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Pixel(4) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star() });
        var left = new Border { Background = new SolidColorBrush(Colors.Blue) };
        left.SetAttached("Grid.Column", 0);
        var splitter = new GridSplitter { ResizeBehavior = behavior };
        splitter.SetAttached("Grid.Column", 1);
        var right = new Border { Background = new SolidColorBrush(Colors.Lime) };
        right.SetAttached("Grid.Column", 2);
        grid.AddChild(left);
        grid.AddChild(splitter);
        grid.AddChild(right);
        return grid;
    }

    [Fact]
    public void GridSplitter_drag_redistributes_adjacent_tracks()
    {
        var grid = ThreeColumnGrid();
        var (router, _) = Harness(grid);
        var splitter = Assert.IsType<GridSplitter>(grid.LogicalChildren[1]);

        Assert.Equal(200, grid.ColumnDefinitions[0].Width.Value, 3);
        Assert.Equal(GridUnitType.Pixel, grid.ColumnDefinitions[0].Width.Unit);
        // Star track converts to pixels when the drag starts.
        Assert.Equal(GridUnitType.Star, grid.ColumnDefinitions[2].Width.Unit);

        var at = splitter.RenderRect;
        Down(router, at.X + at.Width / 2, at.Y + at.Height / 2);
        Move(router, at.X + at.Width / 2 + 40, at.Y + at.Height / 2);
        Up(router, at.X + at.Width / 2 + 40, at.Y + at.Height / 2);

        Assert.Equal(GridUnitType.Pixel, grid.ColumnDefinitions[2].Width.Unit);
        Assert.Equal(240, grid.ColumnDefinitions[0].Width.Value, 0);
        Assert.Equal(556, grid.ColumnDefinitions[2].Width.Value, 0);
    }

    [Fact]
    public void GridSplitter_keyboard_nudges_by_increment()
    {
        var grid = ThreeColumnGrid(GridResizeBehavior.CurrentAndNext);
        var (router, _) = Harness(grid);
        var splitter = Assert.IsType<GridSplitter>(grid.LogicalChildren[1]);
        Assert.True(splitter.Focus());

        router.DispatchKeyboard(new WasmKeyArgs(Handle, "ArrowRight", true, false, false, false));
        Assert.Equal(14, grid.ColumnDefinitions[1].Width.Value, 0);
        Assert.Equal(586, grid.ColumnDefinitions[2].Width.Value, 0);

        // Min clamp holds: the track cannot go negative.
        for (var i = 0; i < 100; i++)
            router.DispatchKeyboard(new WasmKeyArgs(Handle, "ArrowLeft", true, false, false, false));
        Assert.True(grid.ColumnDefinitions[1].Width.Value >= 0);
    }

    [Fact]
    public void GridSplitter_preview_defers_until_release()
    {
        var grid = ThreeColumnGrid();
        var (router, _) = Harness(grid);
        var splitter = Assert.IsType<GridSplitter>(grid.LogicalChildren[1]);
        splitter.ShowsPreview = true;

        var at = splitter.RenderRect;
        Down(router, at.X + at.Width / 2, at.Y + at.Height / 2);
        Move(router, at.X + at.Width / 2 + 40, at.Y + at.Height / 2);
        Assert.Equal(200, grid.ColumnDefinitions[0].Width.Value, 3);

        Up(router, at.X + at.Width / 2 + 40, at.Y + at.Height / 2);
        Assert.Equal(240, grid.ColumnDefinitions[0].Width.Value, 0);
    }

    [Fact]
    public void GridSplitter_paints_bar_and_preview_marker()
    {
        // The splitter paints its preview marker in its own pass, so it is
        // added last here: later siblings would cover the marker exactly
        // like any overflowing child paint.
        var grid = new Grid { Width = 800, Height = 600 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Pixel(200) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Pixel(4) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star() });
        var left = new Border { Background = new SolidColorBrush(Colors.Blue) };
        left.SetAttached("Grid.Column", 0);
        var right = new Border { Background = new SolidColorBrush(Colors.Lime) };
        right.SetAttached("Grid.Column", 2);
        var splitter = new GridSplitter { ResizeBehavior = GridResizeBehavior.PreviousAndNext };
        splitter.SetAttached("Grid.Column", 1);
        grid.AddChild(left);
        grid.AddChild(right);
        grid.AddChild(splitter);
        var (router, _) = Harness(grid);
        splitter.Background = new SolidColorBrush(Colors.Gray);
        splitter.ShowsPreview = true;

        using var surface = new SkiaSurface(800, 600);
        var mil = new WasmMilCore(surface);
        mil.CommitChannel(mil.Render(grid, new WpfSize(800, 600), new WpfRect(0, 0, 800, 600)));
        using (var bitmap = SKBitmap.FromImage(surface.Snapshot()))
        {
            var bar = bitmap.GetPixel(202, 300);
            Assert.True(bar.Red > 100 && bar.Red < 160, $"bar {bar} is not the gray splitter");
        }

        var at = splitter.RenderRect;
        Down(router, at.X + at.Width / 2, at.Y + at.Height / 2);
        Move(router, at.X + at.Width / 2 + 40, at.Y + at.Height / 2);
        mil.CommitChannel(mil.Render(grid, new WpfSize(800, 600), new WpfRect(0, 0, 800, 600)));
        using (var bitmap = SKBitmap.FromImage(surface.Snapshot()))
        {
            // Preview mode: definitions hold, the bar paints the drag-offset marker.
            var mark = bitmap.GetPixel(241, 300);
            Assert.True(mark.Red < 80 && mark.Green < 80 && mark.Blue < 80, $"marker {mark} is not the black preview mark");
        }
        Up(router, at.X + at.Width / 2 + 40, at.Y + at.Height / 2);
    }
}
