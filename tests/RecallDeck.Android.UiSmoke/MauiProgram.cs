using RecallDeck.Core;
using RecallDeck.Core.Storage;
using RecallDeck.Storage.Checks;

namespace RecallDeck.Android.UiSmoke;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder().UseMauiApp<App>();
        builder.Services.AddMauiBlazorWebView();
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Services.AddSingleton<IStudyStore>(_ => new UiFailureStudyStore(Path.Combine(FileSystem.AppDataDirectory, "ui-smoke.db3")));
        builder.Services.AddScoped<DeckService>();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddScoped<ReviewScheduler>();
        builder.Services.AddScoped<CardService>();
        return builder.Build();
    }
}
