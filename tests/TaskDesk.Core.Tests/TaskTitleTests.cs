using TaskDesk.Core.Tasks;

namespace TaskDesk.Core.Tests;

public class TaskTitleTests
{
    [Theory]
    [InlineData("Buy milk", "Buy milk")]
    [InlineData("  Buy milk \t", "Buy milk")]
    public void Normalize_trims_surrounding_whitespace(string input, string expected)
        => Assert.Equal(expected, TaskTitle.Normalize(input));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Normalize_rejects_an_empty_title(string? input)
        => Assert.Throws<ArgumentException>(() => TaskTitle.Normalize(input));

    [Fact]
    public void Normalize_accepts_500_characters_and_rejects_501()
    {
        Assert.Equal(500, TaskTitle.Normalize(new string('a', 500)).Length);
        Assert.Throws<ArgumentException>(() => TaskTitle.Normalize(new string('a', 501)));
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("  ", false)]
    [InlineData(" x ", true)]
    public void IsValid_matches_Normalize(string? input, bool expected)
        => Assert.Equal(expected, TaskTitle.IsValid(input));
}
