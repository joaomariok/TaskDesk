using TaskDesk.Core.Lists;

namespace TaskDesk.Core.Tests;

public class ListRulesTests
{
    [Theory]
    [InlineData("Work", true)]
    [InlineData("  Work  ", true)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData(null, false)]
    public void List_name_validity(string? name, bool expected) => Assert.Equal(expected, ListName.IsValid(name));

    [Fact]
    public void List_name_allows_exactly_100_characters_after_trimming()
    {
        Assert.True(ListName.IsValid(" " + new string('a', 100) + " "));
        Assert.False(ListName.IsValid(new string('a', 101)));
    }

    [Fact]
    public void List_name_is_trimmed() => Assert.Equal("Work", ListName.Normalize("  Work "));

    [Fact]
    public void Invalid_list_name_throws() => Assert.Throws<ArgumentException>(() => ListName.Normalize(" "));

    [Theory]
    [InlineData(null, true)]
    [InlineData("#FF8800", true)]
    [InlineData("#ff8800", true)]
    [InlineData("FF8800", false)]
    [InlineData("#FFF", false)]
    [InlineData("#GG8800", false)]
    [InlineData("", false)]
    public void List_colour_validity(string? color, bool expected) => Assert.Equal(expected, ListColor.IsValid(color));

    [Fact]
    public void List_colour_is_normalised_to_upper_case() => Assert.Equal("#FF8800", ListColor.Normalize("#ff8800"));

    [Fact]
    public void Invalid_list_colour_throws() => Assert.Throws<ArgumentException>(() => ListColor.Normalize("red"));
}
