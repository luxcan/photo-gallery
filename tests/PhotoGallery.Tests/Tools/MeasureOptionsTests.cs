using PhotoGallery.Measure;

namespace PhotoGallery.Tests.Tools;

/// <summary>
/// The measuring harness writes the family's names and face crops. Its --out
/// must never land in the library or in a source tree, wherever the database
/// copy it reads happens to sit.
/// </summary>
public sealed class MeasureOptionsTests : IDisposable
{
    private readonly string _root;
    private readonly string _library;
    private readonly string _thumbs;
    private readonly string _models;
    private readonly string _databaseCopy;

    public MeasureOptionsTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"pg-measure-options-{Guid.NewGuid():N}");
        _library = Path.Combine(_root, "library");
        _thumbs = Path.Combine(_library, "thumbs");
        _models = Path.Combine(_root, "models");
        Directory.CreateDirectory(_thumbs);
        Directory.CreateDirectory(_models);
        Directory.CreateDirectory(Path.Combine(_root, "copy"));
        _databaseCopy = Path.Combine(_root, "copy", "index.db");
        File.WriteAllText(_databaseCopy, string.Empty);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Parse_AcceptsAnOutputOutsideTheLibraryAndAnySourceTree()
    {
        MeasureOptions? options = Parse(Path.Combine(_root, "measure"), out string? problem);

        Assert.NotNull(options);
        Assert.Null(problem);
    }

    [Fact]
    public void Parse_RefusesAnOutputInsideTheThumbsFolder() =>
        AssertRefused(Path.Combine(_thumbs, "measure"));

    [Fact]
    public void Parse_RefusesAnOutputBesideTheDatabaseCopy() =>
        AssertRefused(Path.Combine(_root, "copy", "measure"));

    [Fact]
    public void Parse_RefusesAnOutputInsideTheLibraryThatHoldsTheThumbs()
    {
        // The database read is a copy elsewhere, so only the thumbs folder says
        // where the real library is: its parent holds the live index.db.
        AssertRefused(Path.Combine(_library, "measure"));
        AssertRefused(_library);
    }

    [Fact]
    public void Parse_RefusesAnOutputInsideAGitWorkingTree()
    {
        string repository = Path.Combine(_root, "repo");
        Directory.CreateDirectory(Path.Combine(repository, ".git"));

        AssertRefused(Path.Combine(repository, "out", "measure"));
    }

    [Fact]
    public void Parse_RefusesAnOutputInsideAGitWorktree()
    {
        // A linked worktree marks its root with a .git FILE, not a folder.
        string worktree = Path.Combine(_root, "worktree");
        Directory.CreateDirectory(worktree);
        File.WriteAllText(Path.Combine(worktree, ".git"), "gitdir: elsewhere");

        AssertRefused(Path.Combine(worktree, "measure"));
    }

    private void AssertRefused(string output)
    {
        MeasureOptions? options = Parse(output, out string? problem);

        Assert.Null(options);
        Assert.Contains("--out", problem);
    }

    private MeasureOptions? Parse(string output, out string? problem) =>
        MeasureOptions.Parse(
            ["--db", _databaseCopy, "--thumbs", _thumbs, "--models", _models, "--out", output],
            out problem);
}
