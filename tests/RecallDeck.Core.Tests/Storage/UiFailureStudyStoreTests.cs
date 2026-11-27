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

    public void Dispose()
    {
        File.Delete(Path.Combine(directory, "study.db3"));
        File.Delete(Path.Combine(directory, "ordinary.db3"));
        if (Directory.Exists(directory)) Directory.Delete(directory);
    }
}
