using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;

namespace RecallDeck.UI;

public sealed class AppNavigation : IDisposable
{
    private readonly NavigationManager navigation;
    private readonly List<string> routes = [];

    public AppNavigation(NavigationManager navigation)
    {
        this.navigation = navigation;
        routes.Add(navigation.BaseUri);
        Record(navigation.Uri);
        navigation.LocationChanged += LocationChanged;
    }

    public bool GoBack()
    {
        if (routes.Count < 2) return false;

        navigation.NavigateTo(routes[^2]);
        return true;
    }

    private void LocationChanged(object? sender, LocationChangedEventArgs args) => Record(args.Location);

    private void Record(string location)
    {
        var previous = routes.IndexOf(location);
        if (previous >= 0) routes.RemoveRange(previous + 1, routes.Count - previous - 1);
        else routes.Add(location);
    }

    public void Dispose() => navigation.LocationChanged -= LocationChanged;
}
