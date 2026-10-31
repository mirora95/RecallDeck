namespace RecallDeck.Core;

public sealed record Deck
{
    public Deck(Guid id, string title, string? description = null)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("A deck ID is required.", nameof(id));

        Title = PlainText.Required(title, 80, nameof(title));

        if (description?.Length > 500)
            throw new ArgumentException("Use at most 500 characters.", nameof(description));

        Id = id;
        Description = description ?? string.Empty;
    }

    public Guid Id { get; }
    public string Title { get; }
    public string Description { get; }
}
