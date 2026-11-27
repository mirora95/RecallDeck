using RecallDeck.Core;
using RecallDeck.Core.Storage;
using RecallDeck.Storage.Sqlite;
using SQLite;

namespace RecallDeck.Storage.Checks;

public static class SqliteSmoke
{
    private static readonly Deck Deck = new(Guid.Parse("5754f2d4-fad9-480a-a54b-84c14d1271a4"), "日本語 😀", "two\nlines");
    private static readonly Card Card = new(Guid.Parse("8b17d062-99cb-47cf-b572-b27758f378f9"), Deck.Id,
        "<script>literal</script>", "'answer'", new ReviewSchedule(0, DateTimeOffset.UnixEpoch));
    private static readonly ReviewEvent Review = new(Guid.Parse("38e6a6d5-a0d5-4d50-bb60-27ecb8d03702"), Card.Id,
        Guid.Parse("52bd8228-5e4f-4ea0-a0f7-00757078fca4"), ReviewRating.Remembered, DateTimeOffset.UnixEpoch);
    private static readonly ReviewSchedule Next = new(1, DateTimeOffset.UnixEpoch.AddDays(1));

    public static async Task SeedAsync(string path)
    {
        if (File.Exists(path)) throw new InvalidOperationException("Preserve existing smoke data: resume verification instead.");
        SeedVersionOne(path);
        await CheckSavedAsync(path);
    }

    public static void SeedVersionOne(string path)
    {
        if (File.Exists(path)) throw new InvalidOperationException("Preserve existing smoke data: resume verification instead.");
        using var db = new SQLiteConnection(path);
        db.RunInTransaction(() =>
        {
            // The shipped version-1 DDL is a fixture, independent of production initialization.
            db.Execute("CREATE TABLE Decks (Id TEXT PRIMARY KEY NOT NULL, Title TEXT NOT NULL, Description TEXT NOT NULL)");
            db.Execute("CREATE TABLE Cards (Id TEXT PRIMARY KEY NOT NULL, DeckId TEXT NOT NULL REFERENCES Decks(Id) ON DELETE CASCADE, Front TEXT NOT NULL, Back TEXT NOT NULL, Level INTEGER NOT NULL, DueAtTicks INTEGER NOT NULL)");
            db.Execute("CREATE TABLE Reviews (Id TEXT PRIMARY KEY NOT NULL, CardId TEXT NOT NULL REFERENCES Cards(Id) ON DELETE CASCADE, SessionItemId TEXT NOT NULL UNIQUE, Rating INTEGER NOT NULL, ReviewedAtTicks INTEGER NOT NULL)");
            db.Execute("CREATE INDEX CardsByDeck ON Cards(DeckId)");
            db.Execute("CREATE INDEX ReviewsByCard ON Reviews(CardId)");
            db.Execute("INSERT INTO Decks VALUES (?, ?, ?)", Deck.Id.ToString(), Deck.Title, Deck.Description);
            db.Execute("INSERT INTO Cards VALUES (?, ?, ?, ?, ?, ?)", Card.Id.ToString(), Card.DeckId.ToString(), Card.Front, Card.Back, Next.Level, Next.DueAt.UtcTicks);
            db.Execute("INSERT INTO Reviews VALUES (?, ?, ?, ?, ?)", Review.Id.ToString(), Review.CardId.ToString(), Review.SessionItemId.ToString(), (int)Review.Rating, Review.ReviewedAt.UtcTicks);
            db.Execute("PRAGMA user_version = 1");
        });
    }

    public static async Task VerifyRestartAndRollbackAsync(string path)
    {
        // Check the exact synthetic fixture before any destructive verification or cleanup.
        await CheckSavedAsync(path);
        var store = new SqliteStudyStore(path);
        var second = new ReviewEvent(Guid.Parse("10573086-c355-4c18-9cd5-4f0c69788757"), Card.Id,
            Guid.Parse("0dce04f6-80d8-41c0-bf28-7e6d05c8317a"), ReviewRating.Remembered, DateTimeOffset.UnixEpoch.AddDays(1));
        var later = new ReviewSchedule(2, DateTimeOffset.UnixEpoch.AddDays(4));
        using (var db = new SQLiteConnection(path))
            db.Execute("CREATE TRIGGER IF NOT EXISTS FailReview BEFORE INSERT ON Reviews BEGIN SELECT RAISE(ABORT, 'synthetic failure'); END");
        try
        {
            try
            {
                await store.ApplyReviewAsync(second, later);
                throw new InvalidOperationException("Expected a failed review write.");
            }
            catch (IOException error) when (error.InnerException is SQLiteException sql && sql.Message.Contains("synthetic failure", StringComparison.Ordinal)) { }
            await CheckSavedAsync(path);
        }
        finally
        {
            using var db = new SQLiteConnection(path);
            db.Execute("DROP TRIGGER IF EXISTS FailReview");
        }
        await store.ApplyReviewAsync(second, later);
        var saved = await new SqliteStudyStore(path).ReadSnapshotAsync();
        if (saved.Cards.Single().Schedule != later || saved.Reviews.Count != 2 || !saved.Reviews.Contains(second))
            throw new InvalidOperationException("Review retry was not atomic.");
        await VerifyReplacementAsync(path, saved);
        await store.DeleteDeckAsync(Deck.Id);
        var empty = await new SqliteStudyStore(path).ReadSnapshotAsync();
        if (empty.Decks.Count != 0 || empty.Cards.Count != 0 || empty.Reviews.Count != 0)
            throw new InvalidOperationException("Cascade left related records.");
    }

