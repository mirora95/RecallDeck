using RecallDeck.Core.Backup;
using RecallDeck.UI.Backup;
using RecallDeck.Mobile.Backup;
using Microsoft.Extensions.Logging;
using RecallDeck.Core;
using RecallDeck.Core.Storage;
using RecallDeck.Storage.Sqlite;

namespace RecallDeck.Mobile;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();

        builder.Services.AddMauiBlazorWebView();

        builder.Services.AddSingleton<IStudyStore>(_ => new SqliteStudyStore(Path.Combine(FileSystem.AppDataDirectory, "study.db3")));

        builder.Services.AddScoped<DeckService>();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddScoped<ReviewScheduler>();
        builder.Services.AddScoped<CardService>();
        builder.Services.AddScoped<ReviewQueueService>();
        builder.Services.AddScoped<StudyBackupService>();
        builder.Services.AddScoped<IBackupFiles, AndroidBackupFiles>();

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
