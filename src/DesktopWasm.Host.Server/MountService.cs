using System.Net;
using System.Text;
using System.Text.Json;
using DesktopWasm.AppModel;

namespace DesktopWasm.Host.Server;

/// <summary>
/// Transitional pure-app mount behind the shell adapters (MERGE-SHELL-2).
/// The imported hosts run unmodified: the WinForms host server is proxied
/// (its absolute /api/* and /_framework/* paths are re-rooted under
/// /winforms/ via base-href rewrite), and the WPF host's published static
/// root is served under /wpf/. Mount truth comes from the backends'
/// themselves (preview-config availability, staged static root), surfaced
/// alongside — never instead of — registry availability. Anything the mount
/// cannot boot stays honestly non-bootable; this is not mixed hosting.
/// </summary>
public sealed class MountService(
    HttpClient winFormsBackend,
    string? wpfWebRoot)
{
    public const string WinFormsPrefix = "/winforms";
    public const string WpfPrefix = "/wpf";

    public string? WpfWebRoot { get; } = wpfWebRoot;

    /// <summary>Isolated app origins ({slug}.localhost) belong entirely to
    /// the mounted WinForms backend; the subdomain IS its origin.</summary>
    public static bool IsIsolatedOrigin(string host) =>
        host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase);

    public static string RewriteBaseHref(string html, string prefix) =>
        html.Replace("<base href=\"/\" />", $"<base href=\"{prefix}/\" />", StringComparison.Ordinal)
            .Replace("<base href=\"/\"/>", $"<base href=\"{prefix}/\"/>", StringComparison.Ordinal);

    /// <summary>Shell-owned mount-compat bootstrap for the WinForms backend
    /// library page. The backend app menu reads <c>location.pathname</c> with
    /// backend-native routes (<c>/app/{slug}</c>) and pushes backend-absolute
    /// URLs; under the <c>/winforms/</c> mount that defaults a deep-link to the
    /// smoke scene and lets later navigations escape the mount. This shim runs
    /// before the backend modules: it normalizes a prefixed deep-link to the
    /// backend-native route (preserving query + hash so app selection
    /// survives) and re-roots later backend-absolute navigations under the
    /// mount prefix. Resource bases are untouched: the rewritten base href
    /// still scopes relative URLs. The durable fix — backend routing that
    /// knows its mount prefix natively — belongs to the runtime/importer lane;
    /// this shim is the shell-owned equivalent until then.</summary>
    public static string MountCompatShim(string prefix) => $$"""
        <script data-dw-mount="{{prefix}}">(function(){var prefix="{{prefix}}";
        function known(p){return p==="/app"||p.indexOf("/app/")===0||p==="/demo"||p.indexOf("/demo/")===0||p==="/dashboard"||p==="/fxlab"||p==="/runtime.html"||p==="/errors"||p.indexOf("/errors/")===0;}
        function reroot(u){try{var x=new URL(u,location.href);if(x.origin!==location.origin)return u;if(known(x.pathname)&&x.pathname!==prefix&&x.pathname.indexOf(prefix+"/")!==0){x.pathname=prefix+x.pathname;return x.href;}return u;}catch(e){return u;}
        }
        var origPush=history.pushState.bind(history),origReplace=history.replaceState.bind(history);
        history.pushState=function(s,t,u){return origPush(s,t,u==null?u:reroot(u));};
        history.replaceState=function(s,t,u){return origReplace(s,t,u==null?u:reroot(u));};
        var path=location.pathname;
        if(path===prefix||path.indexOf(prefix+"/")===0){var native=path.slice(prefix.length)||"/";
        if(native!=="/"&&known(native)){origReplace(null,"",native+location.search+location.hash);}
        }
        })();</script>
        """;

    /// <summary>Inserts <see cref="MountCompatShim"/> as the first head
    /// script so it executes before the backend modules. Idempotent: pages
    /// that already carry the marker pass through unchanged.</summary>
    public static string InjectMountCompat(string html, string prefix)
    {
        if (html.Contains("data-dw-mount=", StringComparison.Ordinal))
            return html;
        string shim = MountCompatShim(prefix);
        int head = html.IndexOf("<head", StringComparison.OrdinalIgnoreCase);
        if (head >= 0)
        {
            int end = html.IndexOf('>', head);
            if (end >= 0)
                return html[..(end + 1)] + shim + html[(end + 1)..];
        }
        return shim + html;
    }

    /// <summary>Proxies one request to the WinForms backend. On the main
    /// host the base href of HTML responses is rewritten so absolute
    /// backend paths re-root under /winforms/. On isolated origins
    /// (*.localhost) the page keeps the backend's native "/" base: that
    /// origin IS the backend origin, and its absolute /app/* URLs must
    /// stay contained in the base or backend boot crashes ("The URI ...
    /// is not contained by the base URI").</summary>
    public async Task<IResult> ProxyWinFormsAsync(HttpContext context, string rest)
    {
        HttpResponseMessage upstream;
        try
        {
            string target = "/" + (rest ?? "");
            if (context.Request.QueryString.HasValue)
                target += context.Request.QueryString.Value;
            using var request = new HttpRequestMessage(
                new HttpMethod(context.Request.Method), new Uri(target, UriKind.Relative));
            if (context.Request.ContentLength > 0)
            {
                request.Content = new StreamContent(context.Request.Body);
                if (context.Request.ContentType is not null)
                    request.Content.Headers.TryAddWithoutValidation("Content-Type", context.Request.ContentType);
            }
            // Conditional headers ride through: without them the backend
            // can never answer 304 and every warm boot re-downloads full
            // assembly/framework bytes through the gateway.
            foreach (string conditional in new[] { "If-None-Match", "If-Modified-Since" })
                if (context.Request.Headers.TryGetValue(conditional, out var values))
                    request.Headers.TryAddWithoutValidation(conditional, values.ToArray());
            upstream = await winFormsBackend.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, context.RequestAborted);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return Results.Json(new
            {
                error = "mount-unavailable",
                detail = $"WinForms host backend unreachable: {ex.GetBaseException().Message}",
            }, statusCode: 503);
        }
        using (upstream)
        {
            if (upstream.StatusCode == HttpStatusCode.NotModified)
            {
                context.Response.StatusCode = (int)HttpStatusCode.NotModified;
                if (TryGetUpstreamValues(upstream, "ETag", out string[]? noneMatch))
                    context.Response.Headers["ETag"] = noneMatch;
                return Results.StatusCode((int)HttpStatusCode.NotModified);
            }
            byte[] bytes = await upstream.Content.ReadAsByteArrayAsync(context.RequestAborted);
            string? media = upstream.Content.Headers.ContentType?.MediaType;
            if (media == "text/html" && !IsIsolatedOrigin(context.Request.Host.Host))
            {
                string rewritten = RewriteBaseHref(Encoding.UTF8.GetString(bytes), WinFormsPrefix);
                rewritten = InjectMountCompat(rewritten, WinFormsPrefix);
                bytes = Encoding.UTF8.GetBytes(rewritten);
            }
            context.Response.StatusCode = (int)upstream.StatusCode;
            // Integrity + cache headers are load-bearing: the browser's
            // synchronous demand hook rejects hash-less assembly bytes, and
            // immutable versions are cached forever. Forward them verbatim
            // instead of rebuilding a bare 200.
            if (upstream.Headers.TryGetValues("X-WfWasm-Sha256", out var sha) ||
                upstream.Content.Headers.TryGetValues("X-WfWasm-Sha256", out sha))
                context.Response.Headers["X-WfWasm-Sha256"] = sha.ToArray();
            if (upstream.Headers.TryGetValues("Cache-Control", out var cache) ||
                upstream.Content.Headers.TryGetValues("Cache-Control", out cache))
                context.Response.Headers["Cache-Control"] = cache.ToArray();
            // Validators ride through: without the ETag no gateway client
            // can revalidate, so every warm boot re-downloads full bytes.
            if (TryGetUpstreamValues(upstream, "ETag", out string[]? etag))
                context.Response.Headers["ETag"] = etag;
            if (upstream.Content.Headers.TryGetValues("Last-Modified", out var modified))
                context.Response.Headers["Last-Modified"] = modified.ToArray();
            return Results.Bytes(bytes, media ?? "application/octet-stream");
        }
    }

    private static bool TryGetUpstreamValues(HttpResponseMessage upstream, string name, out string[]? values)
    {
        if (upstream.Headers.TryGetValues(name, out var h) ||
            upstream.Content.Headers.TryGetValues(name, out h))
        {
            values = h.ToArray();
            return true;
        }
        values = null;
        return false;
    }

    public record MountStatus(bool CanBoot, string Detail, string? MountedUrl);

    /// <summary>WinForms mount truth: the backend's own preview-config
    /// availability for the scene, not the shell's guess.</summary>
    public async Task<MountStatus> WinFormsStatusAsync(AppDescriptor app, string sessionId)
    {
        if (!app.Capabilities.HasFlag(AppCapability.WinForms))
            return new MountStatus(false, $"App '{app.Id}' does not declare the winforms capability.", null);
        JsonDocument config;
        try
        {
            using var response = await winFormsBackend.GetAsync("/api/preview-config");
            response.EnsureSuccessStatusCode();
            config = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        }
        catch (Exception ex)
        {
            return new MountStatus(false,
                $"WinForms mount unreachable: {ex.GetBaseException().Message}", null);
        }
        using (config)
        {
            foreach (var entry in config.RootElement.GetProperty("apps").EnumerateArray())
            {
                string? slug = entry.TryGetProperty("slug", out var s) ? s.GetString() : null;
                if (!string.Equals(slug, app.Id, StringComparison.OrdinalIgnoreCase))
                    continue;
                bool available = !entry.TryGetProperty("available", out var a) || a.GetBoolean();
                string note = entry.TryGetProperty("setupNote", out var n) ? n.GetString() ?? "" : "";
                if (!available)
                    return new MountStatus(false,
                        $"WinForms scene '{slug}' reports unavailable: {note} Registry gap stands: {app.AvailabilityNote}", null);
                // A menu entry is not evidence that its source files exist.
                // Built-in scenes need only the host; on-demand scenes must
                // actually produce their entry manifest before we offer Open.
                if (entry.TryGetProperty("appFolder", out var folder) && folder.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(folder.GetString()))
                {
                    try
                    {
                        using var response = await winFormsBackend.GetAsync(
                            "/api/appmanifest?app=" + Uri.EscapeDataString(folder.GetString()!));
                        if (!response.IsSuccessStatusCode)
                            return new MountStatus(false, "The app's files are not ready in the running Windows Forms host.", null);
                        using var manifest = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                        if (!manifest.RootElement.TryGetProperty("entry", out var manifestEntry) ||
                            manifestEntry.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(manifestEntry.GetString()) ||
                            !manifest.RootElement.TryGetProperty("files", out var files) || files.ValueKind != JsonValueKind.Array || files.GetArrayLength() == 0)
                            return new MountStatus(false, "The Windows Forms host did not provide a loadable app manifest.", null);
                    }
                    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
                    {
                        return new MountStatus(false, "The app's files could not be checked. Try opening it again.", null);
                    }
                }
                return new MountStatus(true,
                    "The running Windows Forms host can launch this app.",
                    $"{WinFormsPrefix}/app/{slug}?session={Uri.EscapeDataString(sessionId)}");
            }
        }
        return new MountStatus(false, $"No WinForms scene '{app.Id}' in the mounted host.", null);
    }

    /// <summary>WPF mount truth: engine-native demos are compiled into the
    /// staged static root; converted externals still need packaging.</summary>
    public MountStatus WpfStatus(AppDescriptor app, string sessionId)
    {
        if (!app.Capabilities.HasFlag(AppCapability.Wpf))
            return new MountStatus(false, $"App '{app.Id}' does not declare the wpf capability.", null);
        if (WpfWebRoot is null || !File.Exists(Path.Combine(WpfWebRoot, "index.html")))
            return new MountStatus(false, "WPF static root not staged; mount unavailable.", null);
        if (app.Entry.Kind == "native-demo")
            return new MountStatus(true,
                $"Mounted transitional host serving demo '{app.Id}'. Registry: {CatalogSource.StatusOf(app)} — {app.AvailabilityNote}",
                $"/app/{app.Id}?session={Uri.EscapeDataString(sessionId)}");
        return new MountStatus(false,
            $"WPF converted app '{app.Id}' needs its staged package: {app.AvailabilityNote}", null);
    }
}
