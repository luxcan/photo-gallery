using System.Security.AccessControl;
using System.Security.Principal;

namespace PhotoGallery.Tests;

/// <summary>
/// Stops the current user listing one folder until disposed - the way a NAS
/// folder with tighter permissions refuses a scan.
/// </summary>
/// <remarks>
/// Only listing is denied, so the folder still exists and its parent still
/// shows it: the case where a walk must say "could not read" rather than
/// "empty". The owner can always change permissions, so the rule can be taken
/// off again and the fixture can delete the folder.
/// </remarks>
internal sealed class DeniedFolder : IDisposable
{
    private readonly DirectoryInfo _folder;
    private readonly FileSystemAccessRule _rule;

    public DeniedFolder(string path)
    {
        _folder = new DirectoryInfo(path);
        _rule = new FileSystemAccessRule(
            WindowsIdentity.GetCurrent().User!,
            FileSystemRights.ListDirectory,
            AccessControlType.Deny);

        DirectorySecurity security = _folder.GetAccessControl();
        security.AddAccessRule(_rule);
        _folder.SetAccessControl(security);
    }

    public void Dispose()
    {
        DirectorySecurity security = _folder.GetAccessControl();
        security.RemoveAccessRule(_rule);
        _folder.SetAccessControl(security);
    }
}
