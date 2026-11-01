using RecallDeck.Core.Storage;

namespace RecallDeck.Core.Tests.Storage;

public class StorageValueTests
{
    [Fact]
    public void SnapshotDefensivelyCopiesInputsAndDoesNotExposeMutableLists()
    {
        var deck = new Deck(Guid.NewGuid(), "Deck");
        var decks = new List<Deck> { deck };
        var cards = new List<Card>();
        var reviews = new List<ReviewEvent>();
        var snapshot = new StudySnapshot(decks, cards, reviews);

        decks.Clear();
        cards.Add(new Card(Guid.NewGuid(), deck.Id, "front", "back", new ReviewSchedule(0, DateTimeOffset.UnixEpoch)));
        reviews.Add(new ReviewEvent(Guid.NewGuid(), cards[0].Id, Guid.NewGuid(), ReviewRating.Again, DateTimeOffset.UnixEpoch));

        Assert.Equal(deck, Assert.Single(snapshot.Decks));
        Assert.Empty(snapshot.Cards);
        Assert.Empty(snapshot.Reviews);
        Assert.Throws<NotSupportedException>(() => Assert.IsAssignableFrom<IList<Deck>>(snapshot.Decks).Clear());
        Assert.Throws<NotSupportedException>(() => Assert.IsAssignableFrom<IList<Card>>(snapshot.Cards).Clear());
        Assert.Throws<NotSupportedException>(() => Assert.IsAssignableFrom<IList<ReviewEvent>>(snapshot.Reviews).Clear());
    }

    [Theory]
    [InlineData("id")]
    [InlineData("cardId")]
    [InlineData("sessionItemId")]
    public void ReviewRejectsEmptyIdentities(string field)
    {
        var id = Guid.NewGuid();

        var error = Assert.Throws<ArgumentException>(() => new ReviewEvent(field == "id" ? Guid.Empty : id, field == "cardId" ? Guid.Empty : id, field == "sessionItemId" ? Guid.Empty : id, ReviewRating.Again, DateTimeOffset.UnixEpoch));

        Assert.Equal(field, error.ParamName);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    public void ReviewRejectsUnknownRating(int rating)
    {
        Assert.Equal("rating", Assert.Throws<ArgumentOutOfRangeException>(() => new ReviewEvent(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), (ReviewRating)rating, DateTimeOffset.UnixEpoch)).ParamName);
    }

    [Fact]
    public void ReviewNormalizesInstantToUtc()
    {
        var at = new DateTimeOffset(2026, 11, 1, 19, 0, 0, TimeSpan.FromHours(5));

        var review = new ReviewEvent(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), ReviewRating.Remembered, at);

        Assert.Equal(at.ToUniversalTime(), review.ReviewedAt);
        Assert.Equal(TimeSpan.Zero, review.ReviewedAt.Offset);
    }
}
