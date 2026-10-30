using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RecallDeck.UI.Pages;

namespace RecallDeck.Core.Tests;

public class LandingTests
{
    [Fact]
    public async Task LandingExplainsCurrentStateWithoutPromisingAvailableFeatures()
    {
        await using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());

        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var component = await renderer.RenderComponentAsync<Home>();
            return component.ToHtmlString();
        });

        Assert.Contains("<h1 id=\"welcome\">Make room for what you learn.</h1>", html);
        Assert.Contains("Decks and reviews are not available yet", html);
        Assert.Contains("id=\"release-plan\"", html);
        Assert.DoesNotContain("<button", html);
    }
}
