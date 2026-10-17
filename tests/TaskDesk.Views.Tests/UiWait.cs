namespace TaskDesk.Views.Tests;

/// <summary>
/// Waits on the UI thread without blocking it: each poll yields, so layout, loading and item realization run
/// in between. Fails the test when the condition does not hold within the timeout.
/// </summary>
public static class UiWait
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    public static async Task UntilAsync(Func<bool> condition, Func<string> failure, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                Assert.Fail(failure());
            }

            await Task.Delay(20, cancellationToken);
        }
    }
}
