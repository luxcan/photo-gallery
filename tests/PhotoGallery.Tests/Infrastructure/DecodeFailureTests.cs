using System.Runtime.InteropServices;
using PhotoGallery.Infrastructure.Imaging;

namespace PhotoGallery.Tests.Infrastructure;

/// <summary>
/// Which exceptions write a picture off as unreadable, and which are left to
/// stop the pass.
/// </summary>
/// <remarks>
/// A real file that makes an extension codec raise a bare COM error cannot be
/// made on demand - it depends on which codecs a machine has - so the rule
/// every reader shares is asserted directly.
/// </remarks>
public sealed class DecodeFailureTests
{
    [Fact]
    public void ACodecErrorWpfDoesNotRecognise_IsUnreadable()
    {
        // What WPF raises for any failure it has no .NET type for. It used to
        // escape the thumbnail, preview and coordinate readers and close the app.
        Assert.True(DecodeFailure.IsUnreadable(new COMException("codec", unchecked((int)0xC00D36C4))));
    }

    [Fact]
    public void TheFailuresEveryReaderAlreadyCaught_AreStillUnreadable()
    {
        Exception[] failures =
        [
            new IOException(),
            new UnauthorizedAccessException(),
            new NotSupportedException(),
            new ArgumentException(),
            new OverflowException(),
            new InvalidOperationException(),
            new FileFormatException(),
            new FormatException(),
            new OutOfMemoryException(),
        ];

        Assert.All(failures, ex => Assert.True(DecodeFailure.IsUnreadable(ex), ex.GetType().Name));
    }

    [Fact]
    public void StoppingIsNotAFailureOfTheFile()
    {
        // Stop must still end the pass rather than writing off the file it
        // happened to be reading.
        Assert.False(DecodeFailure.IsUnreadable(new OperationCanceledException()));
    }

    [Fact]
    public void ACrashInNativeCode_IsNotPassedOffAsABadFile()
    {
        // A sibling of the COM error under ExternalException, and a bug rather
        // than a property of the picture.
        Assert.False(DecodeFailure.IsUnreadable(new SEHException()));
    }
}
