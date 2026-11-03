using System.Net;
using System.Text;
using DesktopWasm.Host.Server;
using Xunit;

namespace DesktopWasm.Shell.Tests;

public sealed class LaunchReadinessTests
{
    [Theory]
    [InlineData(200, "{\"entry\":\"Demo\",\"files\":[{\"name\":\"Demo.dll\"}]}", true)]
    [InlineData(404, "missing app", false)]
    [InlineData(200, "<!doctype html><html>fallback</html>", false)]
    [InlineData(200, "{\"entry\":\"Demo\",\"files\":[]}", false)]
    public async Task On_demand_launch_uses_real_host_manifest_not_stale_catalog_or_menu(
        int status, string body, bool expected)
    {
        var app = ShellHost.FixtureDescriptors().Single(a => a.Id == "test-smoke-form");
        // The registry deliberately still says MissingInput. A real manifest
        // can enable launch; a menu claiming available cannot enable missing files.
        using var http = new HttpClient(new Backend(status, body)) { BaseAddress = new Uri("http://backend.invalid") };
        var result = await new MountService(http, null).WinFormsStatusAsync(app, "readiness-test");
        Assert.Equal(expected, result.CanBoot);
        Assert.Equal(expected, result.MountedUrl is not null);
        if (expected) Assert.DoesNotContain("missingInput", result.Detail);
    }

    private sealed class Backend(int status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            bool preview = request.RequestUri!.AbsolutePath == "/api/preview-config";
            return Task.FromResult(new HttpResponseMessage(preview ? HttpStatusCode.OK : (HttpStatusCode)status)
            {
                Content = new StringContent(preview
                    ? """{"apps":[{"slug":"test-smoke-form","available":true,"appFolder":"fixture-app"}]}"""
                    : body, Encoding.UTF8, "application/json")
            });
        }
    }
}
