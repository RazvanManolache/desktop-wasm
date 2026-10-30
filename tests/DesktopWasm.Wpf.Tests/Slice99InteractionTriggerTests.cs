using System.Collections;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Input;

namespace DesktopWasm.Wpf.Tests;

/// <summary>Slice-99 Blend-style Interaction.Triggers: the LeftMainContent
/// staged view reaches &lt;hc:Interaction.Triggers&gt; (TabControl line 58)
/// with an EventTrigger/EventToCommand pair driving the demo's SwitchDemoCmd.
/// Member evidence via the upstream Shared sources compiled into the staged
/// HandyControl image (Interaction.GetTriggers/EventTrigger.EventName/
/// EventToCommand.Command/PassEventArgsToCommand/TriggerCollection) plus the
/// live staged bins. Advance pinned here: the loader accepts the attached
/// Triggers block, instantiates the converted trigger + action, binds Command
/// against the host's DataContext, and the converted attach machinery
/// subscribes to the engine Selector.SelectionChanged CLR event on Loaded;
/// firing selection runs the bound command with the real event args. The 2
/// absent binaries stay declared with no browser path.</summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class Slice99InteractionTriggerTests : IDisposable
{
    private const string PresentationNs = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private const string HcNs = "https://handyorg.github.io/handycontrol";

    private readonly Application? _previous = Application.Current;

    public Slice99InteractionTriggerTests()
    {
        WpfAnimationClock.ResetForTests();
    }

    public void Dispose()
    {
        XamlLoader.CustomElementSource = null;
        Application.Current = _previous;
        WpfAnimationClock.ResetForTests();
    }

    private sealed class RecordingCommand : ICommand
    {
        public readonly List<object?> Calls = new();
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => Calls.Add(parameter);
        public event EventHandler? CanExecuteChanged;
        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }

    private sealed class DemoHost
    {
        public DemoHost(RecordingCommand command) => SwitchDemoCmd = command;
        public ICommand SwitchDemoCmd { get; }
    }

    private static Assembly LoadStagedHandyControlEntry() => StagedHandy.Load();

    [Fact]
    public void Staged_interactivity_types_carry_the_upstream_shapes()
    {
        LoadStagedHandyControlEntry();
        var lib = AppDomain.CurrentDomain.GetAssemblies()
            .First(a => string.Equals(a.GetName().Name, "HandyControl", StringComparison.Ordinal));
        var interaction = lib.GetType("HandyControl.Interactivity.Interaction", throwOnError: true)!;
        var getTriggers = interaction.GetMethod("GetTriggers", BindingFlags.Public | BindingFlags.Static)!;
        Assert.NotNull(getTriggers);
        Assert.Equal(typeof(DependencyObject), getTriggers.GetParameters().Single().ParameterType);
        var triggerType = lib.GetType("HandyControl.Interactivity.EventTrigger", throwOnError: true)!;
        var eventName = DependencyProperty.FromName(triggerType, "EventName");
        Assert.NotNull(eventName);
        Assert.Same(typeof(string), eventName!.PropertyType);
        Assert.Same(triggerType, eventName.OwnerType);
        var freshTrigger = Activator.CreateInstance(triggerType, nonPublic: true)!;
        Assert.Equal("Loaded", triggerType.GetProperty("EventName")!.GetValue(freshTrigger));
        var actionType = lib.GetType("HandyControl.Interactivity.EventToCommand", throwOnError: true)!;
        var commandDp = DependencyProperty.FromName(actionType, "Command");
        Assert.NotNull(commandDp);
        Assert.Same(typeof(ICommand), commandDp!.PropertyType);
        Assert.Same(actionType, commandDp.OwnerType);
        var passThrough = actionType.GetProperty("PassEventArgsToCommand", BindingFlags.Public | BindingFlags.Instance)!;
        Assert.Same(typeof(bool), passThrough.PropertyType);
        Assert.True(passThrough.CanWrite);
        var collectionType = lib.GetType("HandyControl.Interactivity.TriggerCollection", throwOnError: true)!;
        Assert.NotNull(collectionType.GetConstructor(
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
            null, Type.EmptyTypes, null));
        Assert.True(typeof(IList).IsAssignableFrom(collectionType));
    }

    [Fact]
    public void Staged_manifest_still_declares_only_the_two_absent_binaries()
    {
        var entry = LoadStagedHandyControlEntry();
        var apps = StagedHandy.AppsDirectory();
        using var document = JsonDocument.Parse(File.ReadAllText(System.IO.Path.Combine(apps, "app-manifest.json")));
        var root = document.RootElement;
        Assert.Equal("conversion-failed", root.GetProperty("status").GetString());
        var assemblies = root.GetProperty("assemblies").EnumerateArray().ToList();
        Assert.Equal(6, assemblies.Count);
        var gaps = root.GetProperty("gaps").EnumerateArray().ToList();
        Assert.Equal(2, gaps.Count);
        foreach (var gap in gaps)
        {
            Assert.Equal("missing-dependency", gap.GetProperty("kind").GetString());
            var detail = gap.GetProperty("detail").GetString()!;
            Assert.Contains("absent from upstream build output", detail);
            Assert.DoesNotContain("HandyControl.Interactivity", detail);
            Assert.DoesNotContain("Interaction", detail);
        }
        Assert.All(gaps, gap => Assert.Equal("HandyControl",
            gap.GetProperty("assembly").GetString()));
        _ = entry;
    }

    [Fact]
    public void LeftMainContent_advances_past_interaction_triggers()
    {
        var entry = LoadStagedHandyControlEntry();
        var type = entry.GetType("HandyControlDemo.UserControl.LeftMainContent", throwOnError: true)!;
        var error = Assert.Throws<TargetInvocationException>(() => Activator.CreateInstance(type));
        var load = Assert.IsType<XamlLoadException>(error.InnerException);
        Assert.Contains("StaticResource 'TabControlInLine' was not found", load.Message);
        Assert.DoesNotContain("<Interaction.Triggers> does not belong to <TabControl>", load.Message);
        Assert.DoesNotContain("Unknown element <DataTemplate>", load.Message);
        for (var inner = error.InnerException; inner is not null; inner = inner.InnerException)
            Assert.IsNotType<MissingFieldException>(inner);
    }

    [Fact]
    public void TabControl_selection_fires_bound_command_with_event_args()
    {
        var entry = LoadStagedHandyControlEntry();
        XamlLoader.CustomElementSource = entry;
        const string xaml =
            "<TabControl xmlns=\"" + PresentationNs + "\" " +
            "xmlns:hc=\"" + HcNs + "\">" +
            "<hc:Interaction.Triggers>" +
            "<hc:EventTrigger EventName=\"SelectionChanged\">" +
            "<hc:EventToCommand Command=\"{Binding SwitchDemoCmd}\" PassEventArgsToCommand=\"True\" />" +
            "</hc:EventTrigger>" +
            "</hc:Interaction.Triggers>" +
            "</TabControl>";
        var tabs = Assert.IsType<TabControl>(XamlLoader.Load(xaml).Root);
        tabs.AddItem("Alpha");
        tabs.AddItem("Beta");
        var command = new RecordingCommand();
        tabs.DataContext = new DemoHost(command);
        // NameResolver defers the converted subscription until the host is
        // loaded (real Blend behavior): selection before load stays silent.
        tabs.SelectedIndex = 1;
        Assert.Empty(command.Calls);
        tabs.RaiseLoadedTree();
        tabs.SelectedIndex = 0;
        var fired = Assert.Single(command.Calls);
        var args = Assert.IsType<SelectionChangedEventArgs>(fired);
        Assert.Equal("Beta", Assert.Single(args.RemovedItems));
        Assert.Equal("Alpha", Assert.Single(args.AddedItems));
        tabs.SelectedIndex = 1;
        Assert.Equal(2, command.Calls.Count);
        Assert.IsType<SelectionChangedEventArgs>(command.Calls[1]);
    }

    [Fact]
    public void Command_binding_tracks_datacontext_swap()
    {
        var entry = LoadStagedHandyControlEntry();
        XamlLoader.CustomElementSource = entry;
        const string xaml =
            "<TabControl xmlns=\"" + PresentationNs + "\" " +
            "xmlns:hc=\"" + HcNs + "\">" +
            "<hc:Interaction.Triggers>" +
            "<hc:EventTrigger EventName=\"SelectionChanged\">" +
            "<hc:EventToCommand Command=\"{Binding SwitchDemoCmd}\" PassEventArgsToCommand=\"True\" />" +
            "</hc:EventTrigger>" +
            "</hc:Interaction.Triggers>" +
            "</TabControl>";
        var tabs = Assert.IsType<TabControl>(XamlLoader.Load(xaml).Root);
        tabs.AddItem("Alpha");
        tabs.AddItem("Beta");
        var first = new RecordingCommand();
        var second = new RecordingCommand();
        tabs.DataContext = new DemoHost(first);
        tabs.RaiseLoadedTree();
        tabs.SelectedIndex = 1;
        Assert.Single(first.Calls);
        tabs.DataContext = new DemoHost(second);
        tabs.SelectedIndex = 0;
        Assert.Single(first.Calls);
        Assert.Single(second.Calls);
    }

    [Fact]
    public void Interaction_triggers_rejects_unknown_children_precisely()
    {
        var entry = LoadStagedHandyControlEntry();
        XamlLoader.CustomElementSource = entry;
        var badChild =
            "<TabControl xmlns=\"" + PresentationNs + "\" " +
            "xmlns:hc=\"" + HcNs + "\">" +
            "<hc:Interaction.Triggers>" +
            "<hc:NopeTrigger />" +
            "</hc:Interaction.Triggers>" +
            "</TabControl>";
        var childError = Assert.Throws<XamlLoadException>(() => XamlLoader.Load(badChild));
        Assert.Contains("needs <EventTrigger>/<RoutedEventTrigger> entries", childError.Message);
        var badRouted =
            "<TabControl xmlns=\"" + PresentationNs + "\" " +
            "xmlns:hc=\"" + HcNs + "\">" +
            "<hc:Interaction.Triggers>" +
            "<hc:RoutedEventTrigger />" +
            "</hc:Interaction.Triggers>" +
            "</TabControl>";
        var routedError = Assert.Throws<XamlLoadException>(() => XamlLoader.Load(badRouted));
        Assert.Contains("needs RoutedEvent=", routedError.Message);
        var badAction =
            "<TabControl xmlns=\"" + PresentationNs + "\" " +
            "xmlns:hc=\"" + HcNs + "\">" +
            "<hc:Interaction.Triggers>" +
            "<hc:EventTrigger EventName=\"SelectionChanged\">" +
            "<hc:InvokeCommandAction />" +
            "</hc:EventTrigger>" +
            "</hc:Interaction.Triggers>" +
            "</TabControl>";
        var actionError = Assert.Throws<XamlLoadException>(() => XamlLoader.Load(badAction));
        Assert.Contains("needs <EventToCommand> actions", actionError.Message);
        var badAttribute =
            "<TabControl xmlns=\"" + PresentationNs + "\" " +
            "xmlns:hc=\"" + HcNs + "\">" +
            "<hc:Interaction.Triggers>" +
            "<hc:EventTrigger EventNames=\"SelectionChanged\" />" +
            "</hc:Interaction.Triggers>" +
            "</TabControl>";
        var attributeError = Assert.Throws<XamlLoadException>(() => XamlLoader.Load(badAttribute));
        Assert.Contains("is not supported on <EventTrigger>", attributeError.Message);
        var badConverter =
            "<TabControl xmlns=\"" + PresentationNs + "\" " +
            "xmlns:hc=\"" + HcNs + "\">" +
            "<hc:Interaction.Triggers>" +
            "<hc:EventTrigger EventName=\"SelectionChanged\">" +
            "<hc:EventToCommand Command=\"{Binding SwitchDemoCmd}\" EventArgsConverter=\"x\" />" +
            "</hc:EventTrigger>" +
            "</hc:Interaction.Triggers>" +
            "</TabControl>";
        var converterError = Assert.Throws<XamlLoadException>(() => XamlLoader.Load(badConverter));
        Assert.Contains("is not supported on <EventToCommand>", converterError.Message);
    }

    private static class StagedHandy
    {
        private static readonly object Gate = new();
        private static Assembly? _entry;

        internal static Assembly Load()
        {
            lock (Gate)
            {
                var hit = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => string.Equals(a.GetName().Name, "HandyControlDemo", StringComparison.OrdinalIgnoreCase));
                if (hit is not null)
                {
                    VerifyHashes();
                    return hit;
                }
                if (_entry is not null) return _entry;
                var apps = AppsDirectory();
                using var document = JsonDocument.Parse(
                    File.ReadAllText(System.IO.Path.Combine(apps, "app-manifest.json")));
                var images = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
                foreach (var assembly in document.RootElement.GetProperty("assemblies").EnumerateArray())
                {
                    var file = assembly.GetProperty("file").GetString()!;
                    var expected = assembly.GetProperty("sha256").GetString()!;
                    var bytes = File.ReadAllBytes(System.IO.Path.Combine(apps, file));
                    var actual = Convert.ToHexString(SHA256.HashData(bytes));
                    Assert.True(string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase),
                        $"hash mismatch in staged '{file}': {actual} != {expected}");
                    images[assembly.GetProperty("name").GetString()!] = bytes;
                }
                AppDomain.CurrentDomain.AssemblyResolve += (_, args) =>
                {
                    var requested = new AssemblyName(args.Name).Name;
                    var loaded = AppDomain.CurrentDomain.GetAssemblies()
                        .FirstOrDefault(a => string.Equals(a.GetName().Name, requested, StringComparison.OrdinalIgnoreCase));
                    if (loaded is not null) return loaded;
                    if (requested is not null && images.TryGetValue(requested, out var image))
                        return Assembly.Load(image);
                    return null;
                };
                foreach (var image in images.Values)
                    Assembly.Load(image);
                _entry = AppDomain.CurrentDomain.GetAssemblies()
                    .First(a => string.Equals(a.GetName().Name, "HandyControlDemo", StringComparison.OrdinalIgnoreCase));
                return _entry;
            }
        }

        internal static string AppsDirectory()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(System.IO.Path.Combine(
                directory.FullName, "src", "DesktopWasm.BrowserHost", "wwwroot", "apps", "handycontrol", "app-manifest.json")))
                directory = directory.Parent;
            Assert.NotNull(directory);
            return System.IO.Path.Combine(directory!.FullName, "src", "DesktopWasm.BrowserHost", "wwwroot", "apps", "handycontrol");
        }

        private static void VerifyHashes()
        {
            var apps = AppsDirectory();
            using var document = JsonDocument.Parse(
                File.ReadAllText(System.IO.Path.Combine(apps, "app-manifest.json")));
            foreach (var assembly in document.RootElement.GetProperty("assemblies").EnumerateArray())
            {
                var file = assembly.GetProperty("file").GetString()!;
                var expected = assembly.GetProperty("sha256").GetString()!;
                var actual = Convert.ToHexString(SHA256.HashData(
                    File.ReadAllBytes(System.IO.Path.Combine(apps, file))));
                Assert.True(string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase),
                    $"hash mismatch in staged '{file}': {actual} != {expected}");
            }
        }
    }
}
