namespace PhotoGallery.Application.UseCases.Faces;

/// <summary>What a face pass produced.</summary>
/// <param name="Pending">Previews that needed looking at when the pass started.</param>
/// <param name="Scanned">Previews actually read.</param>
/// <param name="ModelsMissing">
/// The pass did nothing because the weights are not installed. A separate answer
/// from "there was nothing to do", so the screen can offer the install rather
/// than reporting success.
/// </param>
public sealed record FaceDetectionResult(
    int Pending,
    int Scanned,
    int FacesFound,
    int Failed,
    TimeSpan Elapsed,
    bool WasCancelled,
    bool ModelsMissing)
{
    public static FaceDetectionResult WithoutModels(TimeSpan elapsed) =>
        new(0, 0, 0, 0, elapsed, false, true);

    public string Summary => ModelsMissing
        ? "the face model is not installed yet"
        : Pending == 0
            ? "every picture has already been looked at"
            : (WasCancelled ? "stopped, and will resume where it left off: " : string.Empty)
            + $"{FacesFound:N0} faces in {Scanned:N0} pictures, {Failed:N0} could not be read "
            + $"({Elapsed.TotalSeconds:N0}s)";
}
