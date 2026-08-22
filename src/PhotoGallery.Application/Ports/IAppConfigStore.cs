namespace PhotoGallery.Application.Ports;

/// <summary>
/// Reads and writes the one small file that lives outside every working folder.
/// </summary>
/// <remarks>
/// It cannot sit beside the executable, because an app installed under Program
/// Files has no write access there.
/// </remarks>
public interface IAppConfigStore
{
    /// <summary>Never throws: a missing or corrupt file yields defaults.</summary>
    AppConfig Load();

    void Save(AppConfig config);

    /// <summary>Records a folder as the most recently opened one.</summary>
    void RememberFolder(string workingFolderPath);

    /// <summary>
    /// Forgets which library was last open, so the next start asks again.
    /// </summary>
    /// <remarks>
    /// The folder stays in the recents list - the point is to be asked, not to
    /// lose the answer.
    /// </remarks>
    void ForgetLastFolder();
}
