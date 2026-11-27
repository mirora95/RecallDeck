using RecallDeck.Core;
using RecallDeck.Core.Storage;
using RecallDeck.Storage.Sqlite;
using SQLite;

namespace RecallDeck.Storage.Checks;

// Test host only: the shipping app never registers this wrapper.
public sealed class UiFailureStudyStore(string databasePath) : IStudyStore
{
    public const string FailureMarker = "[RecallDeck synthetic failed save] ";
    public const string ReviewFailureMarker = "[RecallDeck synthetic failed review] ";
    private int reviewFailureUsed;
    private readonly SqliteStudyStore inner = new(databasePath);

    private int failureUsed;

    public async Task CreateCardAsync(Card card, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(card);
        cancellationToken.ThrowIfCancellationRequested();
        if (!card.Front.StartsWith(FailureMarker, StringComparison.Ordinal) || Interlocked.CompareExchange(ref failureUsed, 1, 0) != 0)
        {
            await inner.CreateCardAsync(card, cancellationToken);
            return;
        }

        // Initialize with the real adapter; only this validated synthetic card ID can fail.
        await inner.ReadSnapshotAsync(cancellationToken);
        var trigger = "UiSmokeFail_" + card.Id.ToString("N");
        using var db = new SQLiteConnection(databasePath);
        db.Execute($"CREATE TRIGGER {trigger} BEFORE INSERT ON Cards WHEN NEW.Id = '{card.Id:D}' BEGIN SELECT RAISE(ABORT, 'synthetic UI failure'); END");
        try
        {
            await inner.CreateCardAsync(card, cancellationToken);
        }
        finally
        {
            db.Execute($"DROP TRIGGER {trigger}");
        }
    }

    public Task<StudySnapshot> ReadSnapshotAsync(CancellationToken cancellationToken = default) => inner.ReadSnapshotAsync(cancellationToken);
    public Task CreateDeckAsync(Deck deck, CancellationToken cancellationToken = default) => inner.CreateDeckAsync(deck, cancellationToken);
    public Task UpdateDeckAsync(Deck deck, CancellationToken cancellationToken = default) => inner.UpdateDeckAsync(deck, cancellationToken);
    public Task DeleteDeckAsync(Guid id, CancellationToken cancellationToken = default) => inner.DeleteDeckAsync(id, cancellationToken);
    public Task UpdateCardAsync(Card card, CancellationToken cancellationToken = default) => inner.UpdateCardAsync(card, cancellationToken);
    public Task DeleteCardAsync(Guid id, CancellationToken cancellationToken = default) => inner.DeleteCardAsync(id, cancellationToken);
    public async Task ApplyReviewAsync(ReviewEvent review, ReviewSchedule schedule, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(review);
        cancellationToken.ThrowIfCancellationRequested();
        var snapshot = await inner.ReadSnapshotAsync(cancellationToken);
        var card = snapshot.Cards.SingleOrDefault(card => card.Id == review.CardId);
        if (card is null || !card.Front.StartsWith(ReviewFailureMarker, StringComparison.Ordinal) ||
            Interlocked.CompareExchange(ref reviewFailureUsed, 1, 0) != 0)
        {
            await inner.ApplyReviewAsync(review, schedule, cancellationToken);
            return;
        }

        // Abort history insertion after the schedule update, for this exact owned card only.
        var trigger = "UiSmokeReviewFail_" + card.Id.ToString("N");
        using var db = new SQLiteConnection(databasePath);
        db.Execute($"CREATE TRIGGER {trigger} BEFORE INSERT ON Reviews WHEN NEW.CardId = '{card.Id:D}' BEGIN SELECT RAISE(ABORT, 'synthetic UI review failure'); END");
        try
        {
            await inner.ApplyReviewAsync(review, schedule, cancellationToken);
        }
        finally
        {
            db.Execute($"DROP TRIGGER {trigger}");
        }
    }
}
