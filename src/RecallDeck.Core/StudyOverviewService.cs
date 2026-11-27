using RecallDeck.Core.Storage;

namespace RecallDeck.Core;

public sealed record DeckDueSummary(Deck Deck, int DueCount);
public sealed record ReviewHistoryItem(ReviewEvent Event, Card? Card, Deck? Deck);
public sealed record StudyOverview(IReadOnlyList<DeckDueSummary> Decks, int DueCount, int CardCount,
    DateTimeOffset? NextDueAt, int AgainCount, int RememberedCount, IReadOnlyList<ReviewHistoryItem> RecentReviews);

public sealed class StudyOverviewService
{
    public const int RecentLimit = 20;
    private readonly IStudyStore store;
    private readonly TimeProvider clock;

    public StudyOverviewService(IStudyStore store, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(clock);
        this.store = store;
        this.clock = clock;
    }

    public async Task<StudyOverview> ReadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var snapshot = await store.ReadSnapshotAsync(cancellationToken);
        var now = clock.GetUtcNow();
        var dueByDeck = snapshot.Cards.Where(card => card.Schedule.DueAt <= now).ToLookup(card => card.DeckId);
        var decks = snapshot.Decks.OrderBy(deck => deck.Title, StringComparer.OrdinalIgnoreCase).ThenBy(deck => deck.Id)
            .Select(deck => new DeckDueSummary(deck, dueByDeck[deck.Id].Count())).ToArray();
        var cardsById = snapshot.Cards.ToLookup(card => card.Id);
        var decksById = snapshot.Decks.ToLookup(deck => deck.Id);
        var recent = snapshot.Reviews.OrderByDescending(review => review.ReviewedAt).ThenBy(review => review.Id)
            .Take(RecentLimit).Select(review =>
            {
                var card = cardsById[review.CardId].FirstOrDefault();
                var deck = card is null ? null : decksById[card.DeckId].FirstOrDefault();
                return new ReviewHistoryItem(review, card, deck);
            }).ToArray();

        return new StudyOverview(Array.AsReadOnly(decks), dueByDeck.Sum(group => group.Count()), snapshot.Cards.Count,
            snapshot.Cards.Select(card => (DateTimeOffset?)card.Schedule.DueAt).Where(due => due > now).Min(),
            snapshot.Reviews.Count(review => review.Rating == ReviewRating.Again),
            snapshot.Reviews.Count(review => review.Rating == ReviewRating.Remembered), Array.AsReadOnly(recent));
    }
}
