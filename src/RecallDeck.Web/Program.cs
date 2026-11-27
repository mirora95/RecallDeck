using RecallDeck.Core.Backup;
using RecallDeck.UI.Backup;
using RecallDeck.Web.Backup;
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
builder.Services.AddScoped<RecallDeck.UI.AppNavigation>();
builder.Services.AddScoped<RecallDeck.UI.AppLifecycle>();
builder.Services.AddScoped<DeckService>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<ReviewScheduler>();
builder.Services.AddScoped<CardService>();
builder.Services.AddScoped<ReviewQueueService>();
builder.Services.AddScoped<StudyOverviewService>();
builder.Services.AddScoped<StudyBackupService>();
builder.Services.AddScoped<IBackupFiles, BrowserBackupFiles>();

await builder.Build().RunAsync();
