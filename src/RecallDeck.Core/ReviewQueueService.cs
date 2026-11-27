using RecallDeck.Core.Storage;

namespace RecallDeck.Core;

public sealed class ReviewQueueService
{
    private readonly IStudyStore store;
    private readonly TimeProvider clock;

    public ReviewQueueService(IStudyStore store, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(clock);

        this.store = store;
        this.clock = clock;
    }

    /// <summary>
    /// Captures a read-only queue using one UTC instant after reading storage.
    /// Later storage changes do not alter the captured cards or their order.
    /// </summary>
    public async Task<IReadOnlyList<Card>> CaptureAsync(Guid? deckId = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var snapshot = await store.ReadSnapshotAsync(cancellationToken);
        if (deckId.HasValue && !snapshot.Decks.Any(deck => deck.Id == deckId.Value))
            throw new KeyNotFoundException("The deck was not found.");

        var now = clock.GetUtcNow();
        return Array.AsReadOnly(snapshot.Cards
            .Where(card => card.Schedule.DueAt <= now && (!deckId.HasValue || card.DeckId == deckId.Value))
            .OrderBy(card => card.Schedule.DueAt)
            .ThenBy(card => card.Id)
            .ToArray());
    }
}
