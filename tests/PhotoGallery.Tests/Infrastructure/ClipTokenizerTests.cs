using PhotoGallery.Infrastructure.Search;

namespace PhotoGallery.Tests.Infrastructure;

/// <summary>
/// That the tokenizer reproduces CLIP's scheme rather than merely resembling it.
/// </summary>
/// <remarks>
/// The ids asserted here are the documented output of CLIP's own tokenizer -
/// "a diagram" is 320 then 22697, "a dog" 320 then 1929, "a cat" 320 then 2368.
/// They are worth pinning because this is the one part of the feature that fails
/// silently in both directions: a wrong pre-tokenizer, a missing end-of-word
/// suffix or a case-sensitive normalizer all produce valid ids for the wrong
/// words, and the search then answers a question nobody asked.
///
/// <para>Skipped unless <c>PHOTOGALLERY_CLIP_MODELS</c> names the folder holding
/// the vocabulary and merges, since 1.4 MB of them is still not this
/// repository's to carry.</para>
/// </remarks>
public sealed class ClipTokenizerTests
{
    private const string ModelFolderVariable = "PHOTOGALLERY_CLIP_MODELS";

    [SkippableTheory]
    [InlineData("a diagram", 320, 22697)]
    [InlineData("a dog", 320, 1929)]
    [InlineData("a cat", 320, 2368)]
    public void Encode_MatchesTheReferenceTokenizer(string phrase, int first, int second)
    {
        ClipTokenizer tokenizer = Load();

        int[] ids = tokenizer.Encode(phrase);

        Assert.Equal(49406, ids[0]);
        Assert.Equal(first, ids[1]);
        Assert.Equal(second, ids[2]);
        Assert.Equal(49407, ids[3]);
    }

    [SkippableFact]
    public void Encode_AlwaysFillsTheWindowAndClosesTheSentence()
    {
        // The encoder pools at the end marker, so a phrase that overruns has to
        // be cut and still closed. Left open it would pool at whatever the last
        // word happened to be.
        ClipTokenizer tokenizer = Load();

        int[] ids = tokenizer.Encode(string.Join(' ', Enumerable.Repeat("beach", 200)));

        Assert.Equal(ClipTokenizer.ContextLength, ids.Length);
        Assert.Equal(49406, ids[0]);
        Assert.Equal(49407, ids[^1]);
    }

    [SkippableFact]
    public void Encode_IgnoresCaseSoTypingIsNotAnExactScience()
    {
        ClipTokenizer tokenizer = Load();

        Assert.Equal(tokenizer.Encode("A DOG"), tokenizer.Encode("a dog"));
    }

    [SkippableFact]
    public void Encode_OfNothingIsAnEmptySentenceRatherThanARaggedWindow()
    {
        ClipTokenizer tokenizer = Load();

        int[] ids = tokenizer.Encode("   ");

        Assert.Equal(ClipTokenizer.ContextLength, ids.Length);
        Assert.Equal(49406, ids[0]);
        Assert.Equal(49407, ids[1]);
    }

    private static ClipTokenizer Load()
    {
        string? folder = Environment.GetEnvironmentVariable(ModelFolderVariable);
        Skip.If(
            string.IsNullOrWhiteSpace(folder),
            $"Set {ModelFolderVariable} to the folder holding the CLIP vocabulary and merges.");

        string vocabulary = Path.Combine(folder!, "clip_vit_l14_vocab.json");
        string merges = Path.Combine(folder!, "clip_vit_l14_merges.txt");

        Skip.IfNot(
            File.Exists(vocabulary) && File.Exists(merges),
            $"{ModelFolderVariable} does not hold the CLIP vocabulary and merges.");

        return new ClipTokenizer(vocabulary, merges);
    }
}
