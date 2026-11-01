using RecallDeck.Core.Storage;

namespace RecallDeck.Core.Tests.Storage;

internal sealed class InMemoryStudyStore : IStudyStore
{
    private readonly object gate = new();
    private StudySnapshot state = new([], [], []);

    internal Action? BeforeNextCommit { get; set; }

    public Task<StudySnapshot> ReadSnapshotAsync(CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(state);
        }
    }

    public Task CreateDeckAsync(Deck deck, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(deck);

        return Change((decks, _, _) =>
        {
            if (decks.Any(existing => existing.Id == deck.Id))
                throw new InvalidOperationException("The deck ID already exists.");

            decks.Add(deck);
        }, cancellationToken);
    }

    public Task UpdateDeckAsync(Deck deck, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(deck);

        return Change((decks, _, _) =>
        {
            var index = decks.FindIndex(existing => existing.Id == deck.Id);
            if (index < 0)
                throw new KeyNotFoundException("The deck was not found.");

            decks[index] = deck;
        }, cancellationToken);
    }

    public Task DeleteDeckAsync(Guid id, CancellationToken cancellationToken = default)
        => Change((decks, cards, reviews) =>
        {
            if (decks.RemoveAll(deck => deck.Id == id) == 0)
                throw new KeyNotFoundException("The deck was not found.");

            var removedCards = cards.Where(card => card.DeckId == id).Select(card => card.Id).ToHashSet();
            cards.RemoveAll(card => card.DeckId == id);
            reviews.RemoveAll(review => removedCards.Contains(review.CardId));
        }, cancellationToken);

    public Task CreateCardAsync(Card card, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(card);

        return Change((decks, cards, _) =>
        {
            if (!decks.Any(deck => deck.Id == card.DeckId))
                throw new KeyNotFoundException("The deck was not found.");

            if (cards.Any(existing => existing.Id == card.Id))
                throw new InvalidOperationException("The card ID already exists.");

            cards.Add(card);
        }, cancellationToken);
    }

    public Task UpdateCardAsync(Card card, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(card);

        return Change((_, cards, _) =>
        {
            var index = cards.FindIndex(existing => existing.Id == card.Id);
            if (index < 0)
                throw new KeyNotFoundException("The card was not found.");

            var current = cards[index];
            if (card.DeckId != current.DeckId || card.Schedule != current.Schedule)
                throw new InvalidOperationException("Content edits must preserve the card's deck and schedule.");

            cards[index] = card;
        }, cancellationToken);
    }

    public Task DeleteCardAsync(Guid id, CancellationToken cancellationToken = default)
        => Change((_, cards, reviews) =>
        {
            if (cards.RemoveAll(card => card.Id == id) == 0)
                throw new KeyNotFoundException("The card was not found.");

            reviews.RemoveAll(review => review.CardId == id);
        }, cancellationToken);

    public Task ApplyReviewAsync(ReviewEvent review, ReviewSchedule schedule, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(review);
        ArgumentNullException.ThrowIfNull(schedule);

        return Change((_, cards, reviews) =>
        {
            var index = cards.FindIndex(card => card.Id == review.CardId);
            if (index < 0)
                throw new KeyNotFoundException("The card was not found.");

            if (reviews.Any(existing => existing.Id == review.Id || existing.SessionItemId == review.SessionItemId))
                throw new InvalidOperationException("This review or session item was already recorded.");

            var current = cards[index];
            cards[index] = new Card(current.Id, current.DeckId, current.Front, current.Back, schedule);
            reviews.Add(review);
        }, cancellationToken);
    }

    private Task Change(Action<List<Deck>, List<Card>, List<ReviewEvent>> mutate, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var decks = state.Decks.ToList();
            var cards = state.Cards.ToList();
            var reviews = state.Reviews.ToList();
            mutate(decks, cards, reviews);

            var beforeCommit = BeforeNextCommit;
            BeforeNextCommit = null;
            beforeCommit?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();

            state = new StudySnapshot(decks, cards, reviews);
            return Task.CompletedTask;
        }
    }
}
