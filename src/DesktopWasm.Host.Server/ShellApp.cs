using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace DesktopWasm.Host.Server;

/// <summary>
/// Unified shell gateway (MERGE-SHELL lane): one catalog API + launcher portal
/// on port 5220 for both WinForms and WPF apps, served from the canonical
/// catalog/apps.json registry. Framework boot stays behind runtime/session
/// adapters in DesktopWasm.Web; pure apps mount imported hosts only after
/// source-ready, and mixed-framework sessions require both engine adapters in
/// one session (no iframe substitute).
/// </summary>
public static class ShellApp
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static WebApplication Create(ShellOptions options, string[]? args = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args ?? [],
            ContentRootPath = options.ContentRootPath,
            WebRootPath = options.WebRootPath ?? options.ContentRootPath,
        });
        string? catalogFromConfig = builder.Configuration["Catalog:Path"];
        if (!string.IsNullOrWhiteSpace(catalogFromConfig))
            options = options with { CatalogPath = catalogFromConfig };
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole();

        var app = builder.Build();
        Configure(app, options);
        return app;
    }

    public static void Configure(WebApplication app, ShellOptions options)
    {
        string catalogPath = ResolveCatalogPath(app, options.CatalogPath);
        string packagesRoot = options.PackagesRoot
            ?? Path.Combine(Path.GetDirectoryName(catalogPath) ?? app.Environment.ContentRootPath, "packages");
        string logDir = options.LogDir ?? Path.Combine(app.Environment.ContentRootPath, "logs");

        var catalog = new FileCatalogProvider(catalogPath);
        var packages = new PackageService(packagesRoot);
        var sessions = new SessionService();
        ArgumentNullException.ThrowIfNull(app);

        app.MapGet("/api/healthz", () => Results.Ok(new
        {
            status = "ok",
            port = 5220,
            catalogLoaded = catalog.Loaded,
            catalogCount = catalog.All.Count,
            catalogSha256 = catalog.CatalogSha256,
            catalogPath,
            portalSha256 = PortalHash(app),
            loadError = catalog.LoadError,
        }));

        app.MapGet("/api/catalog", (string? framework, string? q, string? status) =>
        {
            if (!catalog.Loaded)
                return Results.Json(new { error = "catalog-unavailable", detail = catalog.LoadError },
                    statusCode: 503);
            if (framework is not null && !IsFrameworkFilter(framework))
                return Results.BadRequest($"unknown framework filter '{framework}': expected winforms, wpf or mixed");
            return Results.Ok(catalog.Filter(framework, q, status).Select(PublicApp));
        });

        app.MapGet("/api/catalog/{id}", (string id) =>
        {
            if (!catalog.Loaded)
                return Results.Json(new { error = "catalog-unavailable", detail = catalog.LoadError },
                    statusCode: 503);
            var record = catalog.Find(id);
            if (record is null)
                return Results.NotFound(new
                {
                    error = "unknown-app",
                    unknownId = id,
                    hint = "Pick an id from /api/catalog; legacy spellings resolve as aliases.",
                });
            return Results.Ok(PublicApp(record));
        });

        app.MapGet("/api/packages/{id}/manifest", (string id) =>
        {
            var record = catalog.Find(id);
            if (record is null)
                return Results.NotFound(new { error = "unknown-app", unknownId = id });
            try
            {
                var files = packages.Manifest(record.Id);
                string manifestHash = Convert.ToHexString(SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(
                        string.Join("\n", files.Select(f => $"{f.Name}:{f.Sha256}")))));
                return Results.Ok(new
                {
                    app = record.Id,
                    manifestSha256 = manifestHash,
                    files,
                    availabilityNote = record.Available
                        ? null
                        : $"app is '{record.Status}': {string.Join("; ", record.Gaps)}",
                });
            }
            catch (PackageUnavailableException ex)
            {
                return Results.Json(new { error = "package-unavailable", detail = ex.Message }, statusCode: 503);
            }
        });

        app.MapGet("/api/packages/{id}/content", (string id, string name, HttpContext context, string? v) =>
        {
            var record = catalog.Find(id);
            if (record is null)
                return Results.NotFound(new { error = "unknown-app", unknownId = id });
            try
            {
                var (bytes, mime, hash) = packages.Content(record.Id, name, v);
                context.Response.Headers["X-DesktopWasm-Sha256"] = hash;
                context.Response.Headers.CacheControl = v is not null
                    ? "private,max-age=31536000,immutable" : "no-cache";
                return Results.File(bytes, mime);
            }
            catch (ContentMismatchException ex)
            {
                return Results.Conflict(new { error = "content-mismatch", detail = ex.Message });
            }
            catch (PackageUnavailableException ex)
            {
                return Results.Json(new { error = "package-unavailable", detail = ex.Message }, statusCode: 503);
            }
            catch (FileNotFoundException ex)
            {
                return Results.NotFound(new { error = "unknown-content", detail = ex.Message });
            }
        });

        app.MapPost("/api/sessions", (SessionRequest? request) =>
        {
            if (request is null || string.IsNullOrWhiteSpace(request.AppId))
                return Results.BadRequest("appId required");
            var record = catalog.Loaded ? catalog.Find(request.AppId) : null;
            if (catalog.Loaded && record is null)
                return Results.NotFound(new { error = "unknown-app", unknownId = request.AppId });
            try
            {
                var session = sessions.Create(
                    record?.Id ?? CatalogVocabulary.Normalize(request.AppId), request.SessionId);
                return Results.Ok(session);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(ex.Message);
            }
        });

        app.MapPost("/api/client-errors", (List<ShellErrorReport>? batch) =>
        {
            try
            {
                int logged = ShellErrorLog.LogBatch(logDir, batch ?? []);
                return Results.Ok(new { logged });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(ex.Message);
            }
        });

        app.MapGet("/api/client-errors", ([FromQuery(Name = "app")] string? appName) =>
            Results.Ok(ShellErrorLog.ReadIndex(logDir, appName)));

        app.MapPost("/api/errors/mark-fixed", (MarkFixedRequest? request) =>
        {
            if (request is null || string.IsNullOrWhiteSpace(request.Signature))
                return Results.BadRequest("signature required");
            return ShellErrorLog.MarkFixed(logDir, request.Signature, request.Note ?? "")
                ? Results.Ok(new { signature = request.Signature, status = ShellErrorLog.StatusFixed })
                : Results.NotFound("unknown signature");
        });

        app.MapGet("/errors", ([FromQuery(Name = "app")] string? appName) =>
        {
            var index = ShellErrorLog.ReadIndex(logDir, appName);
            var rows = string.Join("", index.Select(e =>
                $"<tr><td>{Html(e, "signature")}</td><td>{Html(e, "app")}</td><td>{Html(e, "kind")}</td><td>{Html(e, "message")}</td><td>{Html(e, "status")}</td></tr>"));
            return Results.Content(
                "<!doctype html><html><head><meta charset='utf-8'><title>Shell error inventory</title></head>" +
                $"<body><h1>Shell error inventory{(appName is null ? "" : $" · {System.Net.WebUtility.HtmlEncode(appName)}")}</h1>" +
                "<table border='1'><tr><th>signature</th><th>app</th><th>kind</th><th>message</th><th>status</th></tr>" +
                rows + "</table></body></html>", "text/html");
        });

        // Explicit legacy aliases: WinForms /app/{slug} and WPF /demo|/runtime/{slug}
        // resolve through the same registry into the unified launcher route.
        foreach (string prefix in new[] { "/app", "/demo", "/runtime" })
        {
            app.MapGet(prefix + "/{slug}", (string slug, HttpContext context) =>
            {
                var record = catalog.Loaded ? catalog.Find(slug) : null;
                if (record is null)
                    return Results.NotFound(new
                    {
                        error = "unknown-app",
                        unknownId = slug,
                        hint = $"No app is registered under '{slug}'. Pick one from /api/catalog.",
                    });
                string query = context.Request.QueryString.Value ?? "";
                return Results.Redirect(record.Launch.Route + query, preserveMethod: false);
            });
        }

        static string Html(JsonElement element, string property) =>
            System.Net.WebUtility.HtmlEncode(
                element.TryGetProperty(property, out var value) ? value.GetString() ?? "" : "");

        // Portal static files (DesktopWasm.Web build output when present).
        // The Blazor publish output uses extensions outside the default
        // static-file map (.dat ICU data, .wasm, compressed .br/.gz), so map
        // them explicitly; the webroot is our own build output.
        string webRoot = app.Environment.WebRootPath;
        if (Directory.Exists(webRoot) && File.Exists(Path.Combine(webRoot, "index.html")))
        {
            var contentTypes = new Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider();
            contentTypes.Mappings[".dat"] = "application/octet-stream";
            contentTypes.Mappings[".wasm"] = "application/wasm";
            contentTypes.Mappings[".br"] = "application/octet-stream";
            contentTypes.Mappings[".gz"] = "application/octet-stream";
            contentTypes.Mappings[".dll"] = "application/octet-stream";
            app.UseStaticFiles(new StaticFileOptions
            {
                ContentTypeProvider = contentTypes,
                ServeUnknownFileTypes = true,
                DefaultContentType = "application/octet-stream",
            });
            app.MapFallbackToFile("index.html");
        }
        else
        {
            app.MapGet("/", () => Results.Content(
                "<!doctype html><html><head><meta charset='utf-8'><title>DesktopWasm unified shell</title></head><body>" +
                "<h1>DesktopWasm unified shell :5220</h1>" +
                "<p>Portal build (DesktopWasm.Web) is not staged yet; the catalog API below is the source of truth.</p>" +
                "<ul><li><a href='/api/healthz'>/api/healthz</a></li><li><a href='/api/catalog'>/api/catalog</a></li><li><a href='/errors'>/errors</a></li></ul>" +
                "</body></html>", "text/html"));
        }
    }

    public static object PublicApp(ShellAppRecord record) => new
    {
        record.Id,
        record.Label,
        record.Description,
        record.Category,
        record.Frameworks,
        mixed = record.IsMixed,
        record.Kind,
        record.Status,
        record.Available,
        record.Gaps,
        record.Provenance,
        launch = new
        {
            route = string.IsNullOrWhiteSpace(record.Launch.Route) ? $"/launch/{record.Id}" : record.Launch.Route,
            record.Launch.LegacyRoutes,
        },
    };

    public static bool IsFrameworkFilter(string framework)
    {
        string f = framework.Trim().ToLowerInvariant();
        return f is "winforms" or "wpf" or "mixed";
    }

    public static string? PortalHash(WebApplication app)
    {
        try
        {
            string index = Path.Combine(app.Environment.WebRootPath, "index.html");
            if (!File.Exists(index))
                return null;
            return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(index)));
        }
        catch
        {
            return null;
        }
    }

    public static string ResolveCatalogPath(WebApplication app, string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured))
            return configured;
        string? env = Environment.GetEnvironmentVariable("DESKTOPWASM_CATALOG");
        if (!string.IsNullOrWhiteSpace(env))
            return env;
        string dir = app.Environment.ContentRootPath;
        for (int i = 0; i < 7 && dir is not null; i++)
        {
            string candidate = Path.Combine(dir, "catalog", "apps.json");
            if (File.Exists(candidate))
                return candidate;
            dir = Path.GetDirectoryName(dir)!;
        }
        return Path.Combine(app.Environment.ContentRootPath, "catalog", "apps.json");
    }
}

public sealed record ShellOptions(
    string? CatalogPath = null,
    string? PackagesRoot = null,
    string? LogDir = null,
    string? ContentRootPath = null,
    string? WebRootPath = null);

public sealed record SessionRequest(string? AppId, string? SessionId);

public sealed record MarkFixedRequest(string Signature, string? Note);
