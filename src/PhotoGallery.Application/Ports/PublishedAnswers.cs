namespace PhotoGallery.Application.Ports;

/// <summary>
/// That a machine has put its answers somewhere this one can see them, and when
/// it last did.
/// </summary>
/// <remarks>
/// Everything a directory listing knows and nothing more. The machine's name is
/// inside the file, so it is not here: a screen that had to decompress half a
/// megabyte per machine to draw one line of text is a screen nobody opens twice.
/// Names come from the machines this library has actually merged from, which is
/// every machine after the first share.
/// </remarks>
public sealed record PublishedAnswers(Guid MachineId, DateTime WrittenUtc);
