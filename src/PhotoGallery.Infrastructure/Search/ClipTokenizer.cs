using System.Text.RegularExpressions;
using Microsoft.ML.Tokenizers;

namespace PhotoGallery.Infrastructure.Search;

/// <summary>
/// Turns a typed phrase into the 77 numbers the text encoder expects.
/// </summary>
/// <remarks>
/// Built on <c>Microsoft.ML.Tokenizers</c> rather than written here. A
/// byte-pair encoder is a few hundred lines and every one of them is a chance to
/// be subtly wrong, and subtly wrong does not throw: it returns a confident
/// vector for the wrong words, so a search for "beach" quietly answers with
/// something else. Checked against the documented output of CLIP's own
/// tokenizer - "a diagram" is 320 then 22697 - which is what says the
/// configuration below reproduces the scheme rather than merely resembling it.
///
/// <para>Character-level, where CLIP is byte-level. For anything typed in the
/// Latin alphabet the two agree exactly, because the byte-to-character mapping
/// CLIP uses is the identity over printable ASCII. A query in Chinese would
/// tokenize differently - and this is an English-only model, so such a query has
/// no good answer either way.</para>
/// </remarks>
internal sealed class ClipTokenizer
{
    /// <summary>How many tokens the encoder takes, padding included.</summary>
    public const int ContextLength = 77;

    private const int StartOfText = 49406;

    private const int EndOfText = 49407;

    /// <summary>
    /// CLIP's own pre-tokenization pattern, which decides where words end before
    /// any merging happens.
    /// </summary>
    private static readonly Regex Words = new(
        @"<\|startoftext\|>|<\|endoftext\|>|'s|'t|'re|'ve|'m|'ll|'d|[\p{L}]+|[\p{N}]|[^\s\p{L}\p{N}]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Dictionary<string, int> SpecialTokens = new()
    {
        ["<|startoftext|>"] = StartOfText,
        ["<|endoftext|>"] = EndOfText,
    };

    private readonly BpeTokenizer _tokenizer;

    public ClipTokenizer(string vocabularyPath, string mergesPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vocabularyPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(mergesPath);

        using FileStream vocabulary = File.OpenRead(vocabularyPath);
        using FileStream merges = File.OpenRead(mergesPath);

        _tokenizer = BpeTokenizer.Create(
            vocabStream: vocabulary,
            mergesStream: merges,
            preTokenizer: new RegexPreTokenizer(Words, SpecialTokens),
            normalizer: LowerCaseNormalizer.Instance,
            specialTokens: SpecialTokens,
            unknownToken: "<|endoftext|>",
            continuingSubwordPrefix: null,
            endOfWordSuffix: "</w>",
            fuseUnknownTokens: false);
    }

    /// <summary>
    /// The phrase as exactly <see cref="ContextLength"/> ids: start, the words,
    /// end, and end again for the rest.
    /// </summary>
    /// <remarks>
    /// A phrase longer than the window is cut to fit and still closed properly.
    /// Truncating without the end marker would leave the encoder reading a
    /// sentence that never finishes, and it pools at that marker.
    /// </remarks>
    public int[] Encode(string phrase)
    {
        int[] ids = new int[ContextLength];
        Array.Fill(ids, EndOfText);

        ids[0] = StartOfText;
        if (string.IsNullOrWhiteSpace(phrase))
        {
            ids[1] = EndOfText;
            return ids;
        }

        IReadOnlyList<int> words = _tokenizer.EncodeToIds(phrase);
        int room = ContextLength - 2;
        int taken = Math.Min(words.Count, room);

        for (int i = 0; i < taken; i++)
        {
            ids[i + 1] = words[i];
        }

        ids[taken + 1] = EndOfText;
        return ids;
    }
}
