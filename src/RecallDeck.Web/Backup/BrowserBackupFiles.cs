using Microsoft.JSInterop;
using RecallDeck.Core.Backup;
using RecallDeck.UI.Backup;

namespace RecallDeck.Web.Backup;

public sealed class BrowserBackupFiles(IJSRuntime js) : IBackupFiles
{
    public async Task<BackupFile?> OpenAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using var module = await js.InvokeAsync<IJSObjectReference>("import", cancellationToken, "./backup-files.js");
        return await module.InvokeAsync<BackupFile?>("openBackup", cancellationToken, StudyBackupCodec.MaximumBytes);
    }

    public async Task<BackupSaveResult> SaveAsync(string name, byte[] content, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using var module = await js.InvokeAsync<IJSObjectReference>("import", cancellationToken, "./backup-files.js");
        await module.InvokeVoidAsync("saveBackup", cancellationToken, name, content);
        return BackupSaveResult.DownloadRequested;
    }
}
