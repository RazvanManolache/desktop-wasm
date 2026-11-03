using System.Collections.ObjectModel;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Generated-row text follows DisplayMemberPath changes: rebinding the
/// path re-resolves every generated row (the setter documents this;
/// nothing pinned it).
/// </summary>
[ResetAmbientState]
public sealed class ListBoxDisplayPathChangeTests
{
    private sealed record Person(string Name, int Age);

    [Fact]
    public void Changing_path_re_resolves_generated_rows()
    {
        var box = new ListBox();
        box.ItemsSource = new ObservableCollection<Person>(
        [
            new("Ada", 36),
            new("Grace", 85),
        ]);
        box.DisplayMemberPath = "Name";
        Assert.Equal("Ada", box.Items[0].Content);
        Assert.Equal("Grace", box.Items[1].Content);

        box.DisplayMemberPath = "Age";

        Assert.Equal("36", box.Items[0].Content);
        Assert.Equal("85", box.Items[1].Content);
    }
}
