using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TaskDesk.Core.Tags;

namespace TaskDesk.Presentation;

// Tags of the selected Task, filtering the Task list by a Tag, and deleting a Tag.
public sealed partial class MainViewModel
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddTagCommand))]
    private string _newTagName = "";

    /// <summary>Filters Tasks by the Tag, or shows the Default List again when null.</summary>
    public async Task FilterByTagAsync(Tag? tag, CancellationToken cancellationToken = default)
    {
        CurrentView = null;
        TagFilter = tag;
        await LoadAsync(cancellationToken);
    }

    [RelayCommand(CanExecute = nameof(CanAddTag))]
    private async Task AddTagAsync(CancellationToken cancellationToken)
    {
        if (!CanAddTag())
        {
            return;
        }

        await tagService.AddTagToTaskAsync(SelectedTask!.Id, NewTagName.Trim(), cancellationToken);
        NewTagName = "";
        await LoadAsync(cancellationToken);
    }

    private bool CanAddTag() => SelectedTask is not null && TagName.IsValid(NewTagName?.Trim());

    [RelayCommand(CanExecute = nameof(CanRemoveTag))]
    private async Task RemoveTagAsync(Tag tag, CancellationToken cancellationToken)
    {
        if (!CanRemoveTag())
        {
            return;
        }

        await tagService.RemoveTagFromTaskAsync(SelectedTask!.Id, tag.Id, cancellationToken);
        await LoadAsync(cancellationToken);
    }

    private bool CanRemoveTag() => SelectedTask is not null;

    /// <summary>Deletes the Tag for good. The view must ask for confirmation first; there is no undo.</summary>
    public async Task DeleteTagAsync(Tag tag, CancellationToken cancellationToken = default)
    {
        await tagService.DeleteTagAsync(tag.Id, cancellationToken);
        await LoadAsync(cancellationToken);
    }
}
