using System.ComponentModel;

namespace System.Diagnostics;

// Windows Event Log surface (matches the System.Diagnostics.EventLog subset
// the gallery's Hosting closure references: EventLogLogger constructs an
// EventLog and calls WriteEvent). There is no Windows Event Log in the
// browser, and Host.CreateDefaultBuilder only wires the EventLog logging
// provider on Windows (IsOSPlatform guard), so the provider never activates
// in a browser session: the types below exist so the dependency closure
// binds, and any direct write fails loudly instead of vanishing. The
// framework payload itself is Windows-only (its Registry/AccessControl
// closure does not ship for the browser), so no binary is staged.

/// <summary>Entry severity (matches System.Diagnostics.EventLogEntryType).</summary>
public enum EventLogEntryType
{
    Error = 1,
    Warning = 2,
    Information = 4,
    SuccessAudit = 8,
    FailureAudit = 16,
}

/// <summary>Event instance (matches System.Diagnostics.EventInstance).</summary>
public sealed class EventInstance
{
    public EventInstance(long instanceId, int categoryId)
    {
        InstanceId = instanceId;
        CategoryId = categoryId;
    }

    public EventInstance(long instanceId, int categoryId, EventLogEntryType entryType)
    {
        InstanceId = instanceId;
        CategoryId = categoryId;
        EntryType = entryType;
    }

    public long InstanceId { get; set; }

    public int CategoryId { get; set; }

    public EventLogEntryType EntryType { get; set; }
}

/// <summary>
/// Event log (matches System.Diagnostics.EventLog for the construction and
/// WriteEvent shapes the Hosting EventLog logger calls). Construction and
/// property sets are retained; writes have no Windows sink and throw a
/// precise error instead of fabricating success.
/// </summary>
public sealed class EventLog : Component
{
    public EventLog()
    {
    }

    public EventLog(string logName)
    {
        Log = logName;
    }

    public EventLog(string logName, string machineName)
    {
        Log = logName;
        MachineName = machineName;
    }

    public EventLog(string logName, string machineName, string source)
    {
        Log = logName;
        MachineName = machineName;
        Source = source;
    }

    public string Log { get; set; } = string.Empty;

    public string MachineName { get; set; } = ".";

    public string Source { get; set; } = string.Empty;

    /// <summary>Windows Event Log writes have no sink in the browser.</summary>
    public void WriteEvent(EventInstance instance, params object?[] values) =>
        throw new NotSupportedException(
            "EventLog.WriteEvent needs the Windows Event Log, which the browser does not provide; the Hosting EventLog provider is Windows-only and never activates here.");

    /// <summary>Windows Event Log writes have no sink in the browser.</summary>
    public void WriteEvent(EventInstance instance, byte[]? rawData, params object?[] values) =>
        throw new NotSupportedException(
            "EventLog.WriteEvent needs the Windows Event Log, which the browser does not provide; the Hosting EventLog provider is Windows-only and never activates here.");

    /// <summary>Windows Event Log writes have no sink in the browser.</summary>
    public void WriteEvent(string? source, EventInstance instance, params object?[] values) =>
        throw new NotSupportedException(
            "EventLog.WriteEvent needs the Windows Event Log, which the browser does not provide; the Hosting EventLog provider is Windows-only and never activates here.");

    /// <summary>Windows Event Log writes have no sink in the browser.</summary>
    public void WriteEvent(string? source, EventInstance instance, byte[]? rawData, params object?[] values) =>
        throw new NotSupportedException(
            "EventLog.WriteEvent needs the Windows Event Log, which the browser does not provide; the Hosting EventLog provider is Windows-only and never activates here.");
}
