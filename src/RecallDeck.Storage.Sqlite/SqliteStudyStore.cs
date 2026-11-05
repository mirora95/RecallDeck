using RecallDeck.Core;
using RecallDeck.Core.Storage;
using SQLite;

namespace RecallDeck.Storage.Sqlite;

public sealed class SqliteStudyStore : IStudyStore
{
    private readonly string databasePath;
    private readonly object gate = new();
    internal Action? BeforeNextCommit { get; set; }

    public SqliteStudyStore(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        if (!Path.IsPathFullyQualified(databasePath)) throw new ArgumentException("An absolute app-private database path is required.", nameof(databasePath));
        this.databasePath = databasePath;
    }

    public Task<StudySnapshot> ReadSnapshotAsync(CancellationToken cancellationToken = default)
        => Execute(db =>
        {
            try
            {
                return new StudySnapshot(
                    db.Table<DeckRow>().ToList().Select(x => new Deck(Guid.Parse(x.Id), x.Title, x.Description)),
                    db.Table<CardRow>().ToList().Select(x => new Card(Guid.Parse(x.Id), Guid.Parse(x.DeckId), x.Front, x.Back,
                        new ReviewSchedule(x.Level, new DateTimeOffset(x.DueAtTicks, TimeSpan.Zero)))),
                    db.Table<ReviewRow>().ToList().Select(x => new ReviewEvent(Guid.Parse(x.Id), Guid.Parse(x.CardId), Guid.Parse(x.SessionItemId),
                        (ReviewRating)x.Rating, new DateTimeOffset(x.ReviewedAtTicks, TimeSpan.Zero))));
            }
            catch (Exception error) when (error is ArgumentException or FormatException)
            {
                throw new IOException("Saved study data is invalid. Preserve the database and restore a verified backup; do not clear storage.", error);
            }
        }, cancellationToken);

    public Task CreateDeckAsync(Deck deck, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(deck);
        return Change(db =>
        {
            if (db.Find<DeckRow>(deck.Id.ToString()) is not null) throw new InvalidOperationException("The deck ID already exists.");
            db.Insert(new DeckRow { Id = deck.Id.ToString(), Title = deck.Title, Description = deck.Description });
        }, cancellationToken);
    }

    public Task UpdateDeckAsync(Deck deck, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(deck);
        return Change(db =>
        {
            if (db.Update(new DeckRow { Id = deck.Id.ToString(), Title = deck.Title, Description = deck.Description }) == 0)
                throw new KeyNotFoundException("The deck was not found.");
        }, cancellationToken);
    }

    public Task DeleteDeckAsync(Guid id, CancellationToken cancellationToken = default)
        => Change(db =>
        {
            if (db.Execute("DELETE FROM Decks WHERE Id = ?", id.ToString()) == 0) throw new KeyNotFoundException("The deck was not found.");
        }, cancellationToken);

    public Task CreateCardAsync(Card card, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(card);
        return Change(db =>
        {
            if (db.Find<CardRow>(card.Id.ToString()) is not null) throw new InvalidOperationException("The card ID already exists.");
            if (db.Find<DeckRow>(card.DeckId.ToString()) is null) throw new KeyNotFoundException("The deck was not found.");
            db.Insert(new CardRow { Id = card.Id.ToString(), DeckId = card.DeckId.ToString(), Front = card.Front, Back = card.Back,
                Level = card.Schedule.Level, DueAtTicks = card.Schedule.DueAt.UtcTicks });
        }, cancellationToken);
    }

    public Task UpdateCardAsync(Card card, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(card);
        return Change(db =>
        {
            var previous = db.Find<CardRow>(card.Id.ToString()) ?? throw new KeyNotFoundException("The card was not found.");
            if (previous.DeckId != card.DeckId.ToString() || previous.Level != card.Schedule.Level || previous.DueAtTicks != card.Schedule.DueAt.UtcTicks)
                throw new InvalidOperationException("Content edits must preserve the card's deck and schedule.");
            db.Execute("UPDATE Cards SET Front = ?, Back = ? WHERE Id = ?", card.Front, card.Back, card.Id.ToString());
        }, cancellationToken);
    }

    public Task DeleteCardAsync(Guid id, CancellationToken cancellationToken = default)
        => Change(db =>
        {
            if (db.Execute("DELETE FROM Cards WHERE Id = ?", id.ToString()) == 0) throw new KeyNotFoundException("The card was not found.");
        }, cancellationToken);

