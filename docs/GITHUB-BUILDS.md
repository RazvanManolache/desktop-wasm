# GitHub build scope

The workflow compiles the complete source-only solution, runs twelve complete
test suites that do not need separately staged applications, and publishes a
standalone WPF browser-demo ZIP. It includes the local Python server, licenses,
commit ID and SHA-256 checksum. Python 3.10+ is needed only to serve the download;
the .NET SDK is not needed on the user's machine.

The published library contains 35 first-party toolkit demos. Converted external
applications and the WinForms backend are not included in this download. CI
launches the packaged calculator and project planner in headless Chromium,
both at the server root and mounted under `/wpf`. It requires rendered frames
and preserves mounted navigation, with screenshots and JSON results retained
alongside test reports. This does
not establish full compatibility or interactive coverage of every demo.

## Remaining engine diagnostics

The initial full-solution audit reported 1,691 warnings (including repeated
project builds). These include nullable contracts, imported compatibility
types, unused ABI fields, obsolete API shims and test assertion diagnostics.
The workflow keeps the full compiler log visible and uploads it; it does not
globally suppress these diagnostics or describe the engine as warning-free.
The catalog and shell projects have their warnings corrected and are rebuilt
with warnings treated as errors.

The broader test suite also includes tests requiring omitted third-party demo
closures, local E:/tmp fixtures and separately staged binaries, plus existing
engine test failures. Those suites are not the source-only CI gate. The twelve
suites in `scripts/ci/build.ps1` run without filtering individual failing tests;
adding the remaining suites requires their inputs and engine fixes first.

Successful main-branch builds replace the browser demo and evidence downloads
on the rolling [`ci-latest` prerelease](https://github.com/RazvanManolache/desktop-wasm/releases/tag/ci-latest).
These are development previews; versioned releases remain separate. The
download contains no external application binaries. The workflow does not
start local development servers or scheduled workers.
