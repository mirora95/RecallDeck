namespace RecallDeck.Core;

public sealed record ReviewSchedule
{
    public ReviewSchedule(int level, DateTimeOffset dueAt)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(level, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(level, 5);

        Level = level;
        DueAt = dueAt.ToUniversalTime();
    }

    public int Level { get; }
    public DateTimeOffset DueAt { get; }
}
