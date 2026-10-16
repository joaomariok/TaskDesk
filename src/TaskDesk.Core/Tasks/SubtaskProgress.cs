namespace TaskDesk.Core.Tasks;

/// <summary>The "done/total" progress of a Task's Subtasks. Display only: it never completes the Task.</summary>
public static class SubtaskProgress
{
    /// <summary>"2/5", or an empty string when there are no Subtasks.</summary>
    public static string Format(IEnumerable<Subtask> subtasks)
    {
        var total = 0;
        var done = 0;
        foreach (var subtask in subtasks)
        {
            total++;
            if (subtask.IsCompleted)
            {
                done++;
            }
        }

        return total == 0 ? "" : $"{done}/{total}";
    }
}
