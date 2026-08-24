using PhotoGallery.App.Shell;
using PhotoGallery.Application.Ports;
using PhotoGallery.Application.UseCases.Models;

namespace PhotoGallery.App.Models;

/// <summary>One model file as a line on the Settings screen, and its download.</summary>
/// <remarks>
/// Named individually because "3 of 4 files installed" is not something anybody
/// can act on and "textual/merges.txt is still needed" is - and with at most four
/// files to a feature, listing them costs almost nothing.
///
/// <para>Each line carries its own address, so the list of what is wanted and the
/// way to get it are the same list. Naming a page instead would leave the reader
/// to work out which of its files were meant.</para>
/// </remarks>
public sealed record ModelFileRow(
    string FileName,
    string Size,
    string State,
    bool IsReady,
    bool WasDamaged,
    string? Url)
{
    public static ModelFileRow Of(ModelFileStatus file)
    {
        ArgumentNullException.ThrowIfNull(file);

        return new ModelFileRow(
            file.FileName,
            FileSize.Rounded(file.Bytes),
            file.State switch
            {
                ModelState.Ready => "installed",

                // The store deletes what fails its digest, so by the time this
                // is read the file is gone - "damaged" would send the user
                // looking for something that is no longer there.
                ModelState.Damaged => "was not the right file, and was removed",
                _ => "not yet downloaded",
            },
            file.State == ModelState.Ready,
            file.State == ModelState.Damaged,
            ModelSources.Of(file.Id));
    }

    /// <summary>Whether this line can be clicked to fetch the file.</summary>
    /// <remarks>
    /// False once it is installed as well as when no address is known: a link
    /// beside "installed" invites somebody to download 1.2 GB they already have.
    /// </remarks>
    public bool CanDownload => !IsReady && !string.IsNullOrWhiteSpace(Url);
}
