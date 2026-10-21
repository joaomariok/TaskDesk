using TaskDesk.Core.Tasks;

namespace TaskDesk.Core.Tests;

public class SearchFoldTests
{
    [Theory]
    [InlineData("ÂNGELA", "ângela")]
    [InlineData("Ângela", "ângela")]
    [InlineData("ABC xyz", "abc xyz")]
    [InlineData("", "")]
    public void Apply_folds_to_invariant_lower_case_including_non_ASCII_letters(string text, string expected) =>
        Assert.Equal(expected, SearchFold.Apply(text));

    [Fact]
    public void Apply_keeps_null_null() => Assert.Null(SearchFold.Apply(null));

    [Theory]
    [InlineData("Ângela's gift", "ÂNGELA", true)]
    [InlineData("Ângela's gift", "gift", true)]
    [InlineData("pack", "ângela", false)]
    [InlineData("100% sure", "%", true)]
    [InlineData("pack", "", true)]
    [InlineData(null, "a", false)]
    [InlineData("pack", null, false)]
    public void Contains_ignores_case_by_the_fold(string? text, string? search, bool expected) =>
        Assert.Equal(expected, SearchFold.Contains(text, search));
}
