using TaskDesk.Core.Tags;
using TaskDesk.Core.Tasks;

namespace TaskDesk.Presentation;

/// <summary>What the Task list shows: one List, one Smart View or one Tag's Tasks, never a mix.</summary>
public abstract record TaskListPage
{
    private TaskListPage()
    {
    }

    /// <summary>The Default List's page before the Lists are loaded; loading replaces it with the real id.</summary>
    public static TaskListPage DefaultList { get; } = new ListPage(null);

    /// <summary>A List's page. A null id stands for the Default List until the Lists are loaded.</summary>
    public sealed record ListPage(Guid? ListId) : TaskListPage;

    /// <summary>A Smart View across all Lists.</summary>
    public sealed record SmartViewPage(SmartView View) : TaskListPage;

    /// <summary>The Tasks that carry a Tag, across all Lists.</summary>
    public sealed record TagPage(Tag Tag) : TaskListPage;
}
