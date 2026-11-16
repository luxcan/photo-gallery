namespace PhotoGallery.Infrastructure.Sharing;

/// <summary>
/// Reads another stream until a set number of bytes has come out of it, and
/// refuses to read further.
/// </summary>
/// <remarks>
/// Put between a decompressor and the JSON reader. A few megabytes of gzip can
/// unpack to gigabytes, and the reader would hold on to all of it - one string
/// that size is enough to run the app out of memory, and nothing above the
/// reader can catch that. Counting the bytes as they come out stops the file at
/// the limit instead, as one more file that could not be read.
/// </remarks>
internal sealed class LimitedReadStream : Stream
{
    private readonly Stream _inner;
    private readonly long _limit;
    private long _read;

    public LimitedReadStream(Stream inner, long limit)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentOutOfRangeException.ThrowIfNegative(limit);

        _inner = inner;
        _limit = limit;
    }

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => _read;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer) => Counted(_inner.Read(Allowed(buffer)));

    public override Task<int> ReadAsync(
        byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        Counted(await _inner.ReadAsync(Allowed(buffer), cancellationToken).ConfigureAwait(false));

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _inner.Dispose();
        }

        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        await _inner.DisposeAsync().ConfigureAwait(false);
        await base.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Asks for one byte past the limit and no more, so a file exactly at the
    /// limit still reads whole and one byte over is caught.
    /// </summary>
    private Span<byte> Allowed(Span<byte> buffer) =>
        buffer[..(int)Math.Min(buffer.Length, _limit - _read + 1)];

    private Memory<byte> Allowed(Memory<byte> buffer) =>
        buffer[..(int)Math.Min(buffer.Length, _limit - _read + 1)];

    private int Counted(int read)
    {
        _read += read;

        return _read > _limit
            ? throw new InvalidDataException(
                $"The file unpacks to more than {_limit / (1024 * 1024)} MB, far more than any library writes.")
            : read;
    }
}
