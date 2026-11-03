using System.Security.Cryptography;
using System.Text.Json;
using DesktopWasm.AppModel;
using DesktopWasm.Catalog;
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
        builder.Services.AddResponseCompression(compressionOptions =>
        {
            compressionOptions.EnableForHttps = true;
            compressionOptions.MimeTypes = Microsoft.AspNetCore.ResponseCompression.ResponseCompressionDefaults.MimeTypes.Concat(
                new[] { "application/octet-stream", "application/wasm", "application/json" });
        });

        var app = builder.Build();
        Configure(app, options);
        return app;
    }

    public static void Configure(WebApplication app, ShellOptions options)
    {
        string catalogPath = ResolveCatalogPath(app, options.CatalogPath);
        // Launch harnesses point the package seam at a frozen generation's
        // packages/ dir via DESKTOPWASM_PACKAGES; default stays beside the
        // catalog. Explicit options win over the environment.
        string packagesRoot = options.PackagesRoot
            ?? Environment.GetEnvironmentVariable("DESKTOPWASM_PACKAGES")
            ?? Path.Combine(Path.GetDirectoryName(catalogPath) ?? app.Environment.ContentRootPath, "packages");
        string logDir = options.LogDir ?? Path.Combine(app.Environment.ContentRootPath, "logs");

        var catalog = new CatalogSource(catalogPath);
        var packages = new PackageService(packagesRoot);
        var sessions = new SessionService();
        string backendUrl = options.WinFormsBackendUrl
            ?? Environment.GetEnvironmentVariable("DESKTOPWASM_WFBACKEND")
            ?? "http://127.0.0.1:5230";
        // Normalized once at startup: a trailing separator would otherwise
        // break the static-root prefix match for every file (TryMapWpfPath).
        string? wpfRoot = NormalizeWebRoot(options.WpfWebRootPath
            ?? Environment.GetEnvironmentVariable("DESKTOPWASM_WPFROOT"));
        var backendHttp = new HttpClient(
            options.BackendHandler ?? new SocketsHttpHandler(), disposeHandler: true)
        {
            BaseAddress = new Uri(backendUrl),
            Timeout = TimeSpan.FromSeconds(30),
        };
        var mounts = new MountService(backendHttp, wpfRoot);
        ArgumentNullException.ThrowIfNull(app);
        if (app.Services.GetService(typeof(Microsoft.AspNetCore.ResponseCompression.IResponseCompressionProvider)) is not null)
        {
            app.UseResponseCompression();
        }

        // Isolated app origins: {slug}.localhost (and retry-*.localhost) belong
        // entirely to the mounted WinForms backend (runtime.html, framework,
        // APIs). The subdomain IS the isolation boundary; proxy everything.
        app.Use(async (context, next) =>
        {
            string host = context.Request.Host.Host;
            if (host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase))
            {
                string rest = (context.Request.Path.Value ?? "/").TrimStart('/');
                // The isolated runtime page addresses shared shell assets
                // with winforms/-relative URLs; re-root one level so they
                // land on the backend's absolute paths.
                if (rest.StartsWith("winforms/", StringComparison.Ordinal))
                    rest = rest["winforms/".Length..];
                var result = await mounts.ProxyWinFormsAsync(context, rest);
                await result.ExecuteAsync(context);
                return;
            }
            await next();
        });

        app.MapGet("/api/healthz", () =>
        {
            var missing = StaticPreflight(app.Environment.WebRootPath, wpfRoot, catalogPath);
            return Results.Ok(new
            {
                status = "ok",
                port = 5220,
                catalogLoaded = catalog.Loaded,
                catalogCount = catalog.Registry?.Count ?? 0,
                catalogSha256 = catalog.Sha256,
                catalogPath,
                portalSha256 = PortalHash(app),
                loadError = catalog.LoadError,
                wpfRoot,
                wpfStaged = wpfRoot is not null && File.Exists(Path.Combine(wpfRoot, "index.html")),
                portalStaged = File.Exists(Path.Combine(app.Environment.WebRootPath, "index.html")),
                wpfManifestStaged = wpfRoot is not null && File.Exists(
                    Path.Combine(wpfRoot, "_framework", "blazor.boot.json")),
                wpfEntryStaged = wpfRoot is not null && File.Exists(
                    Path.Combine(wpfRoot, "entry.mjs")),
                staticOk = missing.Count == 0,
                staticMissing = missing,
            });
        });

        // Missing-static-asset preflight: fail visibly (503 + missing list)
        // instead of starting a broken deployment that renders a silent
        // black canvas or serves fallback HTML with framework MIME types.
        // Launch harnesses must gate serves on this endpoint (or the
        // scripts/unified/served-generation-preflight.ps1 file check) and
        // refuse to start when it reports missing assets.
        app.MapGet("/api/static-preflight", () =>
        {
            var missing = StaticPreflight(app.Environment.WebRootPath, wpfRoot, catalogPath);
            if (missing.Count == 0)
                return Results.Ok(new { ok = true, missing = missing });
            return Results.Json(new
            {
                ok = false,
                error = "static-asset-missing",
                missing = missing,
                hint = "Stage the missing static assets (or point DESKTOPWASM_WEBROOT/DESKTOPWASM_WPFROOT at a staged root) before serving; a deployment missing these files renders a black canvas instead of the app.",
            }, statusCode: 503);
        });

        app.MapGet("/api/catalog", (string? framework, string? q, string? status) =>
        {
            if (!catalog.Loaded)
                return Results.Json(new { error = "catalog-unavailable", detail = catalog.LoadError },
                    statusCode: 503);
            if (framework is not null && !IsFrameworkFilter(framework))
                return Results.BadRequest($"unknown framework filter '{framework}': expected winforms, wpf or mixed");
            if (status is not null && !Enum.TryParse<AppAvailability>(status.Trim(), ignoreCase: true, out _))
                return Results.BadRequest($"unknown status filter '{status}': expected available, missingInput, notConverted or unavailable");
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
                    availability = CatalogSource.StatusOf(record),
                    availabilityNote = record.AvailabilityNote,
                    gaps = record.Gaps,
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

        app.MapGet("/api/mount-status", async ([FromQuery(Name = "app")] string appId, string? session) =>
        {
            var record = catalog.Loaded ? catalog.Find(appId) : null;
            if (record is null)
                return Results.NotFound(new { error = "unknown-app", unknownId = appId });
            string sessionId;
            try
            {
                sessionId = string.IsNullOrWhiteSpace(session)
                    ? sessions.Create(record.Id).SessionId
                    : SessionService.GuardSession(session);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(ex.Message);
            }
            object? winforms = null, wpf = null;
            if (record.Capabilities.HasFlag(AppCapability.WinForms))
                winforms = await mounts.WinFormsStatusAsync(record, sessionId);
            if (record.Capabilities.HasFlag(AppCapability.Wpf))
                wpf = mounts.WpfStatus(record, sessionId);
            return Results.Ok(new { app = record.Id, sessionId, winforms, wpf });
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
                    record?.Id ?? request.AppId.Trim().ToLowerInvariant(), request.SessionId);
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

        // Transitional mount: the imported WinForms host runs unmodified on its
        // backend port; absolute backend paths re-root under /winforms/.
        // Backend-only APIs proxy verbatim; /api/client-errors and /errors
        // stay unified on the shell (aligned error reporting, per-app).
        app.MapMethods("/winforms/{**rest}", new[] { "GET", "POST", "PUT", "DELETE", "HEAD", "OPTIONS" },
            (string? rest, HttpContext context) => mounts.ProxyWinFormsAsync(context, rest ?? ""));
        foreach (string backendApi in new[]
        {
            "/api/appmanifest", "/api/appresolve", "/api/appasm", "/api/appcontent",
            "/api/preview-config", "/appfiles/{**rest}",
        })
        {
            string pattern = backendApi;
            // NOTE: keep the Func<,> type explicit. A bare (HttpContext)
            // lambda would bind the RequestDelegate overload and silently
            // discard the Task<IResult> (200 with an empty body).
            Func<HttpContext, Task<IResult>> proxy = (HttpContext context) =>
                mounts.ProxyWinFormsAsync(context,
                    pattern.StartsWith("/appfiles/", StringComparison.Ordinal)
                        ? "appfiles/" + (context.Request.RouteValues["rest"]?.ToString() ?? "")
                        : pattern[1..]);
            app.MapMethods(pattern, new[] { "GET", "POST", "HEAD" }, proxy);
        }

        // WPF mount: published static root under /wpf/, Blazor fallback to
        // the staged index (base-href rewritten). No root, no mount: 503.
        app.MapGet("/wpf/{**rest}", (string? rest, HttpContext context) =>
        {
            if (mounts.WpfWebRoot is null)
                return Results.Json(new
                {
                    error = "mount-unavailable",
                    detail = "WPF static root not staged; mount unavailable.",
                }, statusCode: 503);
            if (!TryMapWpfPath(mounts.WpfWebRoot, rest, out string full))
                return Results.NotFound(new { error = "unknown-content", detail = "path escapes the static root" });
            string first = (rest ?? "").Split('/', 2)[0];
            if ((first == "runtime" || first == "demo") && !File.Exists(full))
                return Results.Content(RuntimeShimHtml(), "text/html");
            if (File.Exists(full))
            {
                ApplyStaticAssetHeaders(context, full);
                if (TryGetPrecompressedFile(context, full, out string compressedPath, out string encoding))
                {
                    context.Response.Headers.ContentEncoding = encoding;
                    return Results.File(compressedPath, MimeFor(full));
                }
                return Results.File(full, MimeFor(full));
            }
            // Reserved package/asset namespaces never use the studio index
            // fallback: a missing manifest or framework asset must be an
            // honest 404 JSON error, not 200 SPA HTML the loader would
            // misparse as the package bytes. Genuine client-side navigation
            // routes below keep the studio fallback.
            if (IsReservedAssetNamespace(first))
                return Results.NotFound(new
                {
                    error = "unknown-content",
                    detail = $"no staged file for '{rest}'",
                });
            string index = Path.Combine(Path.GetFullPath(mounts.WpfWebRoot), "index.html");
            return Results.Content(MountService.RewriteBaseHref(
                File.ReadAllText(index), MountService.WpfPrefix), "text/html");
        });

        // Explicit legacy aliases resolve into the MOUNTED app when bootable,
        // else the unified launcher page (which shows the concrete gaps).
        // Deep-links preserve the caller's query (app selection survives the
        // redirect); a caller-supplied session is reused when valid and
        // rejected with 400 when malformed.
        foreach (string prefix in new[] { "/app", "/demo", "/runtime" })
        {
            string routePrefix = prefix;
            app.MapGet(routePrefix + "/{slug}", async (string slug, HttpContext context) =>
            {
                var record = catalog.Loaded ? catalog.FindLegacy(routePrefix, slug) : null;
                if (record is null)
                {
                    // FACTORY-shell-2: these prefixes are browser navigation, not
                    // API. A raw JSON 404 replaces the whole shell (library and
                    // the client's Unknown-app state become unreachable). Keep
                    // the honest 404 status but serve the shell page so the
                    // client renders its Unknown-app overlay. API surface stays
                    // under /api/* (JSON unknown-app pins there are untouched).
                    string shellIndex = Path.Combine(
                        app.Environment.WebRootPath ?? "", "index.html");
                    if (File.Exists(shellIndex))
                        return Results.Content(
                            await File.ReadAllTextAsync(shellIndex),
                            "text/html", statusCode: 404);
                    return Results.NotFound(new
                    {
                        error = "unknown-app",
                        unknownId = slug,
                        hint = $"No app is registered under '{slug}'. Pick one from /api/catalog.",
                    });
                }
                string? incomingSession = context.Request.Query.TryGetValue("session", out var sv)
                    ? sv.ToString() : null;
                string sessionId;
                try
                {
                    sessionId = sessions.Create(record.Id, incomingSession).SessionId;
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(ex.Message);
                }
                // WinForms mount keeps the legacy /app/ route.
                if (record.Capabilities.HasFlag(AppCapability.WinForms) && routePrefix == "/app")
                {
                    var status = await mounts.WinFormsStatusAsync(record, sessionId);
                    if (status.MountedUrl is not null)
                        return Results.Redirect(
                            WithPreservedQuery(status.MountedUrl, context.Request),
                            preserveMethod: false);
                }
                // The WPF studio routes itself at /app/{slug} (readRoute) and
                // boots runtimes in /runtime/ iframes: serve/redirect into the
                // staged studio root so its absolute routing keeps working.
                if (record.Capabilities.HasFlag(AppCapability.Wpf))
                {
                    var wpf = mounts.WpfStatus(record, sessionId);
                    // Converted apps with staged package bytes boot through
                    // the mounted runtime exactly like engine-native demos;
                    // unstaged ones still fall through to the launch page
                    // with its concrete gaps.
                    if (wpf.CanBoot || IsConvertedStaged(mounts, record))
                    {
                        if (routePrefix == "/app")
                            return ServeWpfStudio(mounts);
                        // The studio iframe stays same-origin: legacy runtime
                        // routes land on the shell-owned runtime page, which
                        // boots the staged runtime under /wpf/ scoping; legacy
                        // demo routes keep their library meaning and land on
                        // the studio for the app.
                        string target = routePrefix == "/runtime"
                            ? $"/wpf/runtime/{record.Id}"
                            : $"/app/{record.Id}";
                        return Results.Redirect(
                            WithPreservedQuery(target, context.Request),
                            preserveMethod: false);
                    }
                }
                return Results.Redirect(
                    WithPreservedQuery(
                        $"/launch/{record.Id}?session={Uri.EscapeDataString(sessionId)}",
                        context.Request),
                    preserveMethod: false);
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
                OnPrepareResponse = ctx =>
                {
                    ApplyStaticAssetHeaders(ctx.Context, ctx.File.PhysicalPath ?? ctx.File.Name);
                }
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

    /// <summary>API projection of the shared <see cref="AppDescriptor"/>.
    /// Field names follow CATALOG-CONTRACT.md; the shell adds only the
    /// unified <c>launch</c> route derived from the id.</summary>
    public static object PublicApp(AppDescriptor record)
    {
        string[] frameworks = CatalogSource.FrameworksOf(record);
        return new
        {
            record.Id,
            record.Label,
            record.Description,
            record.Category,
            record.Icon,
            frameworks,
            mixed = record.Capabilities == AppCapability.Mixed,
            kind = record.Entry.Kind,
            origin = record.Provenance.Origin,
            status = CatalogSource.StatusOf(record),
            available = record.Availability == AppAvailability.Available,
            record.AvailabilityNote,
            record.Gaps,
            record.Evidence,
            provenance = new
            {
                family = record.Provenance.Family,
                record.Provenance.Origin,
                record.Provenance.SourceUrl,
                record.Provenance.Repository,
                record.Provenance.Commit,
                record.Provenance.Project,
                record.Provenance.License,
                record.Provenance.Boundary,
                record.Provenance.HasRecipe,
            },
            entry = new
            {
                record.Entry.Kind,
                record.Entry.EntryAssembly,
                record.Entry.ManifestSlug,
                record.Entry.BootNote,
            },
            launch = new
            {
                route = $"/launch/{record.Id}",
                legacyRoutes = record.Aliases.Where(CatalogValidator.IsRouteAlias).ToArray(),
            },
        };
    }

    public static bool IsFrameworkFilter(string framework)
    {
        string f = framework.Trim().ToLowerInvariant();
        return f is "winforms" or "wpf" or "mixed";
    }

    /// <summary>Merges a deep-link's incoming query into a redirect target.
    /// Target params win on key collision (so the reserved session stands);
    /// caller extras (rendering flags, shared session choice already
    /// reserved above) append deterministically. No incoming query means the
    /// target passes through unchanged.</summary>
    public static string WithPreservedQuery(string target, HttpRequest request)
    {
        string incoming = request.QueryString.Value ?? "";
        if (incoming.Length <= 1)
            return target;
        string[] parts = incoming[1..].Split('&', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return target;
        var present = new HashSet<string>(StringComparer.Ordinal);
        int q = target.IndexOf('?');
        if (q >= 0)
            foreach (string part in target[(q + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
                present.Add(part.Split('=')[0]);
        var extras = parts.Where(p => present.Add(p.Split('=')[0])).ToList();
        if (extras.Count == 0)
            return target;
        return target + (q >= 0 ? "&" : "?") + string.Join("&", extras);
    }

    /// <summary>Lists the static assets a served deployment must have.
    /// Labels are stable (<c>area:relative/path</c>) so the launch-harness
    /// preflight script and this endpoint agree. A deployment missing any
    /// entry must not be served: the browser renders a silent black canvas
    /// (or rejects fallback HTML by MIME type) instead of the app.</summary>
    public static IReadOnlyList<string> StaticPreflight(string? webRoot, string? wpfRoot, string catalogPath)
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(catalogPath) || !File.Exists(catalogPath))
            missing.Add("catalog:apps.json");
        if (string.IsNullOrWhiteSpace(webRoot) || !File.Exists(Path.Combine(webRoot, "index.html")))
            missing.Add("portal:index.html");
        CheckBlazorAssets(webRoot, "portal", missing);
        if (string.IsNullOrWhiteSpace(wpfRoot) || !File.Exists(Path.Combine(wpfRoot, "index.html")))
            missing.Add("wpf:index.html");
        if (string.IsNullOrWhiteSpace(wpfRoot) || !File.Exists(Path.Combine(wpfRoot, "_framework", "blazor.boot.json")))
            missing.Add("wpf:_framework/blazor.boot.json");
        if (string.IsNullOrWhiteSpace(wpfRoot) || !File.Exists(Path.Combine(wpfRoot, "entry.mjs")))
            missing.Add("wpf:entry.mjs");
        if (!string.IsNullOrWhiteSpace(wpfRoot))
            CheckBlazorAssets(wpfRoot, "wpf", missing, checkManifest: false);
        return missing;
    }

    // An index and a boot manifest alone do not make a deployable client.
    // Check the loader and every resource named by the boot manifest, so a
    // source-only wwwroot or a partial framework copy cannot pass readiness.
    private static void CheckBlazorAssets(string? root, string area, List<string> missing,
        bool checkManifest = true)
    {
        string framework = Path.Combine(root ?? "", "_framework");
        if (string.IsNullOrWhiteSpace(root) || !File.Exists(Path.Combine(framework, "blazor.webassembly.js")))
            missing.Add($"{area}:_framework/blazor.webassembly.js");
        string manifest = Path.Combine(framework, "blazor.boot.json");
        if (string.IsNullOrWhiteSpace(root) || !File.Exists(manifest))
        {
            if (checkManifest) missing.Add($"{area}:_framework/blazor.boot.json");
            return;
        }
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllBytes(manifest));
            if (!doc.RootElement.TryGetProperty("resources", out var resources) || resources.ValueKind != JsonValueKind.Object)
                throw new JsonException("Missing resources object.");
            CheckResources(resources, framework, $"{area}:_framework/", missing);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            missing.Add($"{area}:_framework/blazor.boot.json (invalid or unreadable)");
        }
    }

    private static void CheckResources(JsonElement resources, string framework, string prefix, List<string> missing)
    {
        foreach (var group in resources.EnumerateObject())
        {
            if (group.Name == "fingerprinting" || group.Value.ValueKind != JsonValueKind.Object) continue;
            CheckGroup(group.Value, "");
        }

        void CheckGroup(JsonElement group, string directory)
        {
            foreach (var resource in group.EnumerateObject())
            {
                // Satellite assemblies have an extra culture directory level.
                if (resource.Value.ValueKind == JsonValueKind.Object)
                {
                    CheckGroup(resource.Value, directory + resource.Name + "/");
                    continue;
                }
                if (resource.Value.ValueKind != JsonValueKind.String ||
                    resource.Value.GetString() is not { } hash || !hash.StartsWith("sha256-", StringComparison.Ordinal)) continue;
                string relative = directory + resource.Name;
                string full = Path.GetFullPath(Path.Combine(framework, relative));
                if (!full.StartsWith(Path.GetFullPath(framework) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(full))
                    missing.Add(prefix + relative);
            }
        }
    }

    /// <summary>Normalizes the static root once at startup. A trailing
    /// separator would otherwise break the prefix match in
    /// <see cref="TryMapWpfPath"/> (root + separator never matches), so
    /// every staged file would 404. The filesystem root ("/") is preserved.
    /// Returns null for missing input so "no staged root" stays 503.</summary>
    public static string? NormalizeWebRoot(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;
        string trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return trimmed.Length == 0 ? Path.DirectorySeparatorChar.ToString() : trimmed;
    }

    /// <summary>Converted WPF apps whose package bytes are staged under the
    /// static root (<c>apps/{manifest}/app-manifest.json</c>) boot through
    /// the mounted runtime even though the mount-status gate only covers
    /// engine-native demos. Staged manifest bytes are the honest condition:
    /// the loader fetches exactly this file first.</summary>
    public static bool IsConvertedStaged(MountService mounts, AppDescriptor app)
    {
        if (mounts.WpfWebRoot is null)
            return false;
        // Either staged layout boots: manifest-slug dirs (converter output)
        // or catalog-id dirs (legacy alias targets resolve to the id).
        var slugs = new[] { app.Entry.ManifestSlug, app.Id }
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        return slugs.Any(slug => File.Exists(
            Path.Combine(mounts.WpfWebRoot, "apps", slug, "app-manifest.json")));
    }

    /// <summary>First /wpf/ path segments that carry package or
    /// framework-asset bytes rather than client-side navigation routes.
    /// <c>apps/</c> holds per-app package manifests + binaries (the loader
    /// fetches <c>apps/{slug}/app-manifest.json</c> first);
    /// <c>_framework/</c> holds the Blazor boot manifest + runtime payloads.
    /// A missing file under either is an honest 404, never the studio index
    /// fallback.</summary>
    public static bool IsReservedAssetNamespace(string? first) =>
        string.Equals(first, "apps", StringComparison.Ordinal) ||
        string.Equals(first, "_framework", StringComparison.Ordinal);

    /// <summary>Maps a /wpf/ request path into the staged static root.
    /// Returns false when the path escapes the root (traversal).</summary>
    public static bool TryMapWpfPath(string webRoot, string? rest, out string full)
    {
        string root = Path.GetFullPath(webRoot);
        string rel = (rest ?? "").Replace('/', Path.DirectorySeparatorChar);
        full = Path.GetFullPath(Path.Combine(root, rel));
        return full.Equals(root, StringComparison.Ordinal) ||
            full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    /// <summary>Shell-owned WPF runtime page (transitional mount). The staged
    /// entry script only boots the runtime for raw /runtime/ and /demo/
    /// pathnames, which the /wpf/ mount cannot provide; this page runs the
    /// same runtime branch with /wpf/-scoped asset URLs so Blazor routes the
    /// app and the studio iframe stays same-origin. No imported file edits.
    /// </summary>
    public static string RuntimeShimHtml() => """
        <!doctype html>
        <html lang="en">
        <head>
            <meta charset="utf-8" />
            <meta name="viewport" content="width=device-width, initial-scale=1.0" />
            <title>WPF app runtime · unified shell mount</title>
            <base href="/wpf/" />
            <link rel="stylesheet" href="css/app.css" />
            <link rel="stylesheet" href="css/studio.css" />
        </head>
        <body class="runtime-page">
            <div id="app"><div class="runtime-boot"><span class="spinner"></span><h2>Starting WPF runtime</h2><p class="loading-progress-text"></p></div></div>
            <div id="wpf-a11y-live" role="status" aria-live="polite" class="wpf-a11y-live"></div>
            <div id="blazor-error-ui" role="alert">The runtime encountered an error. Return to the library or restart the app.</div>
            <script type="module">
                await import('/wpf/runtime-channel.mjs');
                for (const src of ['/wpf/wasm-host.js', '/wpf/js/app-renderer.js', '/wpf/js/wpf-dialogs.js', '/wpf/js/wpf-a11y.js', '/wpf/js/wpf-dragdrop.js', '/wpf/js/wpf-primitives.js', '/wpf/js/wpf-tabs.js', '/wpf/js/wpf-hittest.js', '/wpf/js/wpf-templates.js', '/wpf/js/wpf-animation.js', '/wpf/js/wpf-text.js', '/wpf/js/wpf-gridrows.js', '/wpf/js/wpf-shell.js', '/wpf/js/wpf-effects.js', '/wpf/js/wpf-gallery.js', '/wpf/js/wpf-oxyplot.js', '/wpf/_framework/blazor.webassembly.js']) {
                    await new Promise((resolve, reject) => {
                        const el = document.createElement('script');
                        el.src = src; el.onload = resolve;
                        el.onerror = () => reject(new Error('Could not load ' + src));
                        document.body.append(el);
                    });
                }
            </script>
        </body>
        </html>
        """;

    /// <summary>Serves the staged WPF studio shell (its own /app/ routing
    /// takes over client-side). 503 when no static root is staged.</summary>
    public static IResult ServeWpfStudio(MountService mounts)
    {
        if (mounts.WpfWebRoot is null)
            return Results.Json(new
            {
                error = "mount-unavailable",
                detail = "WPF static root not staged; mount unavailable.",
            }, statusCode: 503);
        string index = Path.Combine(Path.GetFullPath(mounts.WpfWebRoot), "index.html");
        return Results.Content(MountService.RewriteBaseHref(
            File.ReadAllText(index), MountService.WpfPrefix), "text/html");
    }

    public static string MimeFor(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".html" => "text/html",
            ".js" or ".mjs" => "text/javascript",
            ".css" => "text/css",
            ".json" => "application/json",
            ".wasm" => "application/wasm",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".ico" => "image/x-icon",
            ".woff" => "font/woff",
            ".woff2" => "font/woff2",
            ".ttf" => "font/ttf",
            ".dat" or ".bin" or ".dll" => "application/octet-stream",
            _ => "application/octet-stream",
        };

    public static void ApplyStaticAssetHeaders(HttpContext context, string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext is ".wasm" or ".dll" or ".dat" or ".bin" or ".woff" or ".woff2" ||
            path.Contains("_framework", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("apps", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.Headers["Cache-Control"] = "public, max-age=31536000, immutable";
        }
    }

    public static bool TryGetPrecompressedFile(HttpContext context, string fullPath, out string compressedPath, out string encoding)
    {
        string? acceptEncoding = context.Request.Headers.AcceptEncoding;
        if (!string.IsNullOrEmpty(acceptEncoding))
        {
            if (acceptEncoding.Contains("br", StringComparison.OrdinalIgnoreCase))
            {
                string brPath = fullPath + ".br";
                if (File.Exists(brPath))
                {
                    compressedPath = brPath;
                    encoding = "br";
                    return true;
                }
            }
            if (acceptEncoding.Contains("gzip", StringComparison.OrdinalIgnoreCase))
            {
                string gzPath = fullPath + ".gz";
                if (File.Exists(gzPath))
                {
                    compressedPath = gzPath;
                    encoding = "gzip";
                    return true;
                }
            }
        }
        compressedPath = string.Empty;
        encoding = string.Empty;
        return false;
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
    string? WebRootPath = null,
    string? WinFormsBackendUrl = null,
    string? WpfWebRootPath = null,
    HttpMessageHandler? BackendHandler = null);

public sealed record SessionRequest(string? AppId, string? SessionId);

public sealed record MarkFixedRequest(string Signature, string? Note);
