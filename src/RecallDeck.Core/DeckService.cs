using RecallDeck.Core.Storage;

namespace RecallDeck.Core;

public sealed class DeckService
{
    private readonly IStudyStore store;

    public DeckService(IStudyStore store)
    {
        ArgumentNullException.ThrowIfNull(store);

        this.store = store;
    }

    public async Task<Deck> CreateAsync(string title, string? description = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var deck = new Deck(Guid.NewGuid(), title, description);
        await store.CreateDeckAsync(deck, cancellationToken);
        return deck;
    }

    public async Task<Deck> RenameAsync(Guid id, string title, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var snapshot = await store.ReadSnapshotAsync(cancellationToken);
        var current = snapshot.Decks.SingleOrDefault(deck => deck.Id == id)
            ?? throw new KeyNotFoundException("The deck was not found.");
        var renamed = new Deck(current.Id, title, current.Description);
        await store.UpdateDeckAsync(renamed, cancellationToken);
        return renamed;
    }

    public async Task<IReadOnlyList<Deck>> ListAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = await store.ReadSnapshotAsync(cancellationToken);

        return Array.AsReadOnly(snapshot.Decks
            .OrderBy(deck => deck.Title, StringComparer.Ordinal)
            .ThenBy(deck => deck.Id)
            .ToArray());
    }
}
