using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using PhotoGallery.Application.Ports;

namespace PhotoGallery.Infrastructure.Updates;

/// <summary>
/// The releases published on the repository this app is built from.
/// </summary>
/// <remarks>
/// One small JSON request to a public repository, so no key is involved and
/// nothing about this machine is sent. GitHub allows sixty such requests an hour
/// from one address, which is many times what a check every few hours costs.
///
/// <para><strong>A tag is only news if it is a version.</strong> The tag is read
/// as a number and anything else is ignored: a pre-release, a tag somebody typed
/// by hand, a name with a word in it. There is no way to compare those with what
/// is running, and guessing which is newer would eventually tell somebody to
/// install something older than what they have.</para>
///
/// <para><strong>Every failure answers null.</strong> No network, a refusal, a
/// timeout, something that is not the JSON this expects - all of them mean the
/// same thing to the person at the screen, which is that there is nothing to
/// say. The one thing never done here is to throw into a caller whose job is to
/// decide whether to show a notice.</para>
/// </remarks>
public sealed class GitHubReleases : IReleaseSource
{
    /// <summary>
    /// The one address, and the repository the About screen already names.
    /// </summary>
    private const string Newest =
        "https://api.github.com/repos/luxcan/photo-gallery/releases/latest";

    /// <summary>
    /// GitHub refuses a request that does not say who is asking, so this says.
    /// </summary>
    private const string Caller = "PhotoGallery";

    /// <summary>
    /// Long enough for a slow answer, short enough that a network which silently
    /// swallows the request does not hold a thread for a minute.
    /// </summary>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(8);

    private readonly HttpClient _http;

    /// <summary>
    /// One client for the life of the app, which is what an HttpClient is for.
    /// </summary>
    /// <remarks>
    /// A new one per call is the well-known way to run a machine out of sockets:
    /// each leaves its connection in TIME_WAIT for minutes after it is disposed.
    /// This app asks for one thing every few hours, so it would never reach that
    /// - but the habit is the thing that spreads, not the call count.
    /// </remarks>
    public GitHubReleases(HttpClient? http = null)
    {
        _http = http ?? new HttpClient();
        _http.Timeout = Patience;

        if (!_http.DefaultRequestHeaders.UserAgent.Any())
        {
            _http.DefaultRequestHeaders.UserAgent.Add(
                new ProductInfoHeaderValue(Caller, AppVersionHeader()));
        }
    }

    public async Task<PublishedRelease?> NewestAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            using HttpResponseMessage answer = await _http
                .GetAsync(Newest, cancellationToken)
                .ConfigureAwait(false);

            if (!answer.IsSuccessStatusCode)
            {
                return null;
            }

            await using Stream body = await answer.Content
                .ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);

            using JsonDocument json = await JsonDocument
                .ParseAsync(body, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return Read(json.RootElement);
        }
        catch (Exception ex) when (ex is HttpRequestException
                                      or TaskCanceledException
                                      or OperationCanceledException
                                      or JsonException
                                      or InvalidOperationException
                                      or UriFormatException)
        {
            // Not reachable, not answering, or not what was expected. None of
            // those is news.
            return null;
        }
    }

    /// <summary>What the answer says, where it says something this can use.</summary>
    /// <remarks>
    /// Internal so it can be read against canned answers. What this has to get
    /// right is the shape of somebody else's JSON, and that is worth pinning
    /// without a network.
    /// </remarks>
    internal static PublishedRelease? Read(JsonElement release)
    {
        if (release.TryGetProperty("draft", out JsonElement draft)
            && draft.ValueKind == JsonValueKind.True)
        {
            return null;
        }

        if (release.TryGetProperty("prerelease", out JsonElement early)
            && early.ValueKind == JsonValueKind.True)
        {
            return null;
        }

        if (!release.TryGetProperty("tag_name", out JsonElement tag)
            || tag.GetString() is not string tagged)
        {
            return null;
        }

        // "v1.0.1" and "1.0.1" are the same tag written two ways, and this
        // repository has used the first.
        string number = tagged.TrimStart('v', 'V');

        if (!Version.TryParse(number, out Version? published))
        {
            return null;
        }

        string name = release.TryGetProperty("name", out JsonElement titled)
                      && titled.GetString() is string given
                      && given.Length > 0
            ? given
            : tagged;

        string page = release.TryGetProperty("html_url", out JsonElement url)
                      && url.GetString() is string address
            ? address
            : "https://github.com/luxcan/photo-gallery/releases";

        return new PublishedRelease(published, name, page, Downloadable(release));
    }

    /// <summary>The executable the publish script attaches to every release.</summary>
    internal const string Executable = "PhotoGallery.exe";

    /// <summary>
    /// The executable attached to the release and the digest that proves it, or
    /// null when either is missing.
    /// </summary>
    /// <remarks>
    /// GitHub states a digest on every asset uploaded since mid-2025; the publish
    /// script also attaches <c>PhotoGallery.exe.sha256</c>, so a release still
    /// proves itself if that field is ever absent. Neither means no download.
    /// </remarks>
    private static ReleaseDownload? Downloadable(JsonElement release)
    {
        if (!release.TryGetProperty("assets", out JsonElement assets)
            || assets.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        JsonElement? executable = null;
        Uri? digestFile = null;

        foreach (JsonElement asset in assets.EnumerateArray())
        {
            string? assetName = asset.TryGetProperty("name", out JsonElement named)
                ? named.GetString()
                : null;

            if (string.Equals(assetName, Executable, StringComparison.OrdinalIgnoreCase))
            {
                executable = asset;
            }
            else if (string.Equals(assetName, Executable + ".sha256", StringComparison.OrdinalIgnoreCase))
            {
                digestFile = AddressOf(asset);
            }
        }

        if (executable is not JsonElement exe || AddressOf(exe) is not Uri address)
        {
            return null;
        }

        string? digest = exe.TryGetProperty("digest", out JsonElement stated)
                         && stated.ValueKind == JsonValueKind.String
            ? SelfReplacingInstaller.Hex(stated.GetString())
            : null;

        if (digest is null && digestFile is null)
        {
            return null;
        }

        long length = exe.TryGetProperty("size", out JsonElement size)
                      && size.TryGetInt64(out long bytes)
            ? bytes
            : 0;

        return new ReleaseDownload(address, length, digest, digestFile);
    }

    private static Uri? AddressOf(JsonElement asset) =>
        asset.TryGetProperty("browser_download_url", out JsonElement url)
        && Uri.TryCreate(url.GetString(), UriKind.Absolute, out Uri? address)
        && address.Scheme == Uri.UriSchemeHttps
            ? address
            : null;

    /// <summary>
    /// This build's own number, said in the header, because a request that
    /// names itself is one a maintainer can recognise in a log.
    /// </summary>
    private static string AppVersionHeader()
    {
        string number = PhotoGallery.Application.AppVersion.Number;

        // A product header will not take anything but a version-shaped value.
        return Version.TryParse(number, out Version? _) ? number : "1.0.0";
    }
}
