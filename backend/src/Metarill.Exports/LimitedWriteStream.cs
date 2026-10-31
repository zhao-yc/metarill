namespace Metarill.Runtime.Exports;

/// <summary>压缩包写入也受磁盘限额约束，不能等完整文件落盘后才检查大小。</summary>
internal sealed class LimitedWriteStream(Stream inner, long maximum, CancellationToken cancellationToken) : Stream
{
    public override bool CanRead => inner.CanRead;
    public override bool CanSeek => inner.CanSeek;
    public override bool CanWrite => inner.CanWrite;
    public override long Length => inner.Length;
    public override long Position { get => inner.Position; set { Check(value); inner.Position = value; } }
    /// <summary>写入和扩容前统一检查边界与请求取消。</summary>
    private void Check(long end)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (end < 0 || end > maximum) throw new InvalidOperationException("导出文件超过大小上限。");
    }
    public override void Write(byte[] buffer, int offset, int count) { Check(checked(Position + count)); inner.Write(buffer, offset, count); }
    public override void Write(ReadOnlySpan<byte> buffer) { Check(checked(Position + buffer.Length)); inner.Write(buffer); }
    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken token = default)
    { Check(checked(Position + buffer.Length)); await inner.WriteAsync(buffer, token); }
    public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
    public override int Read(Span<byte> buffer) => inner.Read(buffer);
    public override void Flush() { cancellationToken.ThrowIfCancellationRequested(); inner.Flush(); }
    public override Task FlushAsync(CancellationToken token) { cancellationToken.ThrowIfCancellationRequested(); return inner.FlushAsync(token); }
    public override void SetLength(long value) { Check(value); inner.SetLength(value); }
    public override long Seek(long offset, SeekOrigin origin)
    {
        var end = origin switch { SeekOrigin.Begin => offset, SeekOrigin.Current => checked(Position + offset), SeekOrigin.End => checked(Length + offset), _ => throw new ArgumentOutOfRangeException(nameof(origin)) };
        Check(end); return inner.Seek(offset, origin);
    }
}
