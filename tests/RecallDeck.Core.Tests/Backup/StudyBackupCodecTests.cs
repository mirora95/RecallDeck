using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using RecallDeck.Core.Backup;
using RecallDeck.Core.Storage;

namespace RecallDeck.Core.Tests.Backup;

public class StudyBackupCodecTests
{
    private const string EmptyJson = "{\"schemaVersion\":1,\"decks\":[],\"cards\":[],\"reviews\":[]}";
    private static readonly Guid DeckId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid CardId = Guid.Parse("00000000-0000-0000-0000-000000000002");

    [Fact]
    public void EmptySnapshotHasExactVersionOneEnvelopeAndRoundTrips()
    {
        var bytes = StudyBackupCodec.Export(new StudySnapshot([], [], []));

        Assert.Equal(EmptyJson, Encoding.UTF8.GetString(bytes));
        var result = StudyBackupCodec.ValidateImport(bytes);
        Assert.Empty(result.Decks);
        Assert.Empty(result.Cards);
        Assert.Empty(result.Reviews);
    }

    [Fact]
    public void PopulatedRoundTripPreservesAllFieldsLevelsRatingsUnicodeAndUtcTicks()
    {
        var original = Populated();
        var bytes = StudyBackupCodec.Export(original);

        var result = StudyBackupCodec.ValidateImport(bytes);

        Assert.NotSame(original, result);
        Assert.Equal(original.Decks, result.Decks);
        Assert.Equal(original.Cards, result.Cards);
        Assert.Equal(original.Reviews, result.Reviews);
        Assert.NotSame(original.Decks[0], result.Decks[0]);
        Assert.NotSame(original.Cards[0].Schedule, result.Cards[0].Schedule);
        Assert.All(result.Cards, c => Assert.Equal(TimeSpan.Zero, c.Schedule.DueAt.Offset));
        Assert.All(result.Reviews, r => Assert.Equal(TimeSpan.Zero, r.ReviewedAt.Offset));
        Assert.Throws<NotSupportedException>(() => ((IList<Deck>)result.Decks).Clear());
        using var document = JsonDocument.Parse(bytes);
        AssertFields(document.RootElement, "schemaVersion", "decks", "cards", "reviews");
        AssertFields(document.RootElement.GetProperty("decks")[0], "id", "title", "description");
        AssertFields(document.RootElement.GetProperty("cards")[0], "id", "deckId", "front", "back", "schedule");
        AssertFields(document.RootElement.GetProperty("cards")[0].GetProperty("schedule"), "level", "dueAt");
        AssertFields(document.RootElement.GetProperty("reviews")[0], "id", "cardId", "sessionItemId", "rating", "reviewedAt");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"schemaVersion\":1,\"decks\":null,\"cards\":[],\"reviews\":[]}")]
    [InlineData("{\"schemaVersion\":2,\"decks\":[],\"cards\":[],\"reviews\":[]}")]
    [InlineData("{\"schemaVersion\":\"1\",\"decks\":[],\"cards\":[],\"reviews\":[]}")]
    [InlineData("{\"schemaVersion\":1.0,\"decks\":[],\"cards\":[],\"reviews\":[]}")]
    [InlineData("{\"schemaVersion\":1,\"decks\":[],\"cards\":[],\"reviews\":[],\"privatePath\":\"secret\"}")]
    [InlineData("{\"schemaVersion\":1,\"schemaVersion\":1,\"decks\":[],\"cards\":[],\"reviews\":[]}")]
    [InlineData("{\"schemaVersion\":1,\"decks\":[],\"cards\":[],\"reviews\":[],}")]
    [InlineData("/* comment */{\"schemaVersion\":1,\"decks\":[],\"cards\":[],\"reviews\":[]}")]
    public void InvalidEnvelopeIsRejected(string json)
    {
        Assert.Throws<ArgumentException>(() => StudyBackupCodec.ValidateImport(Encoding.UTF8.GetBytes(json)));
    }

    [Fact]
    public void InvalidUtf8AndExcessiveDepthAreRejected()
    {
        var invalidUtf8 = Encoding.UTF8.GetBytes(ValidNode().ToJsonString());
        var titleStart = Encoding.UTF8.GetString(invalidUtf8).IndexOf("Deck", StringComparison.Ordinal);
        invalidUtf8[titleStart] = 0xff;

        Assert.Contains("UTF-8", Assert.Throws<ArgumentException>(() => StudyBackupCodec.ValidateImport(invalidUtf8)).Message);
        Assert.Throws<ArgumentException>(() => StudyBackupCodec.ValidateImport(Encoding.UTF8.GetBytes(new string('[', 30) + new string(']', 30))));
    }

    [Fact]
    public void SizeLimitCountsUtf8BytesAndAcceptsExactLimit()
    {
        Assert.Equal(10 * 1024 * 1024, StudyBackupCodec.MaximumBytes);
        var exact = Encoding.UTF8.GetBytes(EmptyJson.PadRight(StudyBackupCodec.MaximumBytes));

        Assert.Empty(StudyBackupCodec.ValidateImport(exact).Decks);
        Assert.Throws<ArgumentException>(() => StudyBackupCodec.ValidateImport((byte[])[..exact, (byte)' ']));
        var multiByteJson = JsonNode.Parse(EmptyJson)!.AsObject();
        multiByteJson["decks"] = new JsonArray(new JsonObject { ["id"] = DeckId.ToString(), ["title"] = "字", ["description"] = "" });
        var json = multiByteJson.ToJsonString(new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        json = json.PadRight(StudyBackupCodec.MaximumBytes);
        Assert.Equal(StudyBackupCodec.MaximumBytes, json.Length);
        Assert.True(Encoding.UTF8.GetByteCount(json) > StudyBackupCodec.MaximumBytes);
        Assert.Throws<ArgumentException>(() => StudyBackupCodec.ValidateImport(Encoding.UTF8.GetBytes(json)));
    }

    public static IEnumerable<object[]> MissingAndNullMembers()
    {
        foreach (var collection in new[] { "schemaVersion", "decks", "cards", "reviews" })
        {
            yield return [collection, "", false];
            yield return [collection, "", true];
        }
        foreach (var (path, fields) in new[] {
            ("decks[0]", new[] { "id", "title", "description" }),
            ("cards[0]", new[] { "id", "deckId", "front", "back", "schedule" }),
            ("cards[0].schedule", new[] { "level", "dueAt" }),
            ("reviews[0]", new[] { "id", "cardId", "sessionItemId", "rating", "reviewedAt" }) })
        {
            yield return [path, "", true];
            foreach (var field in fields)
            {
                yield return [path, field, false];
                yield return [path, field, true];
            }
        }
    }

    [Theory]
    [MemberData(nameof(MissingAndNullMembers))]
    public void EveryRequiredCollectionRowAndMemberIsValidated(string path, string field, bool useNull)
    {
        var json = ValidNode();
        if (field == "")
        {
            if (path.Contains('['))
            {
                if (path.EndsWith("schedule")) json["cards"]![0]!["schedule"] = null;
                else json[path.Split('[')[0]]![0] = null;
            }
            else if (useNull) json[path] = null;
            else json.Remove(path);
        }
        else
        {
            var row = At(json, path);
            if (useNull) row[field] = null;
            else row.Remove(field);
        }

        var error = Assert.Throws<ArgumentException>(() => Import(json));
        Assert.Contains(path.Split('[')[0], error.Message);
    }

    public static IEnumerable<object[]> InvalidFields()
    {
        foreach (var field in new[] { "id", "title", "description" })
            yield return ["decks[0]", field, "[]"];
        foreach (var (path, field) in new[] { ("decks[0]", "id"), ("cards[0]", "id"), ("cards[0]", "deckId"), ("reviews[0]", "id"), ("reviews[0]", "cardId"), ("reviews[0]", "sessionItemId") })
            foreach (var value in new[] { "not-a-guid", Guid.Empty.ToString() })
                yield return [path, field, JsonSerializer.Serialize(value)];
        foreach (var value in new[] { "-1", "6", "1.5", "\"1\"", "2147483648" })
            yield return ["cards[0].schedule", "level", value];
        foreach (var value in new[] { "-1", "2", "0.5", "\"Again\"", "1e0" })
            yield return ["reviews[0]", "rating", value];
        foreach (var (path, field, max) in new[] { ("decks[0]", "title", 80), ("decks[0]", "description", 500), ("cards[0]", "front", 4000), ("cards[0]", "back", 4000) })
        {
            yield return [path, field, JsonSerializer.Serialize(new string('x', max + 1))];
            if (field != "description") yield return [path, field, "\" \\t\\n \""];
        }
    }

    [Theory]
    [MemberData(nameof(InvalidFields))]
    public void InvalidDomainFieldsAreRejectedWithContextWithoutEchoingValues(string path, string field, string value)
    {
        var json = ValidNode();
        At(json, path)[field] = JsonNode.Parse(value);

        var error = Assert.Throws<ArgumentException>(() => Import(json));
        Assert.Contains(path + "." + field, error.Message);
        Assert.DoesNotContain("not-a-guid", error.Message);
        Assert.DoesNotContain(new string('x', 81), error.Message);
    }

    [Theory]
    [InlineData("2026-01-01T01:02:03")]
    [InlineData("2026-01-01T01:02:03+01:00")]
    [InlineData("2026-01-01T01:02:03-01:00")]
    [InlineData("2026-01-01T01:02:03-00:00")]
    [InlineData("2026-02-30T01:02:03Z")]
    [InlineData("2026-01-01")]
    [InlineData("2026-01-01T01:02:03.12345678Z")]
    [InlineData("")]
    public void DatesRequireValidExplicitUtcWithoutPrecisionLoss(string value)
    {
        foreach (var (path, field) in new[] { ("cards[0].schedule", "dueAt"), ("reviews[0]", "reviewedAt") })
        {
            var json = ValidNode();
            At(json, path)[field] = value;

            Assert.Contains(path + "." + field, Assert.Throws<ArgumentException>(() => Import(json)).Message);
        }
    }

    [Theory]
    [InlineData("0001-01-01T00:00:00Z", 0L)]
    [InlineData("9999-12-31T23:59:59.9999999+00:00", 3155378975999999999L)]
    [InlineData("2026-01-01T01:02:03.1234567Z", 639028261231234567L)]
    public void UtcDatesPreserveExactTicksIncludingBoundaryDates(string value, long ticks)
    {
        var json = ValidNode();
        At(json, "cards[0].schedule")["dueAt"] = value;
        At(json, "reviews[0]")["reviewedAt"] = value;

        var result = Import(json);

        Assert.Equal(ticks, result.Cards[0].Schedule.DueAt.Ticks);
        Assert.Equal(ticks, result.Reviews[0].ReviewedAt.Ticks);
    }

    [Fact]
    public void TextBoundariesUseExistingTrimRulesAndPreserveDescriptionWhitespace()
    {
        var json = ValidNode();
        At(json, "decks[0]")["title"] = "  " + new string('字', 80) + "\n";
        At(json, "decks[0]")["description"] = new string(' ', 500);
        At(json, "cards[0]")["front"] = "\t" + new string('字', 4000) + "  ";
        At(json, "cards[0]")["back"] = " " + string.Concat(Enumerable.Repeat("😀", 2000)) + " ";

        var result = Import(json);

        Assert.Equal(new string('字', 80), result.Decks[0].Title);
        Assert.Equal(new string(' ', 500), result.Decks[0].Description);
        Assert.Equal(new string('字', 4000), result.Cards[0].Front);
        Assert.Equal(4000, result.Cards[0].Back.Length);
    }

    [Theory]
    [InlineData("decks[0]", "title")]
    [InlineData("cards[0]", "front")]
    [InlineData("decks[0]", "id")]
    [InlineData("cards[0].schedule", "dueAt")]
    [InlineData("reviews[0]", "reviewedAt")]
    public void EscapedUnpairedSurrogatesAreRejectedAsValidationErrors(string path, string field)
    {
        var json = ValidNode();
        At(json, path)[field] = "surrogate-placeholder";
        var text = json.ToJsonString().Replace("surrogate-placeholder", "\\ud800", StringComparison.Ordinal);

        Assert.Throws<ArgumentException>(() => StudyBackupCodec.ValidateImport(Encoding.UTF8.GetBytes(text)));
    }

    [Fact]
    public void EscapedUnpairedSurrogatePropertyNameIsRejectedAsValidationError()
    {
        var text = EmptyJson.Insert(1, "\"\\ud800\":0,");

        Assert.Throws<ArgumentException>(() => StudyBackupCodec.ValidateImport(Encoding.UTF8.GetBytes(text)));
    }

    [Theory]
    [InlineData("decks")]
    [InlineData("cards")]
    [InlineData("reviews")]
    public void DuplicateEntityIdsAreRejected(string collection)
    {
        var json = ValidNode();
        json[collection]![1]!["id"] = json[collection]![0]!["id"]!.DeepClone();

        Assert.Contains(collection + "[1].id", Assert.Throws<ArgumentException>(() => Import(json)).Message);
    }

    [Fact]
    public void DuplicateSessionItemsAndDanglingReferencesAreRejected()
    {
        var json = ValidNode();
        json["reviews"]![1]!["sessionItemId"] = json["reviews"]![0]!["sessionItemId"]!.DeepClone();
        Assert.Contains("reviews[1].sessionItemId", Assert.Throws<ArgumentException>(() => Import(json)).Message);
        json = ValidNode();
        json["cards"]![0]!["deckId"] = Guid.NewGuid().ToString();
        Assert.Contains("cards[0].deckId", Assert.Throws<ArgumentException>(() => Import(json)).Message);
        json = ValidNode();
        json["reviews"]![0]!["cardId"] = Guid.NewGuid().ToString();
        Assert.Contains("reviews[0].cardId", Assert.Throws<ArgumentException>(() => Import(json)).Message);
    }

    [Theory]
    [InlineData("decks[0]")]
    [InlineData("cards[0]")]
    [InlineData("cards[0].schedule")]
    [InlineData("reviews[0]")]
    public void UnknownAndDuplicateNestedPropertiesAreRejected(string path)
    {
        var json = ValidNode();
        At(json, path)["unexpectedSecret"] = "private learner text";
        var error = Assert.Throws<ArgumentException>(() => Import(json));
        Assert.Contains(path, error.Message);
        Assert.DoesNotContain("private learner text", error.Message);
        json = ValidNode();
        var row = At(json, path);
        var first = row.First();
        var serializedRow = row.ToJsonString();
        var duplicate = serializedRow.Insert(1, JsonSerializer.Serialize(first.Key) + ":" + first.Value!.ToJsonString() + ",");
        var serialized = json.ToJsonString().Replace(serializedRow, duplicate, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => StudyBackupCodec.ValidateImport(Encoding.UTF8.GetBytes(serialized)));
    }

    [Fact]
    public void ExportRejectsInconsistentSnapshotsAndOversizeInsteadOfProducingInvalidBackups()
    {
        var source = Populated();
        Assert.Throws<ArgumentException>(() => StudyBackupCodec.Export(new StudySnapshot([], source.Cards, source.Reviews)));
        Assert.Throws<ArgumentException>(() => StudyBackupCodec.Export(new StudySnapshot(source.Decks, [], source.Reviews)));
        Assert.Throws<ArgumentException>(() => StudyBackupCodec.Export(new StudySnapshot([source.Decks[0], source.Decks[0]], [], [])));
        Assert.Throws<ArgumentException>(() => StudyBackupCodec.Export(new StudySnapshot([null!], [], [])));
        Assert.Throws<ArgumentException>(() => StudyBackupCodec.Export(new StudySnapshot(source.Decks, [null!], [])));
        Assert.Throws<ArgumentException>(() => StudyBackupCodec.Export(new StudySnapshot(source.Decks, source.Cards, [null!])));
        Assert.Throws<ArgumentException>(() => StudyBackupCodec.Export(new StudySnapshot([new Deck(DeckId, "\ud800")], [], [])));
        var bigCards = Enumerable.Range(0, 1500).Select(_ => new Card(Guid.NewGuid(), DeckId, new string('a', 4000), new string('b', 4000), new ReviewSchedule(0, DateTimeOffset.UnixEpoch)));
        Assert.Throws<ArgumentException>(() => StudyBackupCodec.Export(new StudySnapshot([new Deck(DeckId, "Deck")], bigCards, [])));
    }

    [Fact]
    public void SuccessAndFailureLeaveInputsAndCurrentSnapshotUnchanged()
    {
        var current = Populated();
        var before = StudyBackupCodec.Export(current);
        var bytes = (byte[])before.Clone();

        StudyBackupCodec.ValidateImport(bytes);
        Assert.Equal(before, bytes);
        bytes[0] = (byte)'!';
        var invalidBefore = (byte[])bytes.Clone();
        Assert.Throws<ArgumentException>(() => StudyBackupCodec.ValidateImport(bytes));
        Assert.Equal(invalidBefore, bytes);
        Assert.Equal(before, StudyBackupCodec.Export(current));
    }

    private static StudySnapshot Populated()
    {
        var dates = new[] { DateTimeOffset.MinValue, DateTimeOffset.MaxValue, DateTimeOffset.UnixEpoch.AddTicks(1234567) };
        var decks = new[] { new Deck(DeckId, "O‘zbek\t 日本語 😀", "  notes\nunchanged  "), new Deck(Guid.NewGuid(), "Other") };
        var cards = Enumerable.Range(0, 6).Select(level => new Card(level == 0 ? CardId : Guid.NewGuid(), decks[level % 2].Id, "question\n 字 e\u0301 😀", "<answer>\t literal", new ReviewSchedule(level, dates[level % 3]))).ToArray();
        var reviews = cards.Select((card, i) => new ReviewEvent(Guid.NewGuid(), card.Id, Guid.NewGuid(), (ReviewRating)(i % 2), dates[(i + 1) % 3])).ToArray();
        return new StudySnapshot(decks, cards, reviews);
    }

    private static JsonObject ValidNode()
    {
        // Test fixtures use explicit JSON, independent of the exporter under test.
        JsonObject DeckNode(Guid id) => new() { ["id"] = id.ToString(), ["title"] = "Deck", ["description"] = "" };
        JsonObject CardNode(Guid id) => new() { ["id"] = id.ToString(), ["deckId"] = DeckId.ToString(), ["front"] = "front", ["back"] = "back", ["schedule"] = new JsonObject { ["level"] = 0, ["dueAt"] = "2026-01-01T00:00:00Z" } };
        JsonObject ReviewNode() => new() { ["id"] = Guid.NewGuid().ToString(), ["cardId"] = CardId.ToString(), ["sessionItemId"] = Guid.NewGuid().ToString(), ["rating"] = 0, ["reviewedAt"] = "2026-01-01T00:00:00Z" };
        return new JsonObject { ["schemaVersion"] = 1, ["decks"] = new JsonArray(DeckNode(DeckId), DeckNode(Guid.NewGuid())), ["cards"] = new JsonArray(CardNode(CardId), CardNode(Guid.NewGuid())), ["reviews"] = new JsonArray(ReviewNode(), ReviewNode()) };
    }

    private static JsonObject At(JsonObject root, string path) => path switch
    {
        "decks[0]" => root["decks"]![0]!.AsObject(),
        "cards[0]" => root["cards"]![0]!.AsObject(),
        "cards[0].schedule" => root["cards"]![0]!["schedule"]!.AsObject(),
        "reviews[0]" => root["reviews"]![0]!.AsObject(),
        _ => throw new ArgumentException(nameof(path))
    };

    private static StudySnapshot Import(JsonObject json) => StudyBackupCodec.ValidateImport(Encoding.UTF8.GetBytes(json.ToJsonString()));
    private static void AssertFields(JsonElement element, params string[] names) => Assert.Equal(names.Order(), element.EnumerateObject().Select(p => p.Name).Order());
}
