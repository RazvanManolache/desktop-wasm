namespace DesktopWasm.AppModel;

/// <summary>New-root launch truth. This is not upstream marketing:
/// <see cref="Available"/> needs staged, hash-pinned inputs in this root,
/// and nothing here means runtime-verified (that is dw-verify evidence).
/// <see cref="Staged"/> is bytes-verified staging whose serving and behavior
/// are still unverified: it is never launchable (see
/// <c>AppRegistry.Menu</c>) and refresh never promotes it to
/// <see cref="Available"/>.</summary>
public enum AppAvailability
{
    Unknown = 0,
    Available = 1,
    MissingInput = 2,
    NotConverted = 3,
    Unavailable = 4,
    Staged = 5,
}
