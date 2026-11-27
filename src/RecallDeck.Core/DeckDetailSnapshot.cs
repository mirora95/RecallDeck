namespace RecallDeck.Core;

public sealed record DeckDetailSnapshot(Deck Deck, IReadOnlyList<Card> Cards);
