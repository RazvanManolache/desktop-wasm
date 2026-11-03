# Running the restored demos

The shared entry is http://127.0.0.1:5220/. Its Windows Forms Studio and WPF
Studio links open the original demo interfaces. Direct links:

- WinForms smoke form: http://127.0.0.1:5220/app/smoke
- WPF controls: http://127.0.0.1:5220/demo/controls
- Paint: http://127.0.0.1:5220/app/paint
- Converted WPF HelloWorld: http://127.0.0.1:5220/app/hello-real

From this workspace, after the existing servers have been stopped by their
recorded owner:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/unified/start-demos.ps1
```

The launcher refuses occupied ports. It builds under the coordination build
lease, stages source static assets **and** compiled Blazor payloads, includes
scoped CSS, verifies every boot resource hash, copies server binaries into a
fresh deployment, and launches the servers hidden on loopback 5220/5230.
It records exact process IDs, roots, and logs in the deployment directory's
`deployment.json`. It only cleans up processes it just created on startup failure.
Existing listeners and old project roots are never stopped or modified.

`-NoBuild` uses already-built Debug outputs. `-PrepareOnly` stages and verifies
without launching. `-WpfRoot` and `-PackagesRoot` select an explicit immutable
WPF generation; the default selects the latest staged generation under the
coordination verification tree. The staged generation must already contain the
converted app packages. This launcher does not claim to convert new inputs.

`-ShellOnly` starts a replacement gateway on free port 5220 while reusing the
existing backend on 5230. The existing backend is checked and recorded but is
never owned/stopped by that invocation, including on startup failure.

Do not point the shell at `src/DesktopWasm.Web/wwwroot` alone: that directory
does not contain the compiled runtime. Likewise CanvasHost source static files
alone are not a working WinForms deployment. ASP.NET's local build asset
manifest is not automatically enabled by a Production launch; use the complete
physical roots assembled by this launcher. Microsoft documents this environment
behavior in [static web assets guidance](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/static-files?view=aspnetcore-9.0).

Browser acceptance against the running deployment:

```powershell
python scripts/unified/smoke-demos.py
```

For the stale-inventory launch regression, use `--cases autocomplete,catalog`.
This checks that the AutoComplete launch page offers Open without an unavailable
alert, opens the actual app, and exercises its text field.

The check uses fresh browser contexts, follows both studio links from the shared
catalog, verifies rendered runtimes, clicks both demo counters, checks Paint
stroke/undo pixels, and loads converted WPF HelloWorld. Screenshots and JSON
results are retained under `.artifacts/demo-repair/browser` by default.

The experimental mixed AvalonDock TestApp still has an Application-root BAML
startup limitation. Its package validation is not evidence of successful mixed
execution. That limitation is independent of the restored original demos.
