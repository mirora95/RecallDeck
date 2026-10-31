namespace RecallDeck.Core;

public sealed record Card
{
    public Card(Guid id, Guid deckId, string front, string back, ReviewSchedule schedule)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("A card ID is required.", nameof(id));

        if (deckId == Guid.Empty)
            throw new ArgumentException("A deck ID is required.", nameof(deckId));

        ArgumentNullException.ThrowIfNull(schedule);

        Front = PlainText.Required(front, 4000, nameof(front));
        Back = PlainText.Required(back, 4000, nameof(back));
        Id = id;
        DeckId = deckId;
        Schedule = schedule;
    }

    public Guid Id { get; }
    public Guid DeckId { get; }
    public string Front { get; }
    public string Back { get; }
    public ReviewSchedule Schedule { get; }
}
