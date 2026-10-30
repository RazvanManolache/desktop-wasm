using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using DesktopWasm.Web;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<ShellRoot>("#app");
builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddScoped<WinFormsRuntimeAdapter>();
builder.Services.AddScoped<IRuntimeAdapter>(sp => sp.GetRequiredService<WinFormsRuntimeAdapter>());
builder.Services.AddScoped<WpfRuntimeAdapter>();
builder.Services.AddScoped<MixedSession>();
await builder.Build().RunAsync();
