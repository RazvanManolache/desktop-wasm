namespace DesktopWasm.AppModel;

/// <summary>One canonical catalog entry. Id is stable and globally unique;
/// legacy spellings live in <see cref="Aliases"/> and resolve to this entry.
/// Display/provenance/entry describe the app; capabilities gate the runtime;
/// package carries the verifiable transform identity; availability is the
/// new-root launch truth with concrete gaps and evidence.</summary>
public sealed record AppDescriptor(
    string Id,
    string[] Aliases,
    string Label,
    string Description,
    string Category,
    string Icon,
    AppProvenance Provenance,
    AppEntry Entry,
    AppCapability Capabilities,
    string TransformVersion,
    PackageIdentity? Package,
    AppAvailability Availability,
    string UpstreamStatus,
    string AvailabilityNote,
    string[] Evidence,
    string[] Gaps);
