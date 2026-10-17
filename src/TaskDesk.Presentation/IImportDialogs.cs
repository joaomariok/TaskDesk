using TaskDesk.Core.Import;

namespace TaskDesk.Presentation;

/// <summary>The dialogs the Import control uses; the WPF implementation lives in the App project.</summary>
public interface IImportDialogs
{
    /// <summary>Asks which JSON export to import.</summary>
    /// <returns>The chosen path, or null when the owner cancelled.</returns>
    string? PickImportPath();

    /// <summary>Asks whether to merge the file into the current data or replace everything with it.</summary>
    /// <param name="path">The chosen file, shown to the owner.</param>
    /// <returns>The chosen mode, or null when the owner cancelled.</returns>
    ImportMode? ChooseImportMode(string path);

    /// <summary>Asks the owner to confirm that Replace deletes all current data (after a backup).</summary>
    /// <returns>True to go ahead.</returns>
    bool ConfirmReplace();
}
