using RecallDeck.Storage.Checks;
using RecallDeck.Storage.Sqlite;
using SQLite;

namespace RecallDeck.Core.Tests.Storage;

public sealed class UiFailureStudyStoreTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "RecallDeck-ui-failure-" + Guid.NewGuid());

    [Fact]
    public async Task SyntheticFailurePreservesSnapshotAndRetryCommitsOnce()
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "study.db3");
        var store = new UiFailureStudyStore(path);
        var deck = new Deck(Guid.NewGuid(), "Synthetic UI fixture");
        var card = new Card(Guid.NewGuid(), deck.Id, UiFailureStudyStore.FailureMarker + "日本語 😀", "<script>literal</script>",
            new ReviewSchedule(0, DateTimeOffset.UnixEpoch));
        await store.CreateDeckAsync(deck);
        var before = await new SqliteStudyStore(path).ReadSnapshotAsync();

        var failure = await Assert.ThrowsAsync<IOException>(() => store.CreateCardAsync(card));

        Assert.Contains("synthetic UI failure", Assert.IsType<SQLiteException>(failure.InnerException).Message);
        var failed = await new SqliteStudyStore(path).ReadSnapshotAsync();
        Assert.Equal(before.Decks, failed.Decks);
        Assert.Equal(before.Cards, failed.Cards);
        Assert.Equal(before.Reviews, failed.Reviews);
        using (var db = new SQLiteConnection(path))
            Assert.Equal(0, db.ExecuteScalar<int>("SELECT COUNT(*) FROM sqlite_master WHERE type = 'trigger' AND name LIKE 'UiSmokeFail_%'"));

        await store.CreateCardAsync(card);
        var retried = await new SqliteStudyStore(path).ReadSnapshotAsync();

        Assert.Equal(card, Assert.Single(retried.Cards));
        Assert.Equal(deck, Assert.Single(retried.Decks));
        Assert.Empty(retried.Reviews);
    }

    [Fact]
    public async Task OrdinaryInputSucceedsWithoutConsumingTheSyntheticFailure()
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "ordinary.db3");
        var store = new UiFailureStudyStore(path);
        var deck = new Deck(Guid.NewGuid(), "Ordinary fixture");
        var ordinary = new Card(Guid.NewGuid(), deck.Id, "ordinary", "answer", new ReviewSchedule(0, DateTimeOffset.UnixEpoch));
        var synthetic = new Card(Guid.NewGuid(), deck.Id, UiFailureStudyStore.FailureMarker + "question", "answer", ordinary.Schedule);
        await store.CreateDeckAsync(deck);

        await store.CreateCardAsync(ordinary);
        await Assert.ThrowsAsync<IOException>(() => store.CreateCardAsync(synthetic));

        Assert.Equal(ordinary, Assert.Single((await new SqliteStudyStore(path).ReadSnapshotAsync()).Cards));
    }

    [Fact]
    public async Task ReviewFailureRollsBackScheduleAndHistoryForExactSyntheticCardAndRetryCommitsOnce()
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "review.db3");
        var store = new UiFailureStudyStore(path);
        var deck = new Deck(Guid.NewGuid(), "Synthetic review fixture");
        var schedule = new ReviewSchedule(0, DateTimeOffset.UnixEpoch);
        var synthetic = new Card(Guid.NewGuid(), deck.Id, "[RecallDeck synthetic failed review] Question", "Answer", schedule);
        var ordinary = new Card(Guid.NewGuid(), deck.Id, "Ordinary question", "Answer", schedule);
        await store.CreateDeckAsync(deck);
        await store.CreateCardAsync(synthetic);
        await store.CreateCardAsync(ordinary);
        var ordinaryReview = new ReviewEvent(Guid.NewGuid(), ordinary.Id, Guid.NewGuid(), ReviewRating.Remembered, DateTimeOffset.UnixEpoch);
        var next = new ReviewSchedule(1, DateTimeOffset.UnixEpoch.AddDays(1));
        await store.ApplyReviewAsync(ordinaryReview, next);
        var before = await new SqliteStudyStore(path).ReadSnapshotAsync();
        var review = new ReviewEvent(Guid.NewGuid(), synthetic.Id, Guid.NewGuid(), ReviewRating.Remembered, DateTimeOffset.UnixEpoch);

        var failure = await Assert.ThrowsAsync<IOException>(() => store.ApplyReviewAsync(review, next));

        Assert.Contains("synthetic UI review failure", Assert.IsType<SQLiteException>(failure.InnerException).Message);
        var failed = await new SqliteStudyStore(path).ReadSnapshotAsync();
        Assert.Equal(before.Decks, failed.Decks);
        Assert.Equal(before.Cards, failed.Cards);
        Assert.Equal(before.Reviews, failed.Reviews);
        using (var db = new SQLiteConnection(path))
            Assert.Equal(0, db.ExecuteScalar<int>("SELECT COUNT(*) FROM sqlite_master WHERE type = 'trigger' AND name LIKE 'UiSmokeReviewFail_%'"));

        await store.ApplyReviewAsync(review, next);
        var retried = await new SqliteStudyStore(path).ReadSnapshotAsync();
        Assert.Equal(2, retried.Reviews.Count);
        Assert.Contains(review, retried.Reviews);
        Assert.Contains(ordinaryReview, retried.Reviews);
        Assert.Equal(next, retried.Cards.Single(c => c.Id == synthetic.Id).Schedule);
        Assert.Equal(next, retried.Cards.Single(c => c.Id == ordinary.Id).Schedule);
    }

    [Fact]
    public async Task RestoreFailureRollsBackAllCollectionsAndOrdinaryRestoreDoesNotConsumeIt()
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "restore.db3");
        var store = new UiFailureStudyStore(path);
        var deck = new Deck(Guid.NewGuid(), "Owned baseline");
        var card = new Card(Guid.NewGuid(), deck.Id, "Baseline", "Answer", new(0, DateTimeOffset.UnixEpoch));
        var review = new ReviewEvent(Guid.NewGuid(), card.Id, Guid.NewGuid(), ReviewRating.Remembered, DateTimeOffset.UnixEpoch);
        var before = new RecallDeck.Core.Storage.StudySnapshot([deck], [card], [review]);
        await store.ReplaceAllAsync(before);
        var candidateDeck = new Deck(Guid.NewGuid(), "Owned replacement");
        var candidateCard = new Card(Guid.NewGuid(), candidateDeck.Id, "[RecallDeck synthetic failed restore] Question", "Answer", card.Schedule);
        var candidate = new RecallDeck.Core.Storage.StudySnapshot([candidateDeck], [candidateCard], []);

        var failure = await Assert.ThrowsAsync<IOException>(() => store.ReplaceAllAsync(candidate));

        Assert.Contains("synthetic UI restore failure", Assert.IsType<SQLiteException>(failure.InnerException).Message);
        var failed = await new SqliteStudyStore(path).ReadSnapshotAsync();
        Assert.Equal(before.Decks, failed.Decks);
        Assert.Equal(before.Cards, failed.Cards);
        Assert.Equal(before.Reviews, failed.Reviews);
        using (var db = new SQLiteConnection(path))
            Assert.Equal(0, db.ExecuteScalar<int>("SELECT COUNT(*) FROM sqlite_master WHERE type = 'trigger' AND name LIKE 'UiSmokeRestoreFail_%'"));

        await store.ReplaceAllAsync(candidate);
        var retried = await new SqliteStudyStore(path).ReadSnapshotAsync();
        Assert.Equal(candidate.Decks, retried.Decks);
        Assert.Equal(candidate.Cards, retried.Cards);
        Assert.Empty(retried.Reviews);
    }

    public void Dispose()
    {
        File.Delete(Path.Combine(directory, "study.db3"));
        File.Delete(Path.Combine(directory, "ordinary.db3"));
        File.Delete(Path.Combine(directory, "review.db3"));
        File.Delete(Path.Combine(directory, "restore.db3"));
        if (Directory.Exists(directory)) Directory.Delete(directory);
    }
}
