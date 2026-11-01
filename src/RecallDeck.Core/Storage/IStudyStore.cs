namespace RecallDeck.Core.Storage;

public interface IStudyStore
{
    Task<StudySnapshot> ReadSnapshotAsync(CancellationToken cancellationToken = default);
    Task CreateDeckAsync(Deck deck, CancellationToken cancellationToken = default);
    Task UpdateDeckAsync(Deck deck, CancellationToken cancellationToken = default);
    Task DeleteDeckAsync(Guid id, CancellationToken cancellationToken = default);
    Task CreateCardAsync(Card card, CancellationToken cancellationToken = default);
    Task UpdateCardAsync(Card card, CancellationToken cancellationToken = default);
    Task DeleteCardAsync(Guid id, CancellationToken cancellationToken = default);
    Task ApplyReviewAsync(ReviewEvent review, ReviewSchedule schedule, CancellationToken cancellationToken = default);
}
