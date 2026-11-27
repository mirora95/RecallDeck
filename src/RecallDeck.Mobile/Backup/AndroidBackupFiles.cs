using Android.Content;
using RecallDeck.UI.Backup;

namespace RecallDeck.Mobile.Backup;

public sealed class AndroidBackupFiles : IBackupFiles
{
    public async Task<BackupFile?> OpenAsync(CancellationToken cancellationToken = default)
    {
        using var intent = new Intent(Intent.ActionOpenDocument);
        intent.AddCategory(Intent.CategoryOpenable);
        intent.SetType("application/json");
        var uri = await SelectAsync(intent, cancellationToken);
        if (uri is null) return null;
        cancellationToken.ThrowIfCancellationRequested();
        using var source = global::Android.App.Application.Context.ContentResolver!.OpenInputStream(uri)
            ?? throw new IOException("The selected document could not be opened.");
        var content = await BackupFileReader.ReadAsync(source, cancellationToken);
        return new BackupFile("Selected JSON backup", content);
    }

    public async Task<BackupSaveResult> SaveAsync(string name, byte[] content, CancellationToken cancellationToken = default)
    {
        using var intent = new Intent(Intent.ActionCreateDocument);
        intent.AddCategory(Intent.CategoryOpenable);
        intent.SetType("application/json");
        intent.PutExtra(Intent.ExtraTitle, name);
        var uri = await SelectAsync(intent, cancellationToken);
        if (uri is null) return BackupSaveResult.Canceled;
        cancellationToken.ThrowIfCancellationRequested();
        using (var destination = global::Android.App.Application.Context.ContentResolver!.OpenOutputStream(uri, "wt")
            ?? throw new IOException("The selected document could not be written."))
        {
            await destination.WriteAsync(content, cancellationToken);
            await destination.FlushAsync(cancellationToken);
        }
        return BackupSaveResult.Saved;
    }

    private static Task<global::Android.Net.Uri?> SelectAsync(Intent intent, CancellationToken token)
        => MainThread.InvokeOnMainThreadAsync(() =>
            (Platform.CurrentActivity as BackupDocumentActivity
                ?? throw new IOException("The document picker is not available."))
            .SelectDocumentAsync(intent, token));
}
