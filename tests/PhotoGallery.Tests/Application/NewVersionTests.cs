using System.Text.Json;
using PhotoGallery.Application;
using PhotoGallery.Application.Ports;
using PhotoGallery.Application.UseCases.Updates;
using PhotoGallery.Infrastructure.Updates;

namespace PhotoGallery.Tests.Application;

/// <summary>
/// Noticing that a newer version of the app has been published.
/// </summary>
/// <remarks>
/// Two halves, tested apart because they fail for different reasons. Deciding
/// what counts as newer is a rule, and worth pinning; reaching GitHub is a
/// network call, and pinning that would only prove the network was up when the
/// suite ran. The port between them is where the line is drawn.
///
/// <para>Every version here is worked out from the one this build actually
/// carries rather than written as a literal, so releasing 1.1.0 does not quietly
/// turn "1.0.1 is newer" into a false statement and a red test.</para>
/// </remarks>
public sealed class NewVersionTests
{
    private static Version Running => AppVersion.Comparable!;

    private static Version Newer =>
        new(Running.Major, Running.Minor, Math.Max(Running.Build, 0) + 1);

    private static Version Older =>
        Running.Minor > 0
            ? new Version(Running.Major, Running.Minor - 1)
            : new Version(Math.Max(Running.Major - 1, 0), 9);

    [Fact]
    public async Task ANewerRelease_IsWorthSaying()
    {
        var published = new PublishedRelease(Newer, "Photo Gallery 1.0.1", "https://example/1");

        PublishedRelease? news = await Check(published).HandleAsync();

        Assert.Same(published, news);
    }

    [Fact]
    public async Task TheVersionAlreadyRunning_IsNotNews()
    {
        PublishedRelease? news =
            await Check(new PublishedRelease(Running, "this one", "https://example/0")).HandleAsync();

        Assert.Null(news);
    }

    [Fact]
    public async Task AnOlderRelease_IsNotNews()
    {
        // What a machine running a build newer than anything published sees,
        // which is this machine most of the time while the app is being worked
        // on.
        PublishedRelease? news =
            await Check(new PublishedRelease(Older, "an old one", "https://example/x")).HandleAsync();

        Assert.Null(news);
    }

    [Fact]
    public async Task NothingPublished_IsNotNews()
    {
        Assert.Null(await Check(null).HandleAsync());
    }

    /// <summary>
    /// A tag is only news if it is a version, and "v1.2.3" is one.
    /// </summary>
    [Fact]
    public void TheTagIsReadAsAVersion_WithOrWithoutItsV()
    {
        PublishedRelease? read = GitHubReleases.Read(Json("""
            { "tag_name": "v1.2.3", "name": "Photo Gallery 1.2.3",
              "html_url": "https://github.com/luxcan/photo-gallery/releases/tag/v1.2.3" }
            """));

        Assert.NotNull(read);
        Assert.Equal(new Version(1, 2, 3), read.Number);
        Assert.Equal("Photo Gallery 1.2.3", read.Name);
        Assert.EndsWith("v1.2.3", read.Page, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{ "tag_name": "nightly" }""")]
    [InlineData("""{ "tag_name": "v1.2.3-beta.1" }""")]
    [InlineData("""{ "tag_name": "v1.2.3", "prerelease": true }""")]
    [InlineData("""{ "tag_name": "v1.2.3", "draft": true }""")]
    [InlineData("""{ "name": "no tag at all" }""")]
    [InlineData("""{ }""")]
    public void WhatCannotBeComparedIsNotOffered(string answer)
    {
        // Guessing which of two names is the later one is how somebody ends up
        // being told to install something older than what they have.
        Assert.Null(GitHubReleases.Read(Json(answer)));
    }

    [Fact]
    public void AReleaseWithNoNameIsCalledByItsTag()
    {
        PublishedRelease? read = GitHubReleases.Read(Json("""{ "tag_name": "v2.0.0" }"""));

        Assert.NotNull(read);
        Assert.Equal("v2.0.0", read.Name);
    }

    private static CheckForNewVersionHandler Check(PublishedRelease? newest) =>
        new(new OneRelease(newest));

    private static JsonElement Json(string text) =>
        JsonDocument.Parse(text).RootElement.Clone();

    /// <summary>Whatever the test says has been published, and no network.</summary>
    private sealed class OneRelease : IReleaseSource
    {
        private readonly PublishedRelease? _newest;

        public OneRelease(PublishedRelease? newest) => _newest = newest;

        public Task<PublishedRelease?> NewestAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_newest);
    }
}
