// Catalog import tool: frozen roots in, canonical catalog out.
//   import --winforms-root <p> --wpf-root <p> --out <catalog-dir>
//   verify --winforms-root <p> --wpf-root <p> --catalog <catalog-dir>
//   refresh --catalog <dir> --receipts <json> [--receipts <json>...]
//   (repeatable; the union is the new batch, still-present earlier staging
//   verified on disk is carried forward, invalid receipts abort unwritten)
// Snapshots are migration provenance only; runtime never needs old roots.
// Exit codes: 0 ok, 2 bad input, 3 drift/validation failure.
using DesktopWasm.Catalog.Tool;

var cliArgs = Environment.GetCommandLineArgs().Skip(1).ToArray();
if (cliArgs.Length == 0 || cliArgs[0] is "-h" or "--help")
{
    Console.WriteLine("usage: import|verify|refresh [options]");
    Console.WriteLine("  import  --winforms-root <p> --wpf-root <p> --out <catalog-dir>");
    Console.WriteLine("  verify  --winforms-root <p> --wpf-root <p> --catalog <catalog-dir>");
    Console.WriteLine("  refresh --catalog <dir> --receipts <json>");
    return 0;
}

Dictionary<string, string> opts;
try
{
    opts = Parse(cliArgs[1..]);
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine("error: " + ex.Message);
    return 2;
}

try
{
    return cliArgs[0] switch
    {
        "import" => Importer.Import(Get(opts, "winforms-root"), Get(opts, "wpf-root"), Get(opts, "out")),
        "verify" => Importer.Verify(Get(opts, "winforms-root"), Get(opts, "wpf-root"), Get(opts, "catalog")),
        "refresh" => Importer.Refresh(Get(opts, "catalog"), Receipts(cliArgs)),
        _ => Fail($"unknown command '{cliArgs[0]}'"),
    };
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine("error: " + ex.Message);
    return 2;
}
catch (Exception ex)
{
    Console.Error.WriteLine("failure: " + ex.Message);
    return 3;
}

static Dictionary<string, string> Parse(string[] rest)
{
    var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < rest.Length; i++)
    {
        if (!rest[i].StartsWith("--", StringComparison.Ordinal) || i + 1 >= rest.Length)
            throw new ArgumentException($"Bad arguments near '{rest[Math.Min(i, rest.Length - 1)]}'.");
        map[rest[i][2..]] = rest[++i];
    }
    return map;
}

static string Get(Dictionary<string, string> opts, string key) =>
    opts.TryGetValue(key, out var value) ? value : throw new ArgumentException($"Missing --{key}.");

/// <summary>All --receipts values in order; at least one is required.</summary>
static IReadOnlyList<string> Receipts(string[] cliArgs)
{
    var list = new List<string>();
    for (var i = 1; i + 1 < cliArgs.Length; i++)
        if (cliArgs[i] == "--receipts") list.Add(cliArgs[i + 1]);
    return list.Count > 0 ? list : throw new ArgumentException("Missing --receipts.");
}

static int Fail(string message)
{
    Console.Error.WriteLine("error: " + message);
    return 2;
}
