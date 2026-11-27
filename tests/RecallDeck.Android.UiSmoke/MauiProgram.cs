using RecallDeck.Core.Backup;
using RecallDeck.UI.Backup;
using RecallDeck.Mobile.Backup;
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
        builder.Services.AddScoped<RecallDeck.UI.AppNavigation>();
        builder.Services.AddScoped<RecallDeck.UI.AppLifecycle>();
        builder.Services.AddScoped<DeckService>();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddScoped<ReviewScheduler>();
        builder.Services.AddScoped<CardService>();
        builder.Services.AddScoped<ReviewQueueService>();
        builder.Services.AddScoped<StudyBackupService>();
        builder.Services.AddScoped<IBackupFiles, AndroidBackupFiles>();
        return builder.Build();
    }
}