    private static async Task VerifyReplacementAsync(string path, StudySnapshot before)
    {
        var restoredDeck = new Deck(Deck.Id, "Restored 日本語 😀", "  restored\nnotes");
        var restoredCard = new Card(Card.Id, Deck.Id, "Restored <script>", "answer 😀",
            new ReviewSchedule(5, DateTimeOffset.UnixEpoch.AddDays(30).AddTicks(1234567)));
        var replacement = new StudySnapshot([restoredDeck], [restoredCard], [Review]);
        var store = new SqliteStudyStore(path);
        using (var db = new SQLiteConnection(path))
            db.Execute("CREATE TRIGGER IF NOT EXISTS FailRestore BEFORE INSERT ON Reviews BEGIN SELECT RAISE(ABORT, 'synthetic restore failure'); END");
        try
        {
            try
            {
                await store.ReplaceAllAsync(replacement);
                throw new InvalidOperationException("Expected a failed replacement after deletion and insertion.");
            }
            catch (IOException error) when (error.InnerException is SQLiteException sql && sql.Message.Contains("synthetic restore failure", StringComparison.Ordinal)) { }
            RequireSnapshot(before, await new SqliteStudyStore(path).ReadSnapshotAsync());
        }
        finally
        {
            using var db = new SQLiteConnection(path);
            db.Execute("DROP TRIGGER IF EXISTS FailRestore");
        }

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        try
        {
            await store.ReplaceAllAsync(replacement, cancellation.Token);
            throw new InvalidOperationException("Expected canceled replacement.");
        }
        catch (OperationCanceledException) { }
        RequireSnapshot(before, await new SqliteStudyStore(path).ReadSnapshotAsync());

        await store.ReplaceAllAsync(replacement);
        RequireSnapshot(replacement, await new SqliteStudyStore(path).ReadSnapshotAsync());
        await store.ReplaceAllAsync(new StudySnapshot([], [], []));
        RequireSnapshot(new StudySnapshot([], [], []), await new SqliteStudyStore(path).ReadSnapshotAsync());
        await store.ReplaceAllAsync(before);
        RequireSnapshot(before, await new SqliteStudyStore(path).ReadSnapshotAsync());
    }

    private static void RequireSnapshot(StudySnapshot expected, StudySnapshot actual)
    {
        if (!expected.Decks.OrderBy(row => row.Id).SequenceEqual(actual.Decks.OrderBy(row => row.Id)) ||
            !expected.Cards.OrderBy(row => row.Id).SequenceEqual(actual.Cards.OrderBy(row => row.Id)) ||
            !expected.Reviews.OrderBy(row => row.Id).SequenceEqual(actual.Reviews.OrderBy(row => row.Id)))
            throw new InvalidOperationException("Replacement, rollback or reopening changed a stored field.");
    }

    public static async Task CheckSavedAsync(string path)
    {
        var saved = await new SqliteStudyStore(path).ReadSnapshotAsync();
        using var db = new SQLiteConnection(path);
        if (db.ExecuteScalar<int>("PRAGMA user_version") != 2)
            throw new InvalidOperationException("Version-1 smoke fixture did not upgrade to version 2.");
        var expectedCard = new Card(Card.Id, Deck.Id, Card.Front, Card.Back, Next);
        if (saved.Decks.Count != 1 || saved.Decks[0] != Deck || saved.Cards.Count != 1 || saved.Cards[0] != expectedCard ||
            saved.Reviews.Count != 1 || saved.Reviews[0] != Review)
            throw new InvalidOperationException("Stored identity, Unicode, schedule or history did not match the synthetic fixture.");
    }
}
