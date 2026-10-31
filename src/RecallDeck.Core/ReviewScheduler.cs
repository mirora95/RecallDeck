namespace RecallDeck.Core;

public sealed class ReviewScheduler
{
    private readonly TimeProvider clock;

    public ReviewScheduler(TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        this.clock = clock;
    }

    public Card CreateCard(Guid deckId, string front, string back)
        => new(Guid.NewGuid(), deckId, front, back, new ReviewSchedule(0, clock.GetUtcNow()));

    public ReviewSchedule Rate(ReviewSchedule current, ReviewRating rating)
    {
        ArgumentNullException.ThrowIfNull(current);

        if (rating is not (ReviewRating.Again or ReviewRating.Remembered))
            throw new ArgumentOutOfRangeException(nameof(rating));

        var now = clock.GetUtcNow().ToUniversalTime();
        if (rating == ReviewRating.Again)
            return new ReviewSchedule(0, now.AddMinutes(10));

        var nextLevel = Math.Min(current.Level + 1, 5);
        var days = nextLevel switch
        {
            1 => 1,
            2 => 3,
            3 => 7,
            4 => 14,
            _ => 30
        };

        return new ReviewSchedule(nextLevel, now.AddDays(days));
    }
}
