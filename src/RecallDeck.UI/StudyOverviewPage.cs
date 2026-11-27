using System.Globalization;
using Microsoft.AspNetCore.Components;
using RecallDeck.Core;

namespace RecallDeck.UI;

public abstract class StudyOverviewPage : ComponentBase, IDisposable
{
    [Inject] protected StudyOverviewService OverviewService { get; set; } = default!;
    [Inject] protected AppLifecycle Lifecycle { get; set; } = default!;
    [Inject] protected TimeProvider Clock { get; set; } = default!;

    protected StudyOverview? Overview { get; private set; }
    protected bool Loading { get; private set; } = true;
    protected bool LoadFailed { get; private set; }
    private Task? pending;
    private bool disposed;

    protected override Task OnInitializedAsync()
    {
        Lifecycle.Resumed += ResumeAsync;
        return RefreshAsync();
    }

    protected Task RefreshAsync()
    {
        if (disposed) return Task.CompletedTask;
        return pending is { IsCompleted: false } ? pending : pending = ReadAsync();
    }

    private async Task ReadAsync()
    {
        Loading = true;
        LoadFailed = false;
        Overview = null;
        StateHasChanged();
        try
        {
            var captured = await OverviewService.ReadAsync();
            if (!disposed) Overview = captured;
        }
        catch (Exception)
        {
            if (!disposed) LoadFailed = true;
        }
        finally
        {
            if (!disposed)
            {
                Loading = false;
                StateHasChanged();
            }
        }
    }

    private Task ResumeAsync() => InvokeAsync(async () =>
    {
        if (!disposed) await RefreshAsync();
    });

    protected string LocalDate(DateTimeOffset instant) => TimeZoneInfo.ConvertTime(instant, Clock.LocalTimeZone)
        .ToString("dd MMM yyyy, HH:mm '(UTC'zzz')'", CultureInfo.InvariantCulture);

    public void Dispose()
    {
        disposed = true;
        Lifecycle.Resumed -= ResumeAsync;
    }
}
