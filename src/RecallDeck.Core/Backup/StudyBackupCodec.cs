using System.Globalization;
using System.Text;
using System.Text.Json;
using RecallDeck.Core.Storage;

namespace RecallDeck.Core.Backup;

/// <summary>Version 1 learner-data backups; validation never reads or writes storage.</summary>
public static class StudyBackupCodec
{
    public const int MaximumBytes = 10 * 1024 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static byte[] Export(StudySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", 1);
            writer.WriteStartArray("decks");
            for (var i = 0; i < snapshot.Decks.Count; i++)
            {
                var deck = snapshot.Decks[i] ?? throw Invalid($"decks[{i}]", "A row is required.");
                writer.WriteStartObject();
                writer.WriteString("id", deck.Id);
                WriteText(writer, "title", deck.Title, $"decks[{i}].title");
                WriteText(writer, "description", deck.Description, $"decks[{i}].description");
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteStartArray("cards");
            for (var i = 0; i < snapshot.Cards.Count; i++)
            {
                var card = snapshot.Cards[i] ?? throw Invalid($"cards[{i}]", "A row is required.");
                writer.WriteStartObject();
                writer.WriteString("id", card.Id);
                writer.WriteString("deckId", card.DeckId);
                WriteText(writer, "front", card.Front, $"cards[{i}].front");
                WriteText(writer, "back", card.Back, $"cards[{i}].back");
                writer.WriteStartObject("schedule");
                writer.WriteNumber("level", card.Schedule.Level);
                writer.WriteString("dueAt", card.Schedule.DueAt);
                writer.WriteEndObject();
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteStartArray("reviews");
            for (var i = 0; i < snapshot.Reviews.Count; i++)
            {
                var review = snapshot.Reviews[i] ?? throw Invalid($"reviews[{i}]", "A row is required.");
                writer.WriteStartObject();
                writer.WriteString("id", review.Id);
                writer.WriteString("cardId", review.CardId);
                writer.WriteString("sessionItemId", review.SessionItemId);
                writer.WriteNumber("rating", (int)review.Rating);
                writer.WriteString("reviewedAt", review.ReviewedAt);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        var bytes = stream.ToArray();
        // Use the same trust boundary so every successful export is importable.
        ValidateImport(bytes);
        return bytes;
    }

    public static StudySnapshot ValidateImport(ReadOnlyMemory<byte> utf8Json)
    {
        if (utf8Json.Length > MaximumBytes)
            throw Invalid("backup", "Use at most 10 MiB of UTF-8 JSON.");

        try
        {
            StrictUtf8.GetCharCount(utf8Json.Span);
        }
        catch (DecoderFallbackException)
        {
            throw Invalid("backup", "Valid UTF-8 is required.");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(utf8Json, new JsonDocumentOptions
            {
                AllowDuplicateProperties = false,
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 16
            });
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException)
        {
            throw Invalid("backup", "Malformed JSON, duplicate properties or excessive nesting.");
        }

        using (document)
        {
            var root = document.RootElement;
            Object(root, "backup", "schemaVersion", "decks", "cards", "reviews");
            Integer(root.GetProperty("schemaVersion"), "schemaVersion", 1, 1);

            var decks = new List<Deck>();
            var cards = new List<Card>();
            var reviews = new List<ReviewEvent>();
            var deckIds = new HashSet<Guid>();
            var cardIds = new HashSet<Guid>();
            var reviewIds = new HashSet<Guid>();
            var sessionItemIds = new HashSet<Guid>();

            foreach (var (row, path) in Rows(root.GetProperty("decks"), "decks"))
            {
                Object(row, path, "id", "title", "description");
                var id = Identity(row, "id", path);
                Unique(deckIds, id, path + ".id");
                decks.Add(new Deck(id, Text(row, "title", path, 80, trim: true), Text(row, "description", path, 500, trim: false)));
            }
            foreach (var (row, path) in Rows(root.GetProperty("cards"), "cards"))
            {
                Object(row, path, "id", "deckId", "front", "back", "schedule");
                var id = Identity(row, "id", path);
                Unique(cardIds, id, path + ".id");
                var deckId = Identity(row, "deckId", path);
                Reference(deckIds, deckId, path + ".deckId");
                var schedule = row.GetProperty("schedule");
                Object(schedule, path + ".schedule", "level", "dueAt");
                var level = Integer(schedule.GetProperty("level"), path + ".schedule.level", 0, 5);
                var dueAt = UtcInstant(schedule.GetProperty("dueAt"), path + ".schedule.dueAt");
                cards.Add(new Card(id, deckId, Text(row, "front", path, 4000, trim: true), Text(row, "back", path, 4000, trim: true), new ReviewSchedule(level, dueAt)));
            }
            foreach (var (row, path) in Rows(root.GetProperty("reviews"), "reviews"))
            {
                Object(row, path, "id", "cardId", "sessionItemId", "rating", "reviewedAt");
                var id = Identity(row, "id", path);
                Unique(reviewIds, id, path + ".id");
                var cardId = Identity(row, "cardId", path);
                Reference(cardIds, cardId, path + ".cardId");
                var sessionItemId = Identity(row, "sessionItemId", path);
                Unique(sessionItemIds, sessionItemId, path + ".sessionItemId");
                var rating = Integer(row.GetProperty("rating"), path + ".rating", 0, 1);
                var reviewedAt = UtcInstant(row.GetProperty("reviewedAt"), path + ".reviewedAt");
                reviews.Add(new ReviewEvent(id, cardId, sessionItemId, (ReviewRating)rating, reviewedAt));
            }
            return new StudySnapshot(decks, cards, reviews);
        }
    }

    private static void Object(JsonElement value, string path, params string[] fields)
    {
        if (value.ValueKind != JsonValueKind.Object)
            throw Invalid(path, "An object is required.");
        foreach (var property in value.EnumerateObject())
            if (!fields.Contains(property.Name, StringComparer.Ordinal))
                throw Invalid(path, "An unknown field is present.");
        foreach (var field in fields)
            if (!value.TryGetProperty(field, out var member) || member.ValueKind == JsonValueKind.Null)
                throw Invalid(path + "." + field, "A value is required.");
    }

    private static IEnumerable<(JsonElement Row, string Path)> Rows(JsonElement value, string path)
    {
        if (value.ValueKind != JsonValueKind.Array)
            throw Invalid(path, "An array is required.");
        var index = 0;
        foreach (var row in value.EnumerateArray())
            yield return (row, $"{path}[{index++}]");
    }

    private static Guid Identity(JsonElement row, string field, string path)
    {
        var value = row.GetProperty(field);
        if (!Guid.TryParseExact(RequiredString(value, path + "." + field), "D", out var id) || id == Guid.Empty)
            throw Invalid(path + "." + field, "A nonempty UUID is required.");
        return id;
    }

    private static string Text(JsonElement row, string field, string path, int maximum, bool trim)
    {
        var value = row.GetProperty(field);
        var text = RequiredString(value, path + "." + field);
        if (trim) text = text.Trim();
        if ((trim && text.Length == 0) || text.Length > maximum)
            throw Invalid(path + "." + field, $"Use {(trim ? "1" : "0")} to {maximum} characters.");
        return text;
    }

    private static int Integer(JsonElement value, string path, int minimum, int maximum)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var number) || number < minimum || number > maximum)
            throw Invalid(path, $"Use an integer from {minimum} to {maximum}.");
        return number;
    }

