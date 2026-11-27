namespace RecallDeck.UI.Backup;

/// <summary>Only user-selected files cross the UI/host boundary. No platform paths escape it.</summary>
public interface IBackupFiles
{
    Task<BackupFile?> OpenAsync(CancellationToken cancellationToken = default);
    Task<BackupSaveResult> SaveAsync(string name, byte[] content, CancellationToken cancellationToken = default);
}

public sealed record BackupFile(string Name, byte[] Content);
public enum BackupSaveResult { Canceled, Saved, DownloadRequested }
