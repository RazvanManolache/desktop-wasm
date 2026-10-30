# Catalog contract (shell / packaging / interop)

Binding for the one unified menu list, 2026-10-30 (dw-catalog lane).
Read with `AGENTS.md` and `docs/architecture/UNIFICATION.md`.
Changes need Ops agreement; no duplicate models per lane.

## Single registry

- `catalog/apps.json` is the only maintained menu list. Shell/API read it
  through `DesktopWasm.Catalog`; no hand-maintained second list.
- `catalog/apps.schema.json` pins the file shape.
- `catalog/conflict-map.json` records the deterministic duplicate/alias
  check (empty today) plus near-miss stem notes (alias-only similarities).
- `catalog/sources/*.snapshot.json` are extraction receipts: frozen-root file,
  sha, UTC time, counts. They are migration provenance only — generation and
  availability after import use mapped LOCAL inputs, and runtime never needs
  old roots present. Regenerate with `src/DesktopWasm.Catalog.Tool import`;
  `verify` re-checks live originals against snapshots and reports drift.
- Generated today: 138 imported entries (WinForms 89 = 20 fixed scenes +
  42 Syncfusion demos + 27 benchmarks; WPF 49 = 49 app-library entries, of
  which 37 in DemoCatalog and 14 with manifests) + `mixed-session-pilot` =
  139 total, 0 conflicts. Counts are asserted by the tool and the test
  suite from the files, not by this document.

## App descriptor (lives in `DesktopWasm.AppModel`, no engine dependency)

- `Id`: stable, globally unique, lowercase `[a-z0-9][a-z0-9-]*` (max 80).
- `Aliases`: legacy spellings. Two scoped kinds: plain id-like aliases
  (same token rule as ids) and route aliases, strictly `/demo/<slug>`
  (WPF menu) or `/app/<slug>` (WinForms scenes), stored lowercase.
  Anything else in an alias field is invalid; traversal, backslashes,
  colons and any `%` escape in a route are rejected, never normalized.
  Aliases may never collide with another entry's id or alias (compared
  normalized); collisions fail validation and land in the conflict map.
- `Display`: label, description, category/group, icon (plain strings).
- `Provenance`: family (`winforms` / `wpf` / `mixed`), origin
  (`native-demo`, `original`, `benchmark`, `vendor-demo`, `pilot`),
  upstream repository URL, commit/project/license, boundary text (what is
  NOT claimed), recipe flag.
- `Entry`: entry assembly/type plus boot kind (`native-demo`,
  `converted-app`, `mixed-session`) and optional manifest slug.
  A mixed entry that is `available` MUST carry a typed `Bridge`
  (provider + contract); prose in `BootNote` is never readiness.
- `Capabilities`: flags `winforms`, `wpf`; both set means mixed on the
  wire as `["winforms","wpf"]`. Unknown flag bits are rejected.
- `Package`: canonical identity for both legacy endpoints via explicit
  adapters. `wpf-bin` artifacts are bare `*.bin` names (one `%20`-for-space
  normalization; any other `%` escape fails the import), `wf-asm` artifacts
  bare `*.dll`/`*.exe` names. Rules preserved from WPF `ApplicationPackage`:
  schema 1, slug+map match, 64-hex shas, entry assembly in closure when
  converted, gaps exactly when not converted. A valid imported app is never
  rejected merely because its legacy format differs; the joint closure
  itself (mixed artifacts) is owned by the packaging lane.
- `Availability` (new-root truth, not upstream marketing):
  `available` (inputs staged + hash-pinned in this root),
  `missing-input` (concrete gap names the un-staged local resource),
  `not-converted` (upstream recipe exists, no package yet),
  `unavailable` (blocked with reason). Plus `UpstreamStatus` (what the
  source claimed) and `Evidence` (receipt paths/hashes, proof links).
- Rules: nothing is `available` without staged, hash-pinned LOCAL inputs;
  nothing here means runtime-verified (no such state; dw-verify owns that).
  `UpstreamStatus: ready` alone never implies launchable. Staging receipts
  (`{id, localPath, sha256, stagedUtc}`) are applied by
  `LocalInputMapper.Refresh` (tool `refresh` command); each kind names its
  own required resource: engine-native demos need the staged host (no
  upstream package needed), converted apps need the hash-pinned package
  copy, scenes need the app folder plus transform inputs, the mixed pilot
  needs a proven interop session.

## Current contents (generated, 139 entries)

- WinForms 89: 20 fixed `SceneState` entries, 42 Syncfusion demos,
  27 desktop benchmarks. All `missing-input` until the import lane stages
  and pins their folders (several point at local staging or un-copied targets).
- WPF 49: 35 engine-native demos + 2 converted real apps from DemoCatalog,
  12 app-library-only externals (6 converted-with-package incl. one
  conversion-failed with concrete gaps, 6 not-converted). Converted ones are
  `missing-input` (package copy pending), the rest `not-converted`.
- `mixed-session-pilot`: capabilities `winforms+wpf`, `unavailable`,
  no bridge identity, gap owned by the interop lane. Proves the mixed
  descriptor end to end without claiming mixed execution works.
- No exact id collisions across families (verified by the tool, see
  `catalog/conflict-map.json`). pc-space-map keeps its inherited
  conversion-failed gaps; its `%20` artifact spelling is adapter-normalized.

## Lane seams

- Shell (`DesktopWasm.Host.Server` / `DesktopWasm.Web`): consume
  `AppRegistry` + `CatalogMenuEntry` projection; serve hashed package/content;
  reject unknown IDs, traversal, content mismatch per existing behavior.
- Packaging: owns the joint closure/maps and the mixed-artifact policy;
  this registry only carries the resulting package identity plus the
  `wpf-bin`/`wf-asm` adapter shapes. Type-identity overlaps (WPF
  PlatformShims vs real WinForms fork/SystemEvents) are decided there with
  interop, not duplicated here.
- Interop: owns the mixed session + typed bridge names; the pilot flips to
  `available` only on a proven session carrying `Bridge{provider,contract}`.
- Import: owns copying/staging + `source-ready.json` + staging receipts;
  snapshots point at frozen originals until then and must be re-pointed (or
  superseded by local mapping) after staging.
