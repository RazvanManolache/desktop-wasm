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
  `staged` (bytes-verified staging whose serving + behavior are still
  unverified: never launchable, refresh never promotes it to `available`),
  `missing-input` (concrete gap names the un-staged local resource),
  `not-converted` (upstream recipe exists, no package yet),
  `unavailable` (blocked with reason). Plus `UpstreamStatus` (what the
  source claimed) and `Evidence` (receipt paths/hashes, proof links).
- Rules: nothing is `available` without staged, hash-pinned LOCAL inputs;
  nothing here means runtime-verified (no such state; dw-verify owns that).
  Nothing is `staged` without hash-pinned local evidence, empty gaps, and
  an explicit unverified note; a mixed `staged` entry still names its typed
  bridge identity. Refresh keeps `staged` on verified receipts and demotes
  honestly to `missing-input` otherwise — it never promotes `staged` to
  `available`.
  `UpstreamStatus: ready` alone never implies launchable. Staging receipts
  (`{id, localPath, sha256, stagedUtc}`) are applied by
  `LocalInputMapper.Refresh` (tool `refresh` command); each kind names its
  own required resource: engine-native demos need the staged host (no
  upstream package needed), converted apps need the hash-pinned package
  copy, scenes need the app folder plus transform inputs. Refresh is
  cumulative (RV1745-CATFIX): `--receipts` is repeatable, the union is the
  new batch, and still-present earlier staging is carried forward from its
  own `local:` evidence only while the evidenced bytes still match the
  evidenced digest on disk, so a partial batch never silently demotes
  earlier inputs; a vanished staged path demotes honestly and an invalid
  receipt aborts unwritten. The mixed pilot needs a proven interop session.
- Digest trust (RVW2100): a receipt `sha256` MUST be 64 hex characters, and
  verifiable bytes are compared, never just checked nonempty (see
  `src/DesktopWasm.Catalog/ReceiptVerifier.cs`). A FILE receipt pins that
  file's SHA-256. A DIRECTORY containing `app-manifest.json` pins the
  MANIFEST digest (e.g. `hello-real` pin `E51A..FDC` equals the SHA-256 of
  `inputs/wpf/catalog-staging/hello-real/app-manifest.json`) plus the
  manifest-claimed closure (each listed assembly file present with matching
  bytes, e.g. `HelloWorld.<sha>.bin`). A directory WITHOUT a manifest
  (e.g. `paint` `targets/paint.net` pin `56BC..1310`,
  `benchmark-fiddler-classic` pin `2310..FAC8`) carries ASSERTED import-lane
  provenance verified by existence only; its pin is never presented as a
  byte-verification fact and no tree hash may be redefined onto it. Fresh
  receipts with wrong/stale/non-hex digests abort `refresh` unwritten;
  carried evidence whose bytes changed is not carried (the entry demotes to
  `missing-input` honestly). Nonempty arbitrary hash strings are not
  verification.

## Current contents (140 entries: 139 imported + testapp-joint; available=8)

- WinForms 89: 20 fixed `SceneState` entries, 42 Syncfusion demos,
  27 desktop benchmarks. All `missing-input` until the import lane stages
  and pins their folders (several point at `E:/tmp` or un-copied targets),
  except `smoke` (RECOVER-CATALOG-REC): browser-proven on generation
  20261031T003154Z, staged backend bundle manifest
  `inputs/winforms/catalog-staging/canvas-host-20261031T003154Z/blazor.boot.json`
  (sha `5913C8D8…78DE59` == shell `backend:canvas-boot-manifest` pin), now
  `available` (runtime unverified); plus `syncfusion-auto-complete`
  (RECOVER-CATALOG-REC2): same staged backend bundle + observed
  launch/render acceptance (receipt
  `inputs/winforms/catalog-staging/receipts-rec2-demo.json`, runtime
  unverified, scene content still resolves from mutable E:/tmp scratch).
- WPF 49: 35 engine-native demos + 2 converted real apps from DemoCatalog,
  12 app-library-only externals (6 converted-with-package incl. one
  conversion-failed with concrete gaps, 6 not-converted). Converted ones are
  `missing-input` (package copy pending), the rest `not-converted`, except
  `controls` + `input` (RECOVER-CATALOG-REC): browser-proven on generation
  20261031T003154Z, staged host bundle manifest
  `inputs/wpf/catalog-staging/browserhost-20261031T003154Z/blazor.boot.json`
  (sha `37660C5D…9623A` == shell `wpf:build-boot-manifest` pin), now
  `available` (receipt `inputs/wpf/catalog-staging/receipts-rec-demo.json`,
  runtime unverified), and
  `hello-real` (RV1745): converted package (1 assembly, 0 gaps) staged at
  `inputs/wpf/catalog-staging/hello-real`, receipt
  `inputs/wpf/catalog-staging/receipts-rv1745-catalog.json`, now `available`
  (staged, hash-pinned, runtime unverified); and `material-design` (RVW2100):
  valid converted manifest (11 assemblies, 0 gaps) outranked stale upstream
  `not-converted` metadata for preparation eligibility only (decision
  RVW-20261030-2100-4467 §15.1; `upstreamStatus` stays `not-converted`),
  staged at `inputs/wpf/catalog-staging/material-design`, receipt
  `inputs/wpf/catalog-staging/receipts-rv2100-material-design.json`, now
  `available` (manifest digest + 11/11 closure verified, runtime unverified).
  Next verified-present spares: geometry, simple-binding, wpf-calculator
  (each 1 assembly, 0 gaps, byte-identical frozen vs new-root BrowserHost copies).
- `mixed-session-pilot`: capabilities `winforms+wpf`, `unavailable`,
  no bridge identity, gap owned by the interop lane. Proves the mixed
  descriptor end to end without claiming mixed execution works.
- `testapp-joint` (RECOVER-CATALOG-2, re-pinned RECOVER-CATALOG-REC2):
  capabilities `winforms+wpf`, `staged` (serving + behavior unverified,
  never launchable). Converted joint package from the packaging producer:
  manifest digest `7b1c88…938892` (regenerated 2026-10-31T01:08Z; prior
  `de3c34b…b17b63` honestly demoted by digest-trust, then re-pinned from
  the producer's rewritten PROVENANCE pin), 11 `mixed`-artifact assemblies
  incl. entry `TestApp`, 0 gaps; bridge provider
  `DesktopWasm.Interop.WindowsFormsHostResolver` + contract
  `System.Windows.Forms.Integration.WindowsFormsHost` verbatim from the
  producer receipt. Staged at `inputs/mixed/testapp-package`, receipt
  `inputs/mixed/receipts-testapp-joint.json`.
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
# Live launch checks and input inventory (2026-10-31)

The persisted catalog's `status`/`available` fields describe its staged-input
records. They do not by themselves describe what an already-running imported
host can launch. In particular, `missingInput` may mean the migration receipt
has not been reconciled, while that host already has the app's source files.

The catalog UI labels this state as input inventory, not a blanket claim that
the application cannot run. Single-framework Open links go to `/app/{id}` for
the host readiness check. `/launch/{id}` offers Open when the matching host
reports ready, without a contradictory unavailable alert or an irrelevant
failure for the other framework.

For WinForms on-demand scenes, readiness requires the backend menu entry and a
real successful JSON app manifest with an entry and nonempty file list. A menu
claiming availability with missing app files, HTML fallback or an empty manifest
does not enable launch. Built-in scenes do not need an on-demand manifest.
Readiness permits an attempt to boot; rendered behavior remains a separate
browser acceptance result. Mixed-session acceptance remains separately gated.
