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
        await ShowAsync(tag is { } shown ? new TaskListPage.TagPage(shown) : TaskListPage.DefaultList, cancellationToken);
    }

    [RelayCommand(CanExecute = nameof(CanAddTag))]
    private Task AddTagAsync(CancellationToken cancellationToken) => _lifetime.RunAsync(async token =>
    {
        if (!CanAddTag())
        {
            return;
        }

        await tagService.AddTagToTaskAsync(DetailTask!.Id, NewTagName.Trim(), token);
        NewTagName = "";
        await LoadAsync(token);
    }, cancellationToken);

    private bool CanAddTag() => DetailTask is not null && TagName.IsValid(NewTagName?.Trim());

    [RelayCommand(CanExecute = nameof(CanRemoveTag))]
    private Task RemoveTagAsync(Tag tag, CancellationToken cancellationToken) => _lifetime.RunAsync(async token =>
    {
        if (!CanRemoveTag())
        {
            return;
        }

        await tagService.RemoveTagFromTaskAsync(DetailTask!.Id, tag.Id, token);
        await LoadAsync(token);
    }, cancellationToken);

    private bool CanRemoveTag() => DetailTask is not null;

    /// <summary>
    /// Deletes the Tag for good. The view must ask for confirmation first; there is no undo. Does nothing once the
    /// view model has stopped (<see cref="StopAsync"/>), so a confirmation answered during the exit deletes nothing.
    /// </summary>
    public Task DeleteTagAsync(Tag tag, CancellationToken cancellationToken = default)
        => _lifetime.RunAsync(async token =>
        {
            await tagService.DeleteTagAsync(tag.Id, token);
            await LoadAsync(token);
        }, cancellationToken);
}
