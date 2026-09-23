using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using HmlEnvDb;

// No Razor component tree: the page markup is the same static index.html the
// dashboard has always used, and this C# app drives it through JS interop —
// a direct port of the original app.js, function for function.
var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
var host = builder.Build();

Dashboard.Init(
    host.Services.GetRequiredService<IJSRuntime>(),
    host.Services.GetRequiredService<HttpClient>());
_ = Dashboard.BootAsync();

await host.RunAsync();
