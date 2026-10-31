using RecallDeck.Core;

namespace RecallDeck.Core.Tests;

public class ReviewSchedulerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 31, 23, 59, 42, TimeSpan.FromHours(5));

    [Fact]
    public void NewCardsHaveDistinctStableIdsAndAreDueImmediatelyAtClockUtcInstant()
    {
        var clock = new TestClock(Now);
        var scheduler = new ReviewScheduler(clock);
        var deckId = Guid.NewGuid();

        var first = scheduler.CreateCard(deckId, " question ", " answer ");
        var second = scheduler.CreateCard(deckId, "question", "answer");

        Assert.NotEqual(Guid.Empty, first.Id);
        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(deckId, first.DeckId);
        Assert.Equal("question", first.Front);
        Assert.Equal("answer", first.Back);
        Assert.Equal(0, first.Schedule.Level);
        Assert.Equal(Now.ToUniversalTime(), first.Schedule.DueAt);
        Assert.Equal(TimeSpan.Zero, first.Schedule.DueAt.Offset);
        Assert.Equal(2, clock.Reads);
    }

    [Theory]
    [InlineData(0, 1, 1)]
    [InlineData(1, 2, 3)]
    [InlineData(2, 3, 7)]
    [InlineData(3, 4, 14)]
    [InlineData(4, 5, 30)]
    [InlineData(5, 5, 30)]
    public void RememberedAdvancesToExpectedLevelAndElapsedDays(int level, int expectedLevel, int days)
    {
        var clock = new TestClock(Now);
        var scheduler = new ReviewScheduler(clock);
        var original = new ReviewSchedule(level, DateTimeOffset.UnixEpoch);

        var next = scheduler.Rate(original, ReviewRating.Remembered);

        Assert.Equal(expectedLevel, next.Level);
        Assert.Equal(Now.ToUniversalTime().AddDays(days), next.DueAt);
        Assert.Equal(TimeSpan.Zero, next.DueAt.Offset);
        Assert.Equal(level, original.Level);
        Assert.Equal(DateTimeOffset.UnixEpoch, original.DueAt);
        Assert.Equal(1, clock.Reads);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void AgainResetsEveryLevelAndSchedulesTenElapsedMinutes(int level)
    {
        var scheduler = new ReviewScheduler(new TestClock(Now));

        var next = scheduler.Rate(new ReviewSchedule(level, DateTimeOffset.UnixEpoch), ReviewRating.Again);

        Assert.Equal(0, next.Level);
        Assert.Equal(Now.ToUniversalTime().AddMinutes(10), next.DueAt);
        Assert.Equal(TimeSpan.Zero, next.DueAt.Offset);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    [InlineData(int.MaxValue)]
    public void UndefinedRatingsAreRejectedBeforeReadingClock(int rating)
    {
        var clock = new TestClock(Now);
        var scheduler = new ReviewScheduler(clock);

        Assert.Equal("rating", Assert.Throws<ArgumentOutOfRangeException>(() => scheduler.Rate(new ReviewSchedule(0, Now), (ReviewRating)rating)).ParamName);
        Assert.Equal(0, clock.Reads);
    }

    [Fact]
    public void EquivalentOffsetsProduceIdenticalUtcSchedulesAcrossCalendarBoundary()
    {
        var utc = new DateTimeOffset(2026, 10, 31, 23, 59, 0, TimeSpan.Zero);
        var first = new ReviewScheduler(new TestClock(utc));
        var second = new ReviewScheduler(new TestClock(utc.ToOffset(TimeSpan.FromHours(-7))));
        var original = new ReviewSchedule(0, utc);

        var result = first.Rate(original, ReviewRating.Remembered);

        Assert.Equal(new DateTimeOffset(2026, 11, 1, 23, 59, 0, TimeSpan.Zero), result.DueAt);
        Assert.Equal(result, second.Rate(original, ReviewRating.Remembered));
    }

    [Fact]
    public void RestoredScheduleNormalizesOffsetWithoutChangingInstant()
    {
        var schedule = new ReviewSchedule(5, Now);

        Assert.Equal(Now.ToUniversalTime(), schedule.DueAt);
        Assert.Equal(TimeSpan.Zero, schedule.DueAt.Offset);
    }

    [Fact]
    public void SchedulerUsesCurrentClockForEachDecision()
    {
        var clock = new TestClock(Now);
        var scheduler = new ReviewScheduler(clock);
        var first = scheduler.CreateCard(Guid.NewGuid(), "front", "back");
        clock.Now = Now.AddHours(2);

        var next = scheduler.Rate(first.Schedule, ReviewRating.Again);

        Assert.Equal(Now.ToUniversalTime().AddHours(2).AddMinutes(10), next.DueAt);
    }

    [Fact]
    public void MissingClockScheduleAndEmptyDeckAreRejected()
    {
        Assert.Throws<ArgumentNullException>(() => new ReviewScheduler(null!));
        var scheduler = new ReviewScheduler(new TestClock(Now));

        Assert.Throws<ArgumentNullException>(() => scheduler.Rate(null!, ReviewRating.Again));
        Assert.Throws<ArgumentException>(() => scheduler.CreateCard(Guid.Empty, "front", "back"));
    }

    [Fact]
    public void DateOverflowFailsWithoutChangingExistingSchedule()
    {
        var original = new ReviewSchedule(5, DateTimeOffset.UnixEpoch);
        var scheduler = new ReviewScheduler(new TestClock(DateTimeOffset.MaxValue));

        Assert.Throws<ArgumentOutOfRangeException>(() => scheduler.Rate(original, ReviewRating.Remembered));
        Assert.Equal(5, original.Level);
        Assert.Equal(DateTimeOffset.UnixEpoch, original.DueAt);
    }

    private sealed class TestClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public int Reads { get; private set; }

        public override DateTimeOffset GetUtcNow()
        {
            Reads++;
            return Now;
        }
    }
}