    public Task ApplyReviewAsync(ReviewEvent review, ReviewSchedule schedule, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(review);
        ArgumentNullException.ThrowIfNull(schedule);
        return Change(db =>
        {
            if (db.Find<CardRow>(review.CardId.ToString()) is null) throw new KeyNotFoundException("The card was not found.");
            if (db.Find<ReviewRow>(review.Id.ToString()) is not null ||
                db.ExecuteScalar<int>("SELECT COUNT(*) FROM Reviews WHERE SessionItemId = ?", review.SessionItemId.ToString()) != 0)
                throw new InvalidOperationException("The review identity or session item already exists.");
            db.Execute("UPDATE Cards SET Level = ?, DueAtTicks = ? WHERE Id = ?", schedule.Level, schedule.DueAt.UtcTicks, review.CardId.ToString());
            db.Insert(new ReviewRow { Id = review.Id.ToString(), CardId = review.CardId.ToString(), SessionItemId = review.SessionItemId.ToString(),
                Rating = (int)review.Rating, ReviewedAtTicks = review.ReviewedAt.UtcTicks });
        }, cancellationToken);
    }

    private Task Change(Action<SQLiteConnection> change, CancellationToken cancellationToken)
        => Execute(db =>
        {
            change(db);
            var beforeCommit = BeforeNextCommit;
            BeforeNextCommit = null;
            beforeCommit?.Invoke();
            return true;
        }, cancellationToken);

    private Task<T> Execute<T>(Func<SQLiteConnection, T> operation, CancellationToken cancellationToken)
        => Task.Run(() =>
        {
            lock (gate)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    using var db = new SQLiteConnection(databasePath, SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.FullMutex);
                    db.BusyTimeout = TimeSpan.FromSeconds(5);
                    db.Execute("PRAGMA foreign_keys = ON");
                    T result = default!;
                    db.RunInTransaction(() =>
                    {
                        Initialize(db);
                        cancellationToken.ThrowIfCancellationRequested();
                        result = operation(db);
                        cancellationToken.ThrowIfCancellationRequested();
                    });
                    return result;
                }
                catch (SQLiteException error)
                {
                    throw new IOException("Study storage could not complete the operation. Keep your edits and retry; preserve the database and do not clear app data.", error);
                }
            }
        }, cancellationToken);

    private static void Initialize(SQLiteConnection db)
    {
        var version = db.ExecuteScalar<int>("PRAGMA user_version");
        if (version == 1) return;
        if (version != 0 || db.ExecuteScalar<int>("SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%'") != 0)
            throw new IOException("This database schema is not supported. Preserve it and use the matching app version or a verified backup; do not clear storage.");

        db.Execute("CREATE TABLE Decks (Id TEXT PRIMARY KEY NOT NULL, Title TEXT NOT NULL, Description TEXT NOT NULL)");
        db.Execute("CREATE TABLE Cards (Id TEXT PRIMARY KEY NOT NULL, DeckId TEXT NOT NULL REFERENCES Decks(Id) ON DELETE CASCADE, Front TEXT NOT NULL, Back TEXT NOT NULL, Level INTEGER NOT NULL, DueAtTicks INTEGER NOT NULL)");
        db.Execute("CREATE TABLE Reviews (Id TEXT PRIMARY KEY NOT NULL, CardId TEXT NOT NULL REFERENCES Cards(Id) ON DELETE CASCADE, SessionItemId TEXT NOT NULL UNIQUE, Rating INTEGER NOT NULL, ReviewedAtTicks INTEGER NOT NULL)");
        db.Execute("CREATE INDEX CardsByDeck ON Cards(DeckId)");
        db.Execute("CREATE INDEX ReviewsByCard ON Reviews(CardId)");
        db.Execute("PRAGMA user_version = 1");
    }
}

[Table("Decks")]
internal sealed class DeckRow
{
    public DeckRow() { }
    [PrimaryKey] public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
}

[Table("Cards")]
internal sealed class CardRow
{
    public CardRow() { }
    [PrimaryKey] public string Id { get; set; } = "";
    public string DeckId { get; set; } = "";
    public string Front { get; set; } = "";
    public string Back { get; set; } = "";
    public int Level { get; set; }
    public long DueAtTicks { get; set; }
}

[Table("Reviews")]
internal sealed class ReviewRow
{
    public ReviewRow() { }
    [PrimaryKey] public string Id { get; set; } = "";
    public string CardId { get; set; } = "";
    public string SessionItemId { get; set; } = "";
    public int Rating { get; set; }
    public long ReviewedAtTicks { get; set; }
}
