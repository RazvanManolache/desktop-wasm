using System.Reflection;
using WfWasm.CanvasHost;

public sealed class AppLaunchErrorPresentationTests
{
    [Fact]
    public void FormatUserFacingLaunchError_HttpRequestException_Produces_Clean_Network_Message()
    {
        var ex = new HttpRequestException("Response status code does not indicate success: 404 (Not Found).");
        string formatted = AppAssemblyLoader.FormatUserFacingLaunchError("my-demo", ex);

        Assert.Equal("The application assets for 'my-demo' could not be downloaded from the server.", formatted);
        Assert.DoesNotContain("HttpRequestException", formatted);
        Assert.DoesNotContain("404", formatted);
    }

    [Fact]
    public void FormatUserFacingLaunchError_IntegrityFailure_Produces_Clean_Integrity_Message()
    {
        var ex = new InvalidDataException("Assembly integrity check failed: MyAssembly.dll");
        string formatted = AppAssemblyLoader.FormatUserFacingLaunchError("my-demo", ex);

        Assert.Equal("An integrity check failed for one of the application files in 'my-demo'.", formatted);
        Assert.DoesNotContain("InvalidDataException", formatted);
        Assert.DoesNotContain("MyAssembly.dll", formatted);
    }

    [Fact]
    public void FormatUserFacingLaunchError_MissingDependency_Produces_Clean_Dependency_Message()
    {
        var ex = new FileNotFoundException("Could not load file or assembly 'MissingLib, Version=1.0.0.0'.");
        string formatted = AppAssemblyLoader.FormatUserFacingLaunchError("my-demo", ex);

        Assert.Equal("The application 'my-demo' is missing a required dependency.", formatted);
        Assert.DoesNotContain("FileNotFoundException", formatted);
        Assert.DoesNotContain("Version=", formatted);
    }

    [Fact]
    public void FormatUserFacingLaunchError_PlatformNotSupported_Produces_WindowsOnly_Message()
    {
        var ex = new PlatformNotSupportedException("System.Diagnostics.Process is not supported on this platform.");
        string formatted = AppAssemblyLoader.FormatUserFacingLaunchError("my-demo", ex);

        Assert.Equal("This app needs Windows-only features that aren't available in the browser.", formatted);
        Assert.DoesNotContain("PlatformNotSupportedException", formatted);
    }

    [Fact]
    public void FormatUserFacingLaunchError_MissingManifest_Produces_Clean_Manifest_Message()
    {
        var ex = new InvalidOperationException("No manifest for app 'unknown-app'.");
        string formatted = AppAssemblyLoader.FormatUserFacingLaunchError("unknown-app", ex);

        Assert.Equal("The application manifest for 'unknown-app' is not available.", formatted);
    }

    [Fact]
    public void AppAssemblyLoader_Tracks_And_Clears_Resolution_Failures()
    {
        string testApp = "test-error-app-" + Guid.NewGuid().ToString("N");
        Assert.Null(AppAssemblyLoader.GetAppLoadError(testApp));
        Assert.Empty(AppAssemblyLoader.GetResolutionFailures(testApp));

        // Simulate resolution error recording
        AppAssemblyLoader.RecordResolutionFailureForTest(testApp, "MissingComponent.dll", new FileNotFoundException("component missing"));

        Assert.Contains("MissingComponent.dll", AppAssemblyLoader.GetResolutionFailures(testApp));
        string? err = AppAssemblyLoader.GetAppLoadError(testApp);
        Assert.NotNull(err);
        Assert.Contains(testApp, err);
        Assert.Contains("MissingComponent.dll", err);

        // Clearing errors for retry
        AppAssemblyLoader.ClearAppErrors(testApp);
        Assert.Null(AppAssemblyLoader.GetAppLoadError(testApp));
        Assert.Empty(AppAssemblyLoader.GetResolutionFailures(testApp));
    }

    [Fact]
    public void Resolve_Throwing_Load_Or_Resolution_Fails_Closed_Without_Crashing()
    {
        // When resolving an unknown or failing assembly headless, it must return null
        // rather than allowing exceptions to escape and abort the runtime.
        var m = typeof(AppAssemblyLoader).GetMethod("ResolveRequestedAssembly",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(m);

        var result = m.Invoke(null, [null, new ResolveEventArgs("NonExistent.Failing.Assembly, Version=1.0.0.0")]);
        Assert.Null(result);
    }
}
