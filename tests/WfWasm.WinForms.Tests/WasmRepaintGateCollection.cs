// FACTORY1615-FLAKE-WPF: the static WasmRepaintGate is process-wide;
// parallel xUnit collections drain each other's marks (proven flake:
// CaretRegionMarkRecordsCaretRect red 3/4 in-suite, green isolated).
// Tests touching the gate share this non-parallel collection. No
// assertion or product change; serialization only.
namespace WfWasm.WinForms.Tests;

[CollectionDefinition("WasmRepaintGate", DisableParallelization = true)]
public sealed class WasmRepaintGateCollection
{
}
