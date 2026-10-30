namespace DesktopWasm.AppModel;

/// <summary>New-root launch truth. This is not upstream marketing:
/// <see cref="Available"/> needs staged, hash-pinned inputs in this root,
/// and nothing here means runtime-verified (that is dw-verify evidence).</summary>
public enum AppAvailability
{
    Unknown = 0,
    Available = 1,
    MissingInput = 2,
    NotConverted = 3,
    Unavailable = 4,
}
