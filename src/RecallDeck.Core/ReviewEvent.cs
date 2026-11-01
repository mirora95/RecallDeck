namespace RecallDeck.Core;

public sealed record ReviewEvent
{
    public ReviewEvent(Guid id, Guid cardId, Guid sessionItemId, ReviewRating rating, DateTimeOffset reviewedAt)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("A review ID is required.", nameof(id));

        if (cardId == Guid.Empty)
            throw new ArgumentException("A card ID is required.", nameof(cardId));

        if (sessionItemId == Guid.Empty)
            throw new ArgumentException("A session item ID is required.", nameof(sessionItemId));

        if (rating is not (ReviewRating.Again or ReviewRating.Remembered))
            throw new ArgumentOutOfRangeException(nameof(rating));

        Id = id;
        CardId = cardId;
        SessionItemId = sessionItemId;
        Rating = rating;
        ReviewedAt = reviewedAt.ToUniversalTime();
    }

    public Guid Id { get; }
    public Guid CardId { get; }
    public Guid SessionItemId { get; }
    public ReviewRating Rating { get; }
    public DateTimeOffset ReviewedAt { get; }
}
