using TaskDesk.Core.Import;

namespace TaskDesk.Presentation;

/// <summary>The dialogs the Import control uses, implemented by each UI. Asynchronous because WinUI dialogs are.</summary>
public interface IImportDialogs
{
    /// <summary>Asks which JSON export to import.</summary>
    /// <returns>The chosen path, or null when the owner cancelled.</returns>
    Task<string?> PickImportPathAsync();

    /// <summary>Asks whether to merge the file into the current data or replace everything with it.</summary>
    /// <param name="path">The chosen file, shown to the owner.</param>
    /// <returns>The chosen mode, or null when the owner cancelled.</returns>
    Task<ImportMode?> ChooseImportModeAsync(string path);

    /// <summary>Asks the owner to confirm that Replace deletes all current data (after a backup).</summary>
    /// <returns>True to go ahead.</returns>
    Task<bool> ConfirmReplaceAsync();
}
