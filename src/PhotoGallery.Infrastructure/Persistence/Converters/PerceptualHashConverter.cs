using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using PhotoGallery.Domain.Assets;

namespace PhotoGallery.Infrastructure.Persistence.Converters;

/// <summary>
/// Stores a perceptual hash as 16 hex characters.
/// </summary>
/// <remarks>
/// Text rather than an integer: SQLite has no unsigned 64-bit type, and nothing
/// queries these in SQL - Hamming distance is computed in memory over the whole
/// set, so readability in a database browser is worth more than eight bytes.
/// </remarks>
public sealed class PerceptualHashConverter : ValueConverter<PerceptualHash, string>
{
    public PerceptualHashConverter()
        : base(hash => hash.ToString(), text => PerceptualHash.Parse(text))
    {
    }
}
