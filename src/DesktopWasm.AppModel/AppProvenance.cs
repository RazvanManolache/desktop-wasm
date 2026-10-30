namespace DesktopWasm.AppModel;

/// <summary>Where an entry came from and what is (not) claimed about it.</summary>
public sealed record AppProvenance(
    string Family,
    string Origin,
    string? SourceUrl,
    string? Repository,
    string? Commit,
    string? Project,
    string? License,
    string? Boundary,
    bool HasRecipe);

/// <summary>How a converted app boots and which package it needs.</summary>
public sealed record AppEntry(
    string Kind,
    string EntryAssembly,
    string? ManifestSlug,
    string? BootNote,
    AppBridge? Bridge = null);

/// <summary>Typed bridge identity for a mixed session: which provider owns
/// the WinForms/WPF join and which contract version it implements. Prose in
/// <see cref="AppEntry.BootNote"/> is never a substitute: availability gates
/// on this value, not on keywords.</summary>
public sealed record AppBridge(string Provider, string Contract);

/// <summary>One packaged assembly: converted bytes plus identity hashes.
/// Mirrors the WPF ApplicationPackage closure rules (see CatalogValidator).
/// Artifact names the legacy endpoint that serves it: wpf-bin (converted
/// .bin manifest), wf-asm (rewritten .dll/.exe on-demand transform), mixed
/// (joint closure, owned by the packaging lane).</summary>
public sealed record PackageAssembly(
    string Name,
    string File,
    string Sha256,
    string[] References,
    string? InputSha256 = null,
    string Artifact = "wpf-bin");

/// <summary>A concrete conversion gap; present exactly when not converted.</summary>
public sealed record PackageGap(string Assembly, string Kind, string Detail);

/// <summary>Package/transform identity for one app.</summary>
public sealed record PackageIdentity(
    int SchemaVersion,
    string MapVersion,
    string Status,
    PackageAssembly[] Assemblies,
    PackageGap[] Gaps,
    string? SourceCommit);
