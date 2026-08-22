using System.IO;
using PhotoGallery.Application.Ports;

namespace PhotoGallery.Infrastructure.Storage;

/// <inheritdoc cref="ISourceAvailability"/>
public sealed class FileSystemSourceAvailability : ISourceAvailability
{
    public bool CanReach(string sourceRoot)
    {
        if (string.IsNullOrWhiteSpace(sourceRoot))
        {
            return false;
        }

        try
        {
            // Enumerated rather than asked with Directory.Exists, which is the
            // whole point of this class. Exists answers false for a folder that
            // was deleted and for a share that is not there alike - it catches
            // everything and returns a bare bool - so it cannot tell absence
            // from ignorance. Enumerating throws, and the exception says which:
            // 0x80070002 and 0x80070003 for a path that is genuinely gone,
            // 0x80070035 "the network path was not found" for a share that is
            // simply away.
            using IEnumerator<string> entries =
                Directory.EnumerateFileSystemEntries(sourceRoot).GetEnumerator();

            // Moved once because enumeration is lazy: the call above can succeed
            // against a path nobody has tried to open yet, and the first step is
            // what actually goes to the share.
            entries.MoveNext();

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                      or ArgumentException or NotSupportedException)
        {
            // Every one of these means the same thing to the caller: this source
            // cannot be spoken for. A root that exists but refuses to be listed
            // is no more knowable than one that is not there, so permission
            // failures land here too rather than being read as "reachable".
            return false;
        }
    }
}
