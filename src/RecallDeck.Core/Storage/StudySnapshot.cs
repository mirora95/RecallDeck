namespace RecallDeck.Core.Storage;

public sealed class StudySnapshot
{
    public StudySnapshot(IEnumerable<Deck> decks, IEnumerable<Card> cards, IEnumerable<ReviewEvent> reviews)
    {
        ArgumentNullException.ThrowIfNull(decks);
        ArgumentNullException.ThrowIfNull(cards);
        ArgumentNullException.ThrowIfNull(reviews);

        Decks = Array.AsReadOnly(decks.ToArray());
        Cards = Array.AsReadOnly(cards.ToArray());
        Reviews = Array.AsReadOnly(reviews.ToArray());
    }

    public IReadOnlyList<Deck> Decks { get; }
    public IReadOnlyList<Card> Cards { get; }
    public IReadOnlyList<ReviewEvent> Reviews { get; }
}
