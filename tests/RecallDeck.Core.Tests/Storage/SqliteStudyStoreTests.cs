using RecallDeck.Storage.Sqlite;
using SQLite;

namespace RecallDeck.Core.Tests.Storage;

public sealed class SqliteStudyStoreTests : StudyStoreContractTests, IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "RecallDeck-tests-" + Guid.NewGuid());

    protected override StoreHarness CreateStore()
    {
        Directory.CreateDirectory(directory);
        var store = new SqliteStudyStore(Path.Combine(directory, Guid.NewGuid() + ".db3"));
        return new StoreHarness(store, action => store.BeforeNextCommit = action);
    }

    [Fact]
    public async Task ReopeningPreservesUnicodeIdentityAndUtcSchedule()
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "reopen.db3");
        var store = new SqliteStudyStore(path);
        var deck = new Deck(Guid.NewGuid(), "日本語 😀", "two\nlines");
        var card = new Card(Guid.NewGuid(), deck.Id, "<script>literal</script>", "'answer'", new ReviewSchedule(2, DateTimeOffset.UnixEpoch));
        await store.CreateDeckAsync(deck);
        await store.CreateCardAsync(card);

        var snapshot = await new SqliteStudyStore(path).ReadSnapshotAsync();

        Assert.Equal(deck, Assert.Single(snapshot.Decks));
        Assert.Equal(card, Assert.Single(snapshot.Cards));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task UnknownExistingDatabaseIsNotReset(int version)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "unknown.db3");
        using (var db = new SQLiteConnection(path))
        {
            db.Execute("CREATE TABLE Original (Value TEXT)");
            db.Execute("INSERT INTO Original VALUES (?)", "keep me");
            db.Execute($"PRAGMA user_version = {version}");
        }
        var original = await File.ReadAllBytesAsync(path);

        await Assert.ThrowsAsync<IOException>(() => new SqliteStudyStore(path).ReadSnapshotAsync());

        Assert.Equal(original, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task BrokenInitializationIsNotReplacedWithAnEmptyDatabase()
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "broken.db3");
        byte[] original = [0, 1, 2, 3, 4, 5];
        await File.WriteAllBytesAsync(path, original);

        await Assert.ThrowsAsync<IOException>(() => new SqliteStudyStore(path).ReadSnapshotAsync());

        Assert.Equal(original, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task TriggerFailureRollsBackScheduleAndAllowsTheSameReviewRetry()
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "rollback.db3");
        var store = new SqliteStudyStore(path);
        var deck = new Deck(Guid.NewGuid(), "Deck");
        var card = new Card(Guid.NewGuid(), deck.Id, "front", "back", new ReviewSchedule(0, DateTimeOffset.UnixEpoch));
        var review = new ReviewEvent(Guid.NewGuid(), card.Id, Guid.NewGuid(), ReviewRating.Remembered, DateTimeOffset.UnixEpoch);
        var next = new ReviewSchedule(1, DateTimeOffset.UnixEpoch.AddDays(1));
        await store.CreateDeckAsync(deck);
        await store.CreateCardAsync(card);
        using (var db = new SQLiteConnection(path))
            db.Execute("CREATE TRIGGER FailReview BEFORE INSERT ON Reviews BEGIN SELECT RAISE(ABORT, 'synthetic failure'); END");

        await Assert.ThrowsAsync<IOException>(() => store.ApplyReviewAsync(review, next));

        var rolledBack = await new SqliteStudyStore(path).ReadSnapshotAsync();
        Assert.Equal(card, Assert.Single(rolledBack.Cards));
        Assert.Empty(rolledBack.Reviews);
        using (var db = new SQLiteConnection(path)) db.Execute("DROP TRIGGER FailReview");

        await store.ApplyReviewAsync(review, next);
        var committed = await new SqliteStudyStore(path).ReadSnapshotAsync();
        Assert.Equal(next, Assert.Single(committed.Cards).Schedule);
        Assert.Equal(review, Assert.Single(committed.Reviews));
    }

    [Fact]
    public async Task AndroidSmokeFixtureAlsoPassesAgainstDesktopSqlite()
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "smoke.db3");

        await RecallDeck.Storage.Checks.SqliteSmoke.SeedAsync(path);
        await RecallDeck.Storage.Checks.SqliteSmoke.VerifyRestartAndRollbackAsync(path);
    }

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}
