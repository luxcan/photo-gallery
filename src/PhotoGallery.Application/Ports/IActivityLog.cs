namespace PhotoGallery.Application.Ports;

/// <summary>
/// Where the app's running commentary goes now that no panel shows it.
/// </summary>
/// <remarks>
/// It exists rather than the lines simply being dropped because several of them
/// are failures - a folder that would not detach, a refresh that threw, a
/// preference that would not save. Those are reported here precisely because
/// they are not worth interrupting the user for, which only works while there is
/// somewhere to read them afterwards.
/// </remarks>
public interface IActivityLog
{
    void Append(string line);
}
