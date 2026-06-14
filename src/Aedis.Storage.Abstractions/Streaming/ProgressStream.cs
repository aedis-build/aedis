namespace Aedis.Storage.Abstractions.Streaming;

/// <summary>
///     Wrapper de <see cref="Stream" /> que reporta progresso durante leitura/escrita.
///     Usado para emitir eventos de progresso em uploads, independente do provider.
/// </summary>
internal sealed class ProgressStream : Stream
{
    private readonly Stream _innerStream;
    private readonly Action<long> _onProgress;
    private long _bytesRead;

    public ProgressStream(Stream innerStream, Action<long> onProgress) {
        _innerStream = innerStream ?? throw new ArgumentNullException(nameof(innerStream));
        _onProgress = onProgress ?? throw new ArgumentNullException(nameof(onProgress));
    }

    public override bool CanRead => _innerStream.CanRead;
    public override bool CanSeek => _innerStream.CanSeek;
    public override bool CanWrite => _innerStream.CanWrite;
    public override long Length => _innerStream.Length;

    public override long Position {
        get => _innerStream.Position;
        set => _innerStream.Position = value;
    }

    public override void Flush() => _innerStream.Flush();
    public override Task FlushAsync(CancellationToken cancellationToken) => _innerStream.FlushAsync(cancellationToken);
    public override long Seek(long offset, SeekOrigin origin) => _innerStream.Seek(offset, origin);
    public override void SetLength(long value) => _innerStream.SetLength(value);

    public override int Read(byte[] buffer, int offset, int count) {
        var bytesRead = _innerStream.Read(buffer, offset, count);
        _bytesRead += bytesRead;
        _onProgress(bytesRead);
        return bytesRead;
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) {
        var bytesRead = await _innerStream.ReadAsync(buffer.AsMemory(offset, count), cancellationToken);
        _bytesRead += bytesRead;
        _onProgress(bytesRead);
        return bytesRead;
    }

    public override int ReadByte() {
        var result = _innerStream.ReadByte();
        if (result != -1) {
            _bytesRead++;
            _onProgress(1);
        }

        return result;
    }

    public override void Write(byte[] buffer, int offset, int count) {
        _innerStream.Write(buffer, offset, count);
        _bytesRead += count;
        _onProgress(count);
    }

    public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) {
        await _innerStream.WriteAsync(buffer.AsMemory(offset, count), cancellationToken);
        _bytesRead += count;
        _onProgress(count);
    }

    public override void WriteByte(byte value) {
        _innerStream.WriteByte(value);
        _bytesRead++;
        _onProgress(1);
    }

    protected override void Dispose(bool disposing) {
        if (disposing) _innerStream.Dispose();
        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync() {
        await _innerStream.DisposeAsync();
        await base.DisposeAsync();
    }
}
