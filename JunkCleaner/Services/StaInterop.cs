using System.Windows.Threading;

namespace JunkCleaner.Services;

/// <summary>Routes Shell32-style calls that behave best on WPF&apos;s STA thread.</summary>
internal static class StaInterop
{
    internal static Task<TResult> InvokeStaAsync<TResult>(Func<TResult> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null)
            return Task.Run(callback);

        return dispatcher.InvokeAsync(callback, DispatcherPriority.Normal).Task;
    }

    internal static Task InvokeStaAsync(Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return Task.Run(callback);
        }

        return dispatcher.InvokeAsync(callback, DispatcherPriority.Normal).Task;
    }
}
