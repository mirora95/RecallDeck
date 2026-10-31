using RecallDeck.Core;

namespace RecallDeck.Core.Tests;

public class ModelTests
{
    private static readonly Guid DeckId = Guid.Parse("01e3e3ab-8011-414a-998d-8e30d80c9525");
    private static readonly Guid CardId = Guid.Parse("84d686ed-8f28-4dd9-9d30-a12ee877f64b");
    private static readonly ReviewSchedule Schedule = new(2, DateTimeOffset.UnixEpoch);

    [Fact]
    public void ModelsTrimRequiredTextButPreserveUnicodeInternalWhitespaceAndLiteralMarkup()
    {
        var deck = new Deck(DeckId, "  O‘zbek\t 日本語 😀  ", "  Notes\nunchanged  ");
        var card = new Card(CardId, deck.Id, " \t<script>salom</script>\n savol  ", "  e\u0301 😀\tjavob\n  ", Schedule);

        Assert.Equal("O‘zbek\t 日本語 😀", deck.Title);
        Assert.Equal("  Notes\nunchanged  ", deck.Description);
        Assert.Equal("<script>salom</script>\n savol", card.Front);
        Assert.Equal("e\u0301 😀\tjavob", card.Back);
        Assert.Equal(DeckId, card.DeckId);
        Assert.Equal(CardId, card.Id);
        Assert.Same(Schedule, card.Schedule);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t\n\u2003 ")]
    public void RequiredFieldsRejectMissingTextWithFieldNames(string? text)
    {
        Assert.Equal("title", Assert.ThrowsAny<ArgumentException>(() => new Deck(DeckId, text!)).ParamName);
        Assert.Equal("front", Assert.ThrowsAny<ArgumentException>(() => new Card(CardId, DeckId, text!, "answer", Schedule)).ParamName);
        Assert.Equal("back", Assert.ThrowsAny<ArgumentException>(() => new Card(CardId, DeckId, "question", text!, Schedule)).ParamName);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(80)]
    public void TitleAcceptsBoundariesAfterTrimming(int length)
    {
        var title = new string('字', length);

        Assert.Equal(title, new Deck(DeckId, $"  {title}  ").Title);
    }

    [Fact]
    public void TitleAndDescriptionRejectOverLimitAndDescriptionAllowsExactLimit()
    {
        Assert.Equal("title", Assert.Throws<ArgumentException>(() => new Deck(DeckId, new string('a', 81))).ParamName);
        Assert.Equal("description", Assert.Throws<ArgumentException>(() => new Deck(DeckId, "Title", new string('a', 501))).ParamName);
        Assert.Equal(new string('字', 500), new Deck(DeckId, "Title", new string('字', 500)).Description);
        Assert.Equal(string.Empty, new Deck(DeckId, "Title").Description);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4000)]
    public void BothCardSidesAcceptBoundariesAfterTrimming(int length)
    {
        var text = new string('字', length);
        var card = new Card(CardId, DeckId, $"  {text}  ", $"\n{text}\t", Schedule);

        Assert.Equal(text, card.Front);
        Assert.Equal(text, card.Back);
    }

    [Fact]
    public void CardSidesRejectOverLimitWithoutTruncating()
    {
        var tooLong = new string('a', 4001);

        Assert.Equal("front", Assert.Throws<ArgumentException>(() => new Card(CardId, DeckId, tooLong, "answer", Schedule)).ParamName);
        Assert.Equal("back", Assert.Throws<ArgumentException>(() => new Card(CardId, DeckId, "question", tooLong, Schedule)).ParamName);
    }

    [Fact]
    public void SupplementaryUnicodeIsPreservedAndBoundsUseUtf16CodeUnits()
    {
        var text = string.Concat(Enumerable.Repeat("😀", 2000));

        Assert.Equal(text, new Card(CardId, DeckId, text, text, Schedule).Front);
        Assert.Throws<ArgumentException>(() => new Card(CardId, DeckId, text + "a", "answer", Schedule));
    }

    [Fact]
    public void EmptyIdentitiesAndMissingScheduleAreRejected()
    {
        Assert.Equal("id", Assert.Throws<ArgumentException>(() => new Deck(Guid.Empty, "Title")).ParamName);
        Assert.Equal("id", Assert.Throws<ArgumentException>(() => new Card(Guid.Empty, DeckId, "front", "back", Schedule)).ParamName);
        Assert.Equal("deckId", Assert.Throws<ArgumentException>(() => new Card(CardId, Guid.Empty, "front", "back", Schedule)).ParamName);
        Assert.Throws<ArgumentNullException>(() => new Card(CardId, DeckId, "front", "back", null!));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(6)]
    [InlineData(int.MaxValue)]
    public void InvalidScheduleLevelsAreRejected(int level)
    {
        Assert.Equal("level", Assert.Throws<ArgumentOutOfRangeException>(() => new ReviewSchedule(level, DateTimeOffset.UnixEpoch)).ParamName);
    }

    [Fact]
    public void RestoredIdsAndScheduleRemainStableWhenConstructingEditedContent()
    {
        var original = new Card(CardId, DeckId, "old", "answer", Schedule);
        var edited = new Card(original.Id, original.DeckId, "new", original.Back, original.Schedule);

        Assert.Equal(CardId, edited.Id);
        Assert.Equal(DeckId, edited.DeckId);
        Assert.Equal(Schedule, edited.Schedule);
        Assert.Equal("old", original.Front);
    }
}
