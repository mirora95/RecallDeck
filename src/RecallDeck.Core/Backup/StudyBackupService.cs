using RecallDeck.Core.Storage;

namespace RecallDeck.Core.Backup;

/// <summary>A detached, validated candidate. Discarding it cancels without accessing storage.</summary>
public sealed class StudyRestorePreview
{
    internal StudyRestorePreview(StudySnapshot snapshot) => Snapshot = snapshot;
    internal StudySnapshot Snapshot { get; }
    public int DeckCount => Snapshot.Decks.Count;
    public int CardCount => Snapshot.Cards.Count;
    public int ReviewCount => Snapshot.Reviews.Count;
}

public sealed class StudyBackupService
{
    private readonly IStudyStore store;

    public StudyBackupService(IStudyStore store) => this.store = store ?? throw new ArgumentNullException(nameof(store));

    public StudyRestorePreview Preview(ReadOnlyMemory<byte> utf8Json)
        => new(StudyBackupCodec.ValidateImport(utf8Json));

    public async Task<byte[]> ExportCurrentAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var snapshot = await store.ReadSnapshotAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return StudyBackupCodec.Export(snapshot);
    }

    public Task ConfirmRestoreAsync(StudyRestorePreview preview, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preview);
        cancellationToken.ThrowIfCancellationRequested();
        return store.ReplaceAllAsync(preview.Snapshot, cancellationToken);
    }
}
