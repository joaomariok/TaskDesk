using Microsoft.UI.Dispatching;

namespace TaskDesk.App.Shell;

/// <summary>Runs work on a window's UI thread and waits for it, like WPF's Dispatcher.Invoke.</summary>
internal static class UiThread
{
    public static void Invoke(DispatcherQueue dispatcher, Action action) => Invoke(dispatcher, () =>
    {
        action();
        return true;
    });

    public static T Invoke<T>(DispatcherQueue dispatcher, Func<T> func)
    {
        if (dispatcher.HasThreadAccess)
        {
            return func();
        }

        var result = default(T)!;
        Exception? failure = null;
        using var done = new ManualResetEventSlim();
        var queued = dispatcher.TryEnqueue(() =>
        {
            try
            {
                result = func();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                done.Set();
            }
        });
        if (!queued)
        {
            throw new InvalidOperationException("The UI thread has shut down.");
        }

        done.Wait();
        if (failure is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(failure);
        }

        return result;
    }
}
