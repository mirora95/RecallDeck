using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using RecallDeck.Core.Storage;
using RecallDeck.Web.Storage;
using RecallDeck.Web.Interop;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.Services.AddScoped<IStudyStore, IndexedDbStudyStore>();
await builder.Build().RunAsync();
