using Bunit;
using RecallDeck.UI.Components;

namespace RecallDeck.Core.Tests;

public sealed class ReviewCardTests : BunitContext
{
    [Fact]
    public void RevealAndRatingsAreControlledByTheSession()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var card = new Card(Guid.NewGuid(), Guid.NewGuid(), "Question", "Answer", new ReviewSchedule(0, DateTimeOffset.UnixEpoch));
        var reveals = 0;
        ReviewRating? rating = null;
        var component = Render<ReviewCard>(p => p.Add(c => c.Card, card)
            .Add(c => c.OnReveal, () => reveals++)
            .Add(c => c.OnRate, value => rating = value));

        Assert.Empty(component.FindAll(".review-answer, #rate-again, #rate-remembered"));
        component.Find("#reveal-answer").Click();
        Assert.Equal(1, reveals);
        Assert.Empty(component.FindAll(".review-answer, #rate-again, #rate-remembered"));

        component.Render(p => p.Add(c => c.Revealed, true));
        component.Find("#rate-again").Click();

        Assert.Equal(ReviewRating.Again, rating);
        Assert.Equal("Answer", component.Find(".review-answer .card-text").TextContent);
        component.Render(p => p.Add(c => c.Busy, true));
        Assert.All(component.FindAll("button"), button => Assert.True(button.HasAttribute("disabled")));
    }
}
