using PhotoGallery.Application.Ports;
using PhotoGallery.Infrastructure.Imaging;

namespace PhotoGallery.Tests.Infrastructure;

/// <summary>
/// Exercises the generator against real photographs, where there are any to
/// exercise it against.
/// </summary>
/// <remarks>
/// These cases need real camera files and cannot synthesise them: a HEIC keeps
/// its EXIF one level up from where a JPEG does, and the whole point of the
/// perceptual hash is that it survives a real encoder. So the folder is named by
/// the <c>PHOTOGALLERY_TEST_PHOTOS</c> environment variable - point it at any
/// folder of photographs to run them, and without it they skip and say so rather
/// than pretending to have passed.
///
/// <para>It used to be a hardcoded share, which meant the tests ran on exactly
/// one machine and silently skipped everywhere else - including on anyone
/// else's.</para>
///
/// <para>The HEIC case is the one that matters most: Windows decodes HEIC only
/// when the HEIF codec is installed, which is an environment fact, so the test
/// reports it rather than asserting it and failing on a machine without it.</para>
/// </remarks>
public sealed class WindowsThumbnailGeneratorTests
{
    private const string PhotosVariable = "PHOTOGALLERY_TEST_PHOTOS";

    private const string NoPhotos =
        "set PHOTOGALLERY_TEST_PHOTOS to a folder of photographs to run this";

    private static readonly string? Share =
        Environment.GetEnvironmentVariable(PhotosVariable);

    private readonly WindowsThumbnailGenerator _generator = new();

    [SkippableFact]
    public async Task Jpeg_ProducesBothRenditions()
    {
        string? photo = FindFirst("*.jpg");
        Skip.If(photo is null, NoPhotos);

        GeneratedThumbnail? result = await _generator.GenerateAsync(photo!);

        Assert.NotNull(result);
        Assert.NotEmpty(result!.Tile);
        Assert.NotEmpty(result.Preview);

        // The tile must genuinely be the cheaper of the two, or the split has
        // achieved nothing.
        Assert.True(result.Tile.Length < result.Preview.Length,
            $"tile {result.Tile.Length} should be smaller than preview {result.Preview.Length}");
        Assert.True(result.SourceWidth > 0 && result.SourceHeight > 0);
    }

    [SkippableFact]
    public async Task Heic_DecodesOnlyIfWindowsHasTheCodec()
    {
        string? photo = FindFirst("*.heic");
        Skip.If(photo is null, $"no HEIC found - {NoPhotos}");

        GeneratedThumbnail? result = await _generator.GenerateAsync(photo!);

        Skip.If(result is null,
            "Windows has no HEIF codec installed - HEIC needs a native decoder");
        Assert.NotEmpty(result!.Tile);
    }

    [SkippableFact]
    public async Task Jpeg_ReadsTheCaptureDateFromExif()
    {
        // 89% of this library carries one, and it is the only honest date a
        // photo has: the file's own dates record when it was copied.
        string? photo = FindFirstWithExifDate();
        Skip.If(photo is null, $"no photo carrying an EXIF date was found - {NoPhotos}");

        GeneratedThumbnail? result = await _generator.GenerateAsync(photo!);

        Assert.NotNull(result);
        Assert.NotNull(result!.TakenUtc);
        Assert.InRange(result.TakenUtc!.Value, new DateTime(1990, 1, 1), DateTime.UtcNow.AddDays(1));
    }

    [SkippableFact]
    public async Task Heic_ReadsTheCaptureDateFromItsOwnMetadataPath()
    {
        // HEIF exposes EXIF one level up from where JPEG keeps it. Asking only
        // the JPEG path left all 864 HEIC photos in this library dated null and
        // unrotated, with nothing to indicate anything had gone wrong.
        string? photo = FindFirst("*.heic");
        Skip.If(photo is null, $"no HEIC found - {NoPhotos}");

        GeneratedThumbnail? result = await _generator.GenerateAsync(photo!);
        Skip.If(result is null, "Windows has no HEIF codec installed");

        Assert.NotNull(result!.TakenUtc);
        Assert.InRange(result.TakenUtc!.Value, new DateTime(1990, 1, 1), DateTime.UtcNow.AddDays(1));
    }

    [SkippableFact]
    public async Task Jpeg_ProducesAHashThatSurvivesReEncoding()
    {
        // Two renditions of one photo are the same picture at different sizes,
        // which is exactly what a perceptual hash must not distinguish.
        string? photo = FindFirst("*.jpg");
        Skip.If(photo is null, NoPhotos);

        GeneratedThumbnail? result = await _generator.GenerateAsync(photo!);
        Assert.NotNull(result);

        string tilePath = Path.Combine(Path.GetTempPath(), $"pg-tile-{Guid.NewGuid():N}.jpg");
        try
        {
            await File.WriteAllBytesAsync(tilePath, result!.Tile);
            GeneratedThumbnail? again = await _generator.GenerateAsync(tilePath);

            Assert.NotNull(again);
            int distance = result.PerceptualHash.DistanceTo(again!.PerceptualHash);
            Assert.True(distance <= 4,
                $"a re-encoded copy hashed {distance} bits away, which would read as a different photo");
        }
        finally
        {
            File.Delete(tilePath);
        }
    }

    [Fact]
    public async Task UnreadableFile_ReturnsNullRatherThanThrowing()
    {
        string path = Path.Combine(Path.GetTempPath(), $"pg-not-an-image-{Guid.NewGuid():N}.jpg");
        await File.WriteAllTextAsync(path, "this is not a JPEG");

        try
        {
            Assert.Null(await _generator.GenerateAsync(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task MissingFile_ReturnsNull() =>
        Assert.Null(await _generator.GenerateAsync(
            Path.Combine(Path.GetTempPath(), $"pg-missing-{Guid.NewGuid():N}.jpg")));

    /// <summary>
    /// Not every photo carries a date - roughly one in ten does not - so this
    /// looks past the ones that do not rather than reporting their absence as a
    /// failure to read.
    /// </summary>
    private static string? FindFirstWithExifDate()
    {
        if (!Directory.Exists(Share))
        {
            return null;
        }

        var generator = new WindowsThumbnailGenerator();
        try
        {
            foreach (string folder in Directory.EnumerateDirectories(Share))
            {
                foreach (string photo in Directory
                    .EnumerateFiles(folder, "*.jpg", SearchOption.TopDirectoryOnly)
                    .Take(5))
                {
                    if (generator.GenerateAsync(photo).GetAwaiter().GetResult()?.TakenUtc is not null)
                    {
                        return photo;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        return null;
    }

    private static string? FindFirst(string pattern)
    {
        if (!Directory.Exists(Share))
        {
            return null;
        }

        try
        {
            foreach (string folder in Directory.EnumerateDirectories(Share))
            {
                string? match = Directory
                    .EnumerateFiles(folder, pattern, SearchOption.TopDirectoryOnly)
                    .FirstOrDefault();
                if (match is not null)
                {
                    return match;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        return null;
    }
}
