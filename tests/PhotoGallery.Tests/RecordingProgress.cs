namespace PhotoGallery.Tests;

/// <summary>Records every report as it is made, without posting anywhere.</summary>
/// <remarks>
/// <see cref="Progress{T}"/> posts to whatever context made it, so a test
/// counting reports through it races the callbacks. This one is synchronous.
/// </remarks>
internal sealed class RecordingProgress<T> : IProgress<T>
{
    private readonly List<T> _seen = [];

    public IReadOnlyList<T> Seen
    {
        get
        {
            lock (_seen)
            {
                return [.. _seen];
            }
        }
    }

    public void Report(T value)
    {
        lock (_seen)
        {
            _seen.Add(value);
        }
    }
}
