using System.Collections.Concurrent;
using System.Diagnostics;
using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Assets;
using PhotoGallery.Domain.Faces;
using PhotoGallery.Infrastructure.Faces;
using PhotoGallery.Infrastructure.Imaging;
using PhotoGallery.Infrastructure.Models;
using PhotoGallery.Infrastructure.Storage;

namespace PhotoGallery.Measure;

/// <summary>
/// PRP 14 phase 1: measures every copy in every near set and writes the
/// numbers the thresholds will be chosen from. Changes nothing anywhere.
/// </summary>
internal static class Measurement
{
    public static int Run(MeasureOptions options)
    {
        var total = Stopwatch.StartNew();

        var models = new ReadOnlyModelStore(options.ModelsPath);
        string landmarkPath = models.ResolvePath(ModelId.FaceLandmarks);
        ModelState detector = models.StateOf(ModelId.FaceDetection);
        ModelState landmarks = models.StateOf(ModelId.FaceLandmarks);
        Console.WriteLine($"det_10g.onnx: {detector}. {Path.GetFileName(landmarkPath)}: {landmarks}.");
        if (detector != ModelState.Ready || landmarks != ModelState.Ready)
        {
            return 3;
        }

        var reading = Stopwatch.StartNew();
        (int exactSets, int exactCopies) counted;
        IReadOnlyList<NearCopy> copies;
        Dictionary<int, List<KnownFace>> facesByPhoto;
        using (var library = new LibraryCopy(options.DatabasePath))
        {
            counted = library.CountExact();
            copies = library.ReadNearCopies();
            var photos = copies.Select(c => c.AssetId).ToHashSet();
            facesByPhoto = library.ReadKnownFaces(photos)
                .GroupBy(f => f.AssetId)
                .ToDictionary(g => g.Key, g => g.ToList());
        }

        Console.WriteLine($"Read the database in {reading.Elapsed.TotalSeconds:N1} s.");

        Directory.CreateDirectory(options.OutputPath);
        string crops = Path.Combine(options.OutputPath, "crops");
        Directory.CreateDirectory(crops);

        var thumbnails = new FileSystemThumbnailStore(new ThumbnailsOnly(options.ThumbnailsPath));
        var photosToMeasure = copies
            .GroupBy(c => c.AssetId)
            .Select(g => (AssetId: g.Key, g.First().ThumbnailName))
            .ToList();

        var measuring = Stopwatch.StartNew();
        var readings = new ConcurrentDictionary<int, PhotoReading>();
        int done = 0;
        using (var measurer = new OnnxEyeMeasurer(models, landmarkPath))
        {
            // Half the cores, as the app's own passes do. On a card the
            // measurer's own slot keeps the graphs to one inference at a time.
            Parallel.ForEach(
                photosToMeasure,
                new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2) },
                photo =>
                {
                    IReadOnlyList<KnownFace> known = facesByPhoto.GetValueOrDefault(photo.AssetId) ?? [];
                    readings[photo.AssetId] = MeasureOne(photo.AssetId, photo.ThumbnailName, known, thumbnails, measurer);

                    int finished = Interlocked.Increment(ref done);
                    if (finished % 100 == 0)
                    {
                        Console.WriteLine($"  {finished} of {photosToMeasure.Count} photographs measured");
                    }
                });
        }

        measuring.Stop();

        WriteCopies(Path.Combine(options.OutputPath, "copies.csv"), copies, readings);
        WriteFaces(Path.Combine(options.OutputPath, "faces.csv"), copies, readings);

        var drawing = Stopwatch.StartNew();
        int drawn = DrawCrops(crops, readings.Values);
        drawing.Stop();

        int nearSets = copies.Select(c => c.SetId).Distinct().Count();
        int unresolvedSets = copies.Where(c => !c.SetResolved).Select(c => c.SetId).Distinct().Count();
        int unread = readings.Values.Count(r => r.SceneSharpness is null);
        var measuredFaces = readings.Values.SelectMany(r => r.Faces).ToList();
        int unmeasuredFaces = measuredFaces.Count(f => f.Eyes.Landmarks is null);

        Console.WriteLine();
        Console.WriteLine($"Exact sets: {counted.exactSets} holding {counted.exactCopies} copies - counted, never measured.");
        Console.WriteLine($"Near sets: {nearSets} ({unresolvedSets} unresolved) holding {copies.Count} copies "
                          + $"of {photosToMeasure.Count} photographs.");
        Console.WriteLine($"Previews that would not read: {unread}.");
        Console.WriteLine($"Known faces: {measuredFaces.Count}, of which {unmeasuredFaces} could not be measured.");
        Console.WriteLine($"Graphs ran on {InferenceDevice.Description}.");
        Console.WriteLine($"Measured {photosToMeasure.Count} photographs in {measuring.Elapsed.TotalSeconds:N1} s "
                          + $"({measuring.Elapsed.TotalMilliseconds / Math.Max(1, photosToMeasure.Count):N0} ms each).");
        Console.WriteLine($"Drew {drawn} face crops in {drawing.Elapsed.TotalSeconds:N1} s.");
        Console.WriteLine($"Total {total.Elapsed.TotalSeconds:N1} s. Written to {options.OutputPath}");
        return 0;
    }

    private static PhotoReading MeasureOne(
        int assetId,
        string? thumbnailName,
        IReadOnlyList<KnownFace> known,
        FileSystemThumbnailStore thumbnails,
        OnnxEyeMeasurer measurer)
    {
        string? path = RenditionName.IsSafeFileName(thumbnailName)
            ? thumbnails.ResolvePreviewPath(thumbnailName!)
            : null;
        BgrImage? preview = path is null ? null : PreviewPixels.Read(path);
        if (preview is null)
        {
            return new PhotoReading(
                assetId, path, null, null, null, null, null,
                known.Select(f => (f, new MeasuredEyes(EyeMeasurement.Unmeasured, null))).ToList());
        }

        FaceBounds[] boxes = known.Select(f => f.Bounds).ToArray();
        IReadOnlyList<MeasuredEyes> eyes;
        try
        {
            eyes = measurer.Measure(preview, boxes);
        }
        catch (Microsoft.ML.OnnxRuntime.OnnxRuntimeException)
        {
            // Counted as faces that could not be measured, so one photograph
            // the graphs refuse does not end the whole measurement.
            eyes = [.. boxes.Select(_ => new MeasuredEyes(EyeMeasurement.Unmeasured, null))];
        }

        return new PhotoReading(
            assetId,
            path,
            preview.Width,
            preview.Height,
            PreviewQualityMeter.FaceSharpness(preview, boxes),
            PreviewQualityMeter.SceneSharpness(preview),
            PreviewQualityMeter.Exposure(preview),
            known.Zip(eyes).ToList());
    }

    private static void WriteCopies(
        string path, IReadOnlyList<NearCopy> copies, IReadOnlyDictionary<int, PhotoReading> readings)
    {
        using var writer = new StreamWriter(path);
        writer.WriteLine(Csv.Row(
            "setId", "setResolved", "assetId", "path", "pixels", "bytes", "hasTakenUtc", "hasGps", "role",
            "quarantined", "previewWidth", "previewHeight", "sharpnessFaces", "sharpnessTiles",
            "clippedHigh", "clippedLow", "knownFaceCount"));

        foreach (NearCopy copy in copies)
        {
            PhotoReading reading = readings[copy.AssetId];
            writer.WriteLine(Csv.Row(
                copy.SetId,
                copy.SetResolved,
                copy.AssetId,
                copy.Path,
                copy.Width is int width && copy.Height is int height ? (long)width * height : null,
                copy.Bytes,
                copy.HasTakenUtc,
                copy.HasGps,
                copy.Role,
                copy.Quarantined,
                reading.PreviewWidth,
                reading.PreviewHeight,
                reading.FaceSharpness,
                reading.SceneSharpness,
                reading.Exposure?.ClippedHigh,
                reading.Exposure?.ClippedLow,
                reading.Faces.Count));
        }
    }

    private static void WriteFaces(
        string path, IReadOnlyList<NearCopy> copies, IReadOnlyDictionary<int, PhotoReading> readings)
    {
        using var writer = new StreamWriter(path);
        writer.WriteLine(Csv.Row(
            "setId", "assetId", "faceId", "personId", "personName", "source", "boundsX", "boundsY",
            "boundsWidth", "boundsHeight", "detectScore", "turn", "confidence", "earLeft", "earRight",
            "measured"));

        foreach (NearCopy copy in copies)
        {
            foreach ((KnownFace face, MeasuredEyes eyes) in readings[copy.AssetId].Faces)
            {
                EyeMeasurement measurement = eyes.Measurement;
                writer.WriteLine(Csv.Row(
                    copy.SetId,
                    copy.AssetId,
                    face.FaceId,
                    face.PersonId,
                    face.PersonName,
                    face.Source,
                    face.Bounds.X,
                    face.Bounds.Y,
                    face.Bounds.Width,
                    face.Bounds.Height,
                    face.DetectScore,
                    measurement.Turn,
                    measurement.Confidence,
                    measurement.LeftOpenness,
                    measurement.RightOpenness,
                    eyes.Landmarks is not null));
            }
        }
    }

    /// <summary>
    /// One crop per known face, drawn on a thread of its own because WPF
    /// drawing needs a single-threaded apartment.
    /// </summary>
    private static int DrawCrops(string folder, IEnumerable<PhotoReading> readings)
    {
        int drawn = 0;
        var thread = new Thread(() =>
        {
            foreach (PhotoReading reading in readings.Where(r => r.Faces.Count > 0 && r.PreviewPath is not null))
            {
                BgrImage? preview = PreviewPixels.Read(reading.PreviewPath!);
                if (preview is null)
                {
                    continue;
                }

                foreach ((KnownFace face, MeasuredEyes eyes) in reading.Faces)
                {
                    FaceCrops.Write(preview, face.Bounds, eyes.Landmarks, Path.Combine(folder, $"{face.FaceId}.png"));
                    drawn++;
                }
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        return drawn;
    }
}
