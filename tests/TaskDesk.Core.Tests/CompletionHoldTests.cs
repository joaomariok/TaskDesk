using TaskDesk.Core.Tasks;

namespace TaskDesk.Core.Tests;

public class CompletionHoldTests
{
    [Theory]
    [InlineData(true, 400)]
    [InlineData(false, 0)]
    public void A_completed_row_is_held_400_ms_unless_Windows_animations_are_off(bool animationsEnabled, int milliseconds)
    {
        Assert.Equal(TimeSpan.FromMilliseconds(milliseconds), CompletionHold.For(animationsEnabled));
    }
}
