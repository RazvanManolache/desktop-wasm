using DesktopWasm.BrowserHost;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");
builder.Services.AddScoped<DemoState>();
builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

// dw-runtime bridge region: serve the runtime-provided WindowsFormsIntegration
// reference from the real interop bridge (packaged assemblies still resolve
// through ApplicationPackageLoader's own handler).
InteropBridgeRegistration.EnsureRegistered();

await builder.Build().RunAsync();
