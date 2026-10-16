using TaskDesk.Core.Tags;

namespace TaskDesk.Core.Tests;

public class TagNameTests
{
    [Theory]
    [InlineData("work", true)]
    [InlineData("a", true)]
    [InlineData("ção-2", true)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("two words", false)]
    [InlineData("tab\there", false)]
    [InlineData(" lead", false)]
    [InlineData("#work", false)]
    [InlineData("wo#rk", true)]
    public void IsValid_follows_the_Tag_name_rules(string? name, bool expected) =>
        Assert.Equal(expected, TagName.IsValid(name));

    [Fact]
    public void Forty_characters_are_allowed_and_forty_one_are_not()
    {
        Assert.True(TagName.IsValid(new string('a', 40)));
        Assert.False(TagName.IsValid(new string('a', 41)));
    }

    [Fact]
    public void Validate_throws_for_an_invalid_name() =>
        Assert.Throws<ArgumentException>(() => TagName.Validate("a b"));

    [Fact]
    public void Normalize_ignores_case() =>
        Assert.Equal(TagName.Normalize("Work"), TagName.Normalize("wORK"));
}
