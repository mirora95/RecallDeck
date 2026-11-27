using System.Text;

namespace RecallDeck.Core.Storage;

public sealed class StudySnapshot
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

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

    /// <summary>Validate related immutable Core values without applying a serialized backup size limit.</summary>
    public void Validate()
    {
        var deckIds = new HashSet<Guid>();
        var cardIds = new HashSet<Guid>();
        var reviewIds = new HashSet<Guid>();
        var sessionIds = new HashSet<Guid>();
        foreach (var deck in Decks)
        {
            if (deck is null || !deckIds.Add(deck.Id))
                throw new ArgumentException("Decks require nonnull rows with unique IDs.", nameof(Decks));
            StrictUtf8.GetByteCount(deck.Title);
            StrictUtf8.GetByteCount(deck.Description);
        }
        foreach (var card in Cards)
        {
            if (card is null || !cardIds.Add(card.Id) || !deckIds.Contains(card.DeckId))
                throw new ArgumentException("Cards require nonnull rows, unique IDs and existing decks.", nameof(Cards));
            StrictUtf8.GetByteCount(card.Front);
            StrictUtf8.GetByteCount(card.Back);
        }
        foreach (var review in Reviews)
            if (review is null || !reviewIds.Add(review.Id) || !sessionIds.Add(review.SessionItemId) || !cardIds.Contains(review.CardId))
                throw new ArgumentException("Reviews require nonnull rows, unique IDs and session items, and existing cards.", nameof(Reviews));
    }
}
