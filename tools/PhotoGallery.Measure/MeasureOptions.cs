namespace PhotoGallery.Measure;

/// <summary>
/// Where the harness reads from and writes to. Every location is given on the
/// command line; none is assumed.
/// </summary>
/// <param name="DatabasePath">A COPY of the library's index.db. Opened read-only.</param>
/// <param name="ThumbnailsPath">The library's thumbs folder. Only read.</param>
/// <param name="ModelsPath">A folder holding det_10g.onnx and 2d106det.onnx. Only read.</param>
/// <param name="OutputPath">Where the CSVs and crops go - never the library or a source tree.</param>
/// <param name="UseGraphicsCard">
/// Run the graphs through DirectML, one inference at a time, instead of on the
/// processor.
/// </param>
internal sealed record MeasureOptions(
    string DatabasePath,
    string ThumbnailsPath,
    string ModelsPath,
    string OutputPath,
    bool UseGraphicsCard)
{
    public const string Usage =
        "PhotoGallery.Measure --db <copy of index.db> --thumbs <thumbs folder> "
        + "--models <folder with det_10g.onnx and 2d106det.onnx> --out <output folder> [--gpu]";

    /// <summary>The options, or null with the reason when the arguments do not make sense.</summary>
    public static MeasureOptions? Parse(IReadOnlyList<string> args, out string? problem)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        bool gpu = false;

        for (int i = 0; i < args.Count; i++)
        {
            if (string.Equals(args[i], "--gpu", StringComparison.OrdinalIgnoreCase))
            {
                gpu = true;
            }
            else if (args[i].StartsWith("--", StringComparison.Ordinal) && i + 1 < args.Count)
            {
                values[args[i][2..]] = args[++i];
            }
            else
            {
                problem = $"Did not understand '{args[i]}'.";
                return null;
            }
        }

        foreach (string required in new[] { "db", "thumbs", "models", "out" })
        {
            if (!values.ContainsKey(required))
            {
                problem = $"--{required} is required.";
                return null;
            }
        }

        string database = Path.GetFullPath(values["db"]);
        if (!File.Exists(database))
        {
            problem = $"No database at {database}.";
            return null;
        }

        string thumbnails = Path.GetFullPath(values["thumbs"]);
        string models = Path.GetFullPath(values["models"]);
        foreach (string folder in new[] { thumbnails, models })
        {
            if (!Directory.Exists(folder))
            {
                problem = $"No folder at {folder}.";
                return null;
            }
        }

        // The family's photographs and names must not land in the library, beside
        // the database copy, or anywhere they could be committed. The database
        // read is a copy, so the library is found from the thumbs folder: its
        // parent holds the live index.db.
        string output = Path.GetFullPath(values["out"]);
        string library = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(thumbnails)) ?? thumbnails;
        if (IsWithin(output, library) || IsWithin(output, Path.GetDirectoryName(database)!))
        {
            problem = "--out must not be inside the library that holds the thumbs folder, or beside the database copy.";
            return null;
        }

        if (WorkingTreeHolding(output) is { } repository)
        {
            problem = $"--out must not be inside the source tree at {repository}.";
            return null;
        }

        problem = null;
        return new MeasureOptions(database, thumbnails, models, output, gpu);
    }

    /// <summary>
    /// The nearest folder at or above <paramref name="path"/> that is a git
    /// working tree: it holds a .git folder, or a .git file for a linked worktree.
    /// </summary>
    private static string? WorkingTreeHolding(string path)
    {
        for (var folder = new DirectoryInfo(path); folder is not null; folder = folder.Parent)
        {
            string marker = Path.Combine(folder.FullName, ".git");
            if (Directory.Exists(marker) || File.Exists(marker))
            {
                return folder.FullName;
            }
        }

        return null;
    }

    private static bool IsWithin(string path, string folder)
    {
        string root = Path.TrimEndingDirectorySeparator(folder) + Path.DirectorySeparatorChar;
        return (Path.TrimEndingDirectorySeparator(path) + Path.DirectorySeparatorChar)
            .StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }
}
