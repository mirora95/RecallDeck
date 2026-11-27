using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using RecallDeck.UI;
using RecallDeck.Core;
using RecallDeck.Core.Storage;
using RecallDeck.Web.Storage;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped<IStudyStore, IndexedDbStudyStore>();
builder.Services.AddScoped<DeckService>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<ReviewScheduler>();
builder.Services.AddScoped<CardService>();

await builder.Build().RunAsync();
