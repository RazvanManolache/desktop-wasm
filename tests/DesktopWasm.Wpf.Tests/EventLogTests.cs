namespace DesktopWasm.Wpf.Tests;

using System.Diagnostics;

/// <summary>
/// Gallery Hosting closure: Microsoft.Extensions.Logging.EventLog builds an
/// EventLog and calls WriteEvent(EventInstance, object[]). The browser has
/// no Windows Event Log and the Hosting provider only wires on Windows, so
/// construction is retained but every write is a precise boundary.
/// </summary>
[ResetAmbientState]
public sealed class EventLogTests
{
    [Fact]
    public void Entry_type_contract_matches_wpf_numeric_values()
    {
        Assert.Equal(1, (int)EventLogEntryType.Error);
        Assert.Equal(2, (int)EventLogEntryType.Warning);
        Assert.Equal(4, (int)EventLogEntryType.Information);
        Assert.Equal(8, (int)EventLogEntryType.SuccessAudit);
        Assert.Equal(16, (int)EventLogEntryType.FailureAudit);
    }

    [Fact]
    public void Logger_call_shapes_construct_and_retain()
    {
        // Exact shapes Microsoft.Extensions.Logging.EventLog 8.0.0 calls.
        var instance = new EventInstance(3000L, 0, EventLogEntryType.Information);
        Assert.Equal(3000L, instance.InstanceId);
        Assert.Equal(0, instance.CategoryId);
        Assert.Equal(EventLogEntryType.Information, instance.EntryType);
        var log = new EventLog("Application", ".", "WPFGallery");
        Assert.Equal("Application", log.Log);
        Assert.Equal(".", log.MachineName);
        Assert.Equal("WPFGallery", log.Source);
        Assert.Equal(string.Empty, new EventLog().Source);
    }

    [Fact]
    public void Writes_are_a_precise_boundary_not_a_silent_sink()
    {
        var log = new EventLog("Application", ".", "WPFGallery");
        var instance = new EventInstance(3000L, 0, EventLogEntryType.Information);
        foreach (var write in new Action[]
        {
            () => log.WriteEvent(instance, "started"),
            () => log.WriteEvent(instance, null, "started"),
            () => log.WriteEvent("WPFGallery", instance, "started"),
            () => log.WriteEvent("WPFGallery", instance, null, "started"),
        })
        {
            var error = Assert.Throws<NotSupportedException>(write);
            Assert.Contains("Windows Event Log", error.Message);
        }
    }
}
