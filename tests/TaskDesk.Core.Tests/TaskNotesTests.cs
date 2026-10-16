using TaskDesk.Core.Tasks;

namespace TaskDesk.Core.Tests;

public class TaskNotesTests
{
    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("short", true)]
    public void Short_notes_are_valid(string? notes, bool expected) => Assert.Equal(expected, TaskNotes.IsValid(notes));

    [Fact]
    public void The_limit_is_10000_characters()
    {
        Assert.True(TaskNotes.IsValid(new string('x', 10_000)));
        Assert.False(TaskNotes.IsValid(new string('x', 10_001)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  \n ")]
    public void Blank_notes_normalise_to_null(string? notes) => Assert.Null(TaskNotes.Normalize(notes));

    [Fact]
    public void Notes_are_otherwise_kept_exactly_as_typed()
        => Assert.Equal("  line 1\nline 2 ", TaskNotes.Normalize("  line 1\nline 2 "));

    [Fact]
    public void Too_long_notes_are_rejected()
        => Assert.Throws<ArgumentException>(() => TaskNotes.Normalize(new string('x', 10_001)));
}
