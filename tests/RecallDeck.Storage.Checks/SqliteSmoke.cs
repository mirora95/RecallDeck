using RecallDeck.Core;
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
        var store = new SqliteStudyStore(path);
        await store.CreateDeckAsync(Deck);
        await store.CreateCardAsync(Card);
        await store.ApplyReviewAsync(Review, Next);
        await CheckSavedAsync(path);
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
            db.Execute("CREATE TRIGGER FailReview BEFORE INSERT ON Reviews BEGIN SELECT RAISE(ABORT, 'synthetic failure'); END");
        try
        {
            await store.ApplyReviewAsync(second, later);
            throw new InvalidOperationException("Expected a failed review write.");
        }
        catch (IOException) { }
        await CheckSavedAsync(path);
        using (var db = new SQLiteConnection(path)) db.Execute("DROP TRIGGER FailReview");
        await store.ApplyReviewAsync(second, later);
        var saved = await new SqliteStudyStore(path).ReadSnapshotAsync();
        if (saved.Cards.Single().Schedule != later || saved.Reviews.Count != 2 || !saved.Reviews.Contains(second))
            throw new InvalidOperationException("Review retry was not atomic.");
        await store.DeleteDeckAsync(Deck.Id);
        var empty = await new SqliteStudyStore(path).ReadSnapshotAsync();
        if (empty.Decks.Count != 0 || empty.Cards.Count != 0 || empty.Reviews.Count != 0)
            throw new InvalidOperationException("Cascade left related records.");
    }

    private static async Task CheckSavedAsync(string path)
    {
        var saved = await new SqliteStudyStore(path).ReadSnapshotAsync();
        var expectedCard = new Card(Card.Id, Deck.Id, Card.Front, Card.Back, Next);
        if (saved.Decks.Count != 1 || saved.Decks[0] != Deck || saved.Cards.Count != 1 || saved.Cards[0] != expectedCard ||
            saved.Reviews.Count != 1 || saved.Reviews[0] != Review)
            throw new InvalidOperationException("Stored identity, Unicode, schedule or history did not match the synthetic fixture.");
    }
}
