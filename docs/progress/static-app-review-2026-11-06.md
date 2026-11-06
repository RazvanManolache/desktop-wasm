# Small converted applications for the static showcase

Reviewed 6 November 2026 with owned headless Chromium contexts and a
physical-files-only server mounted at `/desktop-wasm/`. Each application used
its existing converted entry assembly. Tests checked the original managed form
type to detect accidental substitution with a host demonstration.

The shared browser runtime is downloaded on first launch for each framework.
The sizes below measure each entry assembly and its declared content. Additional
dependency assemblies increase the final package size for the incomplete cases.

## Curated for publication

| Application | App payload | Interaction verified |
| --- | ---: | --- |
| Calculator | 15 KiB | Entered `7 + 8 =` and observed `15` painted by the original form. |
| To-do | 62 KiB | Typed a task and added it to the original task list. |
| Original Tic-Tac-Toe | 267 KiB | Played a winning X row, observed the winner and updated score, dismissed Game Over and reset the round. |

The three converted inputs occupy 118 KiB compressed. CI downloads an immutable
[input bundle](../../scripts/ci/showcase-inputs.json) and verifies its SHA-256.
The site includes four source-built WPF mini apps alongside these WinForms apps.
The combined deployment is approximately 88 MiB uncompressed. Closing a gallery
app removes its runtime iframe. Both framework entry URLs survive browser refresh
on a server that has no application API or routing rewrite.

## Further candidates

| Application | Declared payload | Current static-host result | Work before publication |
| --- | ---: | --- | --- |
| Calendar | 27 KiB | Original month grid renders. Clicking a day opens the Add Event dialog. | Check event creation and adapt its 1080 by 830 window to the gallery viewport. |
| Ruben Roy Calculator | 283 KiB | Original calculator renders and computes `7 + 8 = 15`. | Add attribution and a permanent CI case. |
| VGAPainter | 134 KiB | Original palette and canvas render. Drawing marks the document dirty. | Check pixel output, palette changes and browser file export. |
| Minesweeper | 394 KiB | Starts a game and reveals numbered cells. | Settings persistence raises configuration errors. Check restart, flagging and win/loss. |
| FastColoredTextBox tester | 287 KiB | Original sample launcher renders. Opening the powerful sample fails. | Include the converted FastColoredTextBox dependency and verify editing. |
| Cyotek Color Picker | 412 KiB | Original form opens with an index-out-of-range thread exception. | Resolve the exception and verify color changes. |
| HexBox | 186 KiB | Form creation fails. | Include `Be.Windows.Forms.HexBox` in the static closure. |
| AdvancedDataGridView | 43 KiB | Form creation fails. Eight declared content files were included. | Include `AdvancedDataGridView` in the static closure and check filtering. |
| DockPanel Suite | 172 KiB | Form creation fails. | Include `WeifenLuo.WinFormsUI.Docking` and check docking. |
| Krypton TestForm | 7.82 MiB | Form creation fails. | Include `Krypton.Toolkit`; measure the resulting closure. |
| ReaLTaiizor | 3.91 MiB | Form creation fails. | Include `ReaLTaiizor`; measure the resulting closure. |
| Disk treemap | 300 KiB | Form creation fails. | Include `CommunityToolkit.Mvvm`; file-system input also needs browser validation. |
| CHIP-8 | 24 KiB | Form creation fails. | Supply the expected `System.Windows.Input.Key` WindowsBase type, then check ROM input and emulation. |

The smoke-form manifest endpoint returned 404 during this review. These findings
describe the frozen static packages. A missing closure in this packaging experiment
does not establish the behavior of the full local backend, which can resolve
assemblies on demand.

The next small additions with the strongest interaction evidence are the second
calculator, Calendar and VGAPainter. Minesweeper needs the settings issue addressed
before it receives a runnable gallery card.
