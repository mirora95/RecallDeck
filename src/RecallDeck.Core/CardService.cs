using RecallDeck.Core.Storage;

namespace RecallDeck.Core;

public sealed class CardService
{
    private readonly IStudyStore store;
    private readonly ReviewScheduler scheduler;

    public CardService(IStudyStore store, ReviewScheduler scheduler)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(scheduler);

        this.store = store;
        this.scheduler = scheduler;
    }

    public async Task<Card> CreateAsync(Guid deckId, string front, string back, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var card = scheduler.CreateCard(deckId, front, back);
        await store.CreateCardAsync(card, cancellationToken);
        return card;
    }

    public async Task<Card> EditAsync(Guid id, string front, string back, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var snapshot = await store.ReadSnapshotAsync(cancellationToken);
        var current = snapshot.Cards.SingleOrDefault(card => card.Id == id)
            ?? throw new KeyNotFoundException("The card was not found.");
        var edited = new Card(current.Id, current.DeckId, front, back, current.Schedule);
        await store.UpdateCardAsync(edited, cancellationToken);
        return edited;
    }

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return store.DeleteCardAsync(id, cancellationToken);
    }
}