    private static DateTimeOffset UtcInstant(JsonElement value, string path)
    {
        var text = RequiredString(value, path);
        var local = text.EndsWith('Z') ? text[..^1] : text.EndsWith("+00:00", StringComparison.Ordinal) ? text[..^6] : "";
        var format = local.Length == 19 ? "yyyy-MM-dd'T'HH:mm:ss" : "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF";
        if ((local.Length != 19 && (local.Length < 21 || local.Length > 27 || local[19] != '.' || local.AsSpan(20).ContainsAnyExceptInRange('0', '9'))) ||
            !DateTime.TryParseExact(local, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var instant))
            throw Invalid(path, "An ISO UTC instant with Z or +00:00 and at most 7 fractional digits is required.");
        return new DateTimeOffset(DateTime.SpecifyKind(instant, DateTimeKind.Utc));
    }

    private static string RequiredString(JsonElement value, string path)
    {
        if (value.ValueKind != JsonValueKind.String)
            throw Invalid(path, "Text is required.");
        try
        {
            return value.GetString()!;
        }
        catch (InvalidOperationException)
        {
            throw Invalid(path, "Valid Unicode text is required.");
        }
    }

    private static void Unique(HashSet<Guid> ids, Guid id, string path)
    {
        if (!ids.Add(id)) throw Invalid(path, "Duplicate UUID.");
    }

    private static void Reference(HashSet<Guid> ids, Guid id, string path)
    {
        if (!ids.Contains(id)) throw Invalid(path, "The referenced row does not exist.");
    }

    private static void WriteText(Utf8JsonWriter writer, string field, string text, string path)
    {
        try
        {
            StrictUtf8.GetByteCount(text);
        }
        catch (EncoderFallbackException)
        {
            throw Invalid(path, "Valid Unicode text is required.");
        }
        writer.WriteString(field, text);
    }

    private static ArgumentException Invalid(string path, string reason) => new($"{path}: {reason}");
}
