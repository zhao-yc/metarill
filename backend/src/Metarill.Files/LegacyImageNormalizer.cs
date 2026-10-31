using SkiaSharp;

namespace Metarill.Runtime.Files;

/// <summary>旧图片上传只解码JPEG/PNG；压缩体、像素、边长、输出及并发均有界。</summary>
internal static class LegacyImageNormalizer
{
    internal const long MaximumImageBytes = 10L * 1024 * 1024;
    private const long MaximumPixels = 16_000_000;
    private const int MaximumDimension = 8192;
    private static readonly SemaphoreSlim DecodeSlots = new(2, 2);

    /// <summary>重新编码剥离额外载荷和元数据，EXIF方向应用于真实像素，不保留未知图片格式。</summary>
    internal static async Task<MemoryStream> NormalizeAsync(string name, long size, Stream source, long fileMaximum, CancellationToken token = default)
    {
        var maximum = Math.Min(MaximumImageBytes, fileMaximum);
        if (size <= 0 || size > maximum) throw new InvalidOperationException("图片超过10MiB或文件库大小上限。");
        await DecodeSlots.WaitAsync(token);
        try
        {
            using var compressed = new MemoryStream((int)size);
            var buffer = new byte[65536]; long actual = 0;
            while (true)
            {
                var read = await source.ReadAsync(buffer, token); if (read == 0) break;
                actual += read; if (actual > size || actual > maximum) throw new InvalidOperationException("实际图片大小超过上传声明。");
                compressed.Write(buffer, 0, read);
            }
            if (actual != size) throw new InvalidOperationException("实际图片大小与上传声明不一致。");
            compressed.Position = 0;
            using var codec = SKCodec.Create(compressed) ?? throw new InvalidOperationException("文件不是有效的JPEG/PNG图片。");
            var format = Path.GetExtension(name).ToLowerInvariant() == ".png" ? SKEncodedImageFormat.Png : SKEncodedImageFormat.Jpeg;
            if (codec.EncodedFormat != format) throw new InvalidOperationException("图片实际编码与扩展名不一致。");
            var info = codec.Info;
            if (info.Width <= 0 || info.Height <= 0 || info.Width > MaximumDimension || info.Height > MaximumDimension || (long)info.Width * info.Height > MaximumPixels)
                throw new InvalidOperationException("图片像素或边长超过服务端上限。");
            using var decoded = new SKBitmap();
            if (!decoded.TryAllocPixels(new SKImageInfo(info.Width, info.Height, SKColorType.Rgba8888, SKAlphaType.Premul))) throw new InvalidOperationException("图片像素内存分配失败。");
            if (codec.GetPixels(decoded.Info, decoded.GetPixels()) != SKCodecResult.Success) throw new InvalidOperationException("图片数据损坏或不完整。");
            using var oriented = Orient(decoded, codec.EncodedOrigin);
            using var image = SKImage.FromBitmap(oriented ?? decoded);
            using var encoded = image.Encode(format, 90) ?? throw new InvalidOperationException("图片重新编码失败。");
            if (encoded.Size > maximum) throw new InvalidOperationException("图片处理结果超过服务端大小上限。");
            var result = new MemoryStream((int)encoded.Size);
            encoded.SaveTo(result); result.Position = 0; return result;
        }
        finally { DecodeSlots.Release(); }
    }

    /// <summary>根据标准EXIF八种方向变换像素；移除方向元数据后仍保持照片显示方向。</summary>
    private static SKBitmap? Orient(SKBitmap source, SKEncodedOrigin origin)
    {
        if (origin == SKEncodedOrigin.TopLeft) return null;
        var swapped = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var target = new SKBitmap();
        if (!target.TryAllocPixels(new SKImageInfo(swapped ? source.Height : source.Width, swapped ? source.Width : source.Height, SKColorType.Rgba8888, SKAlphaType.Premul))) { target.Dispose(); throw new InvalidOperationException("方向变换像素内存分配失败。"); }
        try
        {
            var w = source.Width; var h = source.Height;
            var matrix = origin switch
            {
                SKEncodedOrigin.TopRight => new SKMatrix(-1, 0, w, 0, 1, 0, 0, 0, 1),
                SKEncodedOrigin.BottomRight => new SKMatrix(-1, 0, w, 0, -1, h, 0, 0, 1),
                SKEncodedOrigin.BottomLeft => new SKMatrix(1, 0, 0, 0, -1, h, 0, 0, 1),
                SKEncodedOrigin.LeftTop => new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1),
                SKEncodedOrigin.RightTop => new SKMatrix(0, -1, h, 1, 0, 0, 0, 0, 1),
                SKEncodedOrigin.RightBottom => new SKMatrix(0, -1, h, -1, 0, w, 0, 0, 1),
                SKEncodedOrigin.LeftBottom => new SKMatrix(0, 1, 0, -1, 0, w, 0, 0, 1),
                _ => throw new InvalidOperationException("图片方向无效。")
            };
            using var canvas = new SKCanvas(target); canvas.Clear(SKColors.Transparent); canvas.SetMatrix(matrix); canvas.DrawBitmap(source, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest)); canvas.Flush(); return target;
        }
        catch { target.Dispose(); throw; }
    }
}
