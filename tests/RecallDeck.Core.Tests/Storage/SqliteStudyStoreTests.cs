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

    [Fact]
    public async Task VersionOneUpgradePreservesEveryFieldAndReopensAtVersionTwo()
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "upgrade.db3");
        RecallDeck.Storage.Checks.SqliteSmoke.SeedVersionOne(path);
        using (var db = new SQLiteConnection(path))
            Assert.Equal(1, db.ExecuteScalar<int>("PRAGMA user_version"));

        await RecallDeck.Storage.Checks.SqliteSmoke.CheckSavedAsync(path);
        await RecallDeck.Storage.Checks.SqliteSmoke.CheckSavedAsync(path);

        using var upgraded = new SQLiteConnection(path);
        Assert.Equal(2, upgraded.ExecuteScalar<int>("PRAGMA user_version"));
        Assert.Equal(4, upgraded.ExecuteScalar<int>("SELECT COUNT(*) FROM sqlite_master WHERE type = 'trigger'"));
    }

    [Fact]
    public async Task UpgradeRetainsUnrelatedObjectsAndRows()
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "objects.db3");
        RecallDeck.Storage.Checks.SqliteSmoke.SeedVersionOne(path);
        string[] objects;
        using (var db = new SQLiteConnection(path))
        {
            db.Execute("CREATE TABLE Extension (Value TEXT)");
            db.Execute("INSERT INTO Extension VALUES (?)", "日本語 😀");
            db.Execute("CREATE INDEX ExtensionValue ON Extension(Value)");
            db.Execute("CREATE VIEW ExtensionView AS SELECT Value FROM Extension");
            db.Execute("CREATE TRIGGER ExtensionTrigger AFTER UPDATE ON Extension BEGIN SELECT 1; END");
            objects = db.Query<SchemaObject>("SELECT sql AS Sql FROM sqlite_master WHERE sql IS NOT NULL ORDER BY name").Select(row => row.Sql).ToArray();
        }

        await RecallDeck.Storage.Checks.SqliteSmoke.CheckSavedAsync(path);

        using var upgraded = new SQLiteConnection(path);
        var after = upgraded.Query<SchemaObject>("SELECT sql AS Sql FROM sqlite_master WHERE sql IS NOT NULL AND name NOT LIKE 'Validate%' ORDER BY name").Select(row => row.Sql).ToArray();
        Assert.Equal(objects, after);
        Assert.Equal("日本語 😀", upgraded.ExecuteScalar<string>("SELECT Value FROM ExtensionView"));
    }

    public sealed class SchemaObject
    {
        public string Sql { get; set; } = "";
    }

    [Fact]
    public async Task LastTriggerCollisionRollsBackEarlierTriggersAndAllowsRetry()
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "collision.db3");
        RecallDeck.Storage.Checks.SqliteSmoke.SeedVersionOne(path);
        using (var db = new SQLiteConnection(path))
            db.Execute("CREATE TRIGGER ValidateReviewRatingUpdate BEFORE UPDATE ON Reviews BEGIN SELECT 1; END");
        var original = await File.ReadAllBytesAsync(path);

        await Assert.ThrowsAsync<IOException>(() => new SqliteStudyStore(path).ReadSnapshotAsync());

        Assert.Equal(original, await File.ReadAllBytesAsync(path));
        using (var db = new SQLiteConnection(path))
        {
            Assert.Equal(1, db.ExecuteScalar<int>("PRAGMA user_version"));
            Assert.Equal(1, db.ExecuteScalar<int>("SELECT COUNT(*) FROM sqlite_master WHERE type = 'trigger'"));
            db.Execute("DROP TRIGGER ValidateReviewRatingUpdate");
        }
        await RecallDeck.Storage.Checks.SqliteSmoke.CheckSavedAsync(path);
        using var upgraded = new SQLiteConnection(path);
        Assert.Equal(2, upgraded.ExecuteScalar<int>("PRAGMA user_version"));
    }

    [Theory]
    [InlineData("Cards", "Level", "6")]
    [InlineData("Cards", "Level", "-1")]
    [InlineData("Cards", "Level", "1.5")]
    [InlineData("Cards", "Level", "'bad'")]
    [InlineData("Reviews", "Rating", "2")]
    [InlineData("Reviews", "Rating", "-1")]
    [InlineData("Reviews", "Rating", "0.5")]
    [InlineData("Reviews", "Rating", "'bad'")]
    public async Task InvalidVersionOneValuesPreserveFileAndVersion(string table, string column, string value)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "invalid.db3");
        RecallDeck.Storage.Checks.SqliteSmoke.SeedVersionOne(path);
        using (var db = new SQLiteConnection(path)) db.Execute($"UPDATE {table} SET {column} = {value}");
        var original = await File.ReadAllBytesAsync(path);

        var failure = await Assert.ThrowsAsync<IOException>(() => new SqliteStudyStore(path).ReadSnapshotAsync());

        Assert.Contains("Preserve", failure.Message);
        Assert.Equal(original, await File.ReadAllBytesAsync(path));
        using var unchanged = new SQLiteConnection(path);
        Assert.Equal(1, unchanged.ExecuteScalar<int>("PRAGMA user_version"));
        Assert.Equal(0, unchanged.ExecuteScalar<int>("SELECT COUNT(*) FROM sqlite_master WHERE type = 'trigger'"));
    }

    [Theory]
    [InlineData("Cards", "Level", "6")]
    [InlineData("Cards", "Level", "-1")]
    [InlineData("Cards", "Level", "1.5")]
    [InlineData("Cards", "Level", "'bad'")]
    [InlineData("Reviews", "Rating", "2")]
    [InlineData("Reviews", "Rating", "-1")]
    [InlineData("Reviews", "Rating", "0.5")]
    [InlineData("Reviews", "Rating", "'bad'")]
    public async Task VersionTwoRejectsInvalidInsertAndUpdateAtPersistenceBoundary(string table, string column, string value)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "bounds.db3");
        RecallDeck.Storage.Checks.SqliteSmoke.SeedVersionOne(path);
        await RecallDeck.Storage.Checks.SqliteSmoke.CheckSavedAsync(path);
        using var db = new SQLiteConnection(path);

        Assert.Throws<SQLiteException>(() => db.Execute($"UPDATE {table} SET {column} = {value}"));
        var insert = table == "Cards"
            ? $"INSERT INTO Cards SELECT 'other', DeckId, Front, Back, {value}, DueAtTicks FROM Cards"
            : $"INSERT INTO Reviews SELECT 'other', CardId, 'other-session', {value}, ReviewedAtTicks FROM Reviews";
        Assert.Throws<SQLiteException>(() => db.Execute(insert));
        await RecallDeck.Storage.Checks.SqliteSmoke.CheckSavedAsync(path);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
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

    [Fact]
    public async Task AndroidSmokeResumesAfterInterruptedFailureInjection()
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "interrupted-smoke.db3");
        await RecallDeck.Storage.Checks.SqliteSmoke.SeedAsync(path);
        using (var db = new SQLiteConnection(path))
            db.Execute("CREATE TRIGGER FailReview BEFORE INSERT ON Reviews BEGIN SELECT RAISE(ABORT, 'synthetic failure'); END");

        await RecallDeck.Storage.Checks.SqliteSmoke.VerifyRestartAndRollbackAsync(path);

        using var verified = new SQLiteConnection(path);
        Assert.Equal(0, verified.ExecuteScalar<int>("SELECT COUNT(*) FROM sqlite_master WHERE type = 'trigger' AND name = 'FailReview'"));
    }

    [Fact]
    public async Task AndroidSmokeRemovesItsTriggerWhenVerificationFails()
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "failed-smoke.db3");
        await RecallDeck.Storage.Checks.SqliteSmoke.SeedAsync(path);
        using (var db = new SQLiteConnection(path))
            db.Execute("CREATE TRIGGER FailReview BEFORE INSERT ON Reviews BEGIN SELECT RAISE(IGNORE); END");

        await Assert.ThrowsAsync<InvalidOperationException>(() => RecallDeck.Storage.Checks.SqliteSmoke.VerifyRestartAndRollbackAsync(path));

        using var verified = new SQLiteConnection(path);
        Assert.Equal(0, verified.ExecuteScalar<int>("SELECT COUNT(*) FROM sqlite_master WHERE type = 'trigger' AND name = 'FailReview'"));
    }

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}
