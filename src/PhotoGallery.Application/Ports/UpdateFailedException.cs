namespace PhotoGallery.Application.Ports;

/// <summary>An update did not happen, with the sentence that says why.</summary>
public sealed class UpdateFailedException : Exception
{
    public UpdateFailedException(string message)
        : base(message)
    {
    }

    public UpdateFailedException(string message, Exception inner)
        : base(message, inner)
    {
    }

    public UpdateFailedException()
    {
    }
}
