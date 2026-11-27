using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using RecallDeck.UI;
using RecallDeck.Core.Storage;
using RecallDeck.Core.Tests.Storage;

namespace RecallDeck.Core.Tests;

public class NavigationTests : BunitContext
{
    public NavigationTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<IStudyStore, InMemoryStudyStore>();
        Services.AddScoped<DeckService>();
    }

    [Theory]
    [InlineData("/", "Make room for what you learn.", "Home")]
    [InlineData("/decks", "Decks", "Decks")]
    [InlineData("/review", "Review", "Review")]
    [InlineData("/progress", "Progress", "Progress")]
    [InlineData("/backup", "Backup", "Backup")]
    public void EachRouteHasOneSharedShellAndAnHonestState(string path, string heading, string active)
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo(path);
        var page = Render<App>();

        Assert.Equal(heading, page.Find("h1").TextContent);
        Assert.Single(page.FindAll("main#main[tabindex='-1']"));
        Assert.Equal(5, page.FindAll("nav[aria-label='Primary'] a").Count);
        Assert.Equal(active, page.Find("nav a.active").TextContent);

        if (path == "/decks")
        {
            Assert.Contains("No decks yet", page.Find("main").TextContent);
            Assert.Single(page.FindAll("#create-deck"));
            return;
        }

        Assert.Contains("not available yet", page.Find("main").TextContent);
        Assert.Empty(page.FindAll("main input, main button"));
    }

    [Fact]
    public void NavigationUpdatesThePageAndUnknownRoutesKeepTheShell()
    {
        var page = Render<App>();
        var navigation = Services.GetRequiredService<NavigationManager>();

        navigation.NavigateTo("/review");
        Assert.Equal("Review", page.Find("h1").TextContent);
        Assert.Equal("Review", page.Find("nav a.active").TextContent);

        navigation.NavigateTo("/missing");
        Assert.Equal("Page not found", page.Find("h1").TextContent);
        Assert.Equal(5, page.FindAll("nav a").Count);
        Assert.Equal("", page.Find("main a").GetAttribute("href"));
    }

    [Fact]
    public void EventFailureIsReadableWithoutLeakingDetailsAndRetryRestoresContent()
    {
        var page = Render<MainLayout>(p => p.Add(x => x.Body, builder =>
        {
            builder.OpenComponent<FailingAction>(0);
            builder.CloseComponent();
        }));

        page.Find("main button").Click();

        Assert.Contains("could not display this page", page.Find("[role='alert']").TextContent);
        Assert.DoesNotContain("private error detail", page.Markup);
        Assert.Equal(5, page.FindAll("nav a").Count);
        page.Find("main button").Click();
        Assert.Empty(page.FindAll("[role='alert']"));
        Assert.Equal("Trigger failure", page.Find("main button").TextContent);
    }

    [Fact]
    public void ChangingThePageRecoversFromRenderFailureAndEscapesPlainText()
    {
        var page = Render<MainLayout>(p => p.Add(x => x.Body,
            (RenderFragment)(_ => throw new InvalidOperationException("private error detail"))));
        Assert.Single(page.FindAll("[role='alert']"));

        page.Render(p => p.Add(x => x.Body, builder => builder.AddContent(0, "<img src=x onerror=alert(1)>")));

        Assert.Empty(page.FindAll("[role='alert'], main img"));
        Assert.Equal("<img src=x onerror=alert(1)>", page.Find("main").TextContent);
    }

    public class FailingAction : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "button");
            builder.AddAttribute(1, "onclick", EventCallback.Factory.Create<MouseEventArgs>(this,
                _ => throw new InvalidOperationException("private error detail")));
            builder.AddContent(2, "Trigger failure");
            builder.CloseElement();
        }
    }
}
