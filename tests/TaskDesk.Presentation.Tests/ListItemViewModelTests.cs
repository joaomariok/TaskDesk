using TaskDesk.Core.Lists;
using TaskDesk.Core.Tasks;

namespace TaskDesk.Presentation.Tests;

/// <summary>A List's initial is what the narrow rail shows for it, so Lists tell apart without a tooltip.</summary>
public class ListItemViewModelTests
{
    [Theory]
    [InlineData("Work", "W")]
    [InlineData("groceries", "G")]
    [InlineData("éclair club", "É")]
    [InlineData("🛒 Shopping", "🛒")]
    [InlineData("", "")]
    public void The_initial_is_the_first_character_of_the_name_in_capitals(string name, string initial)
    {
        Assert.Equal(initial, NewItem(name).Initial);
    }

    [Fact]
    public void Renaming_a_List_changes_its_initial()
    {
        var item = NewItem("Work");
        var changed = new List<string?>();
        item.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        item.Name = "Home";

        Assert.Equal("H", item.Initial);
        Assert.Contains(nameof(ListItemViewModel.Initial), changed);
    }

    private static ListItemViewModel NewItem(string name) =>
        new(new ListSummary(new TaskList { Id = Guid.NewGuid(), Name = name }, 0));
}
