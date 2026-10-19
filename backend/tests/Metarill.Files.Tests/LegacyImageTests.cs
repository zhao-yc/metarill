using System.Buffers.Binary;
using System.Text;
using Metarill.Runtime.Files;
using SkiaSharp;
using Xunit;

namespace Metarill.Files.Tests;

/// <summary>无数据库时也实际加载native并解码；Linux/Windows CI均执行，不把编译当作native验收。</summary>
[Trait("Native", "SkiaSharp")]
public sealed class LegacyImageTests
{
    /// <summary>真实JPEG/PNG经过像素重编码，尾部附带内容不能进入保存对象。</summary>
    [Theory]
    [InlineData(".png", SKEncodedImageFormat.Png)]
    [InlineData(".jpg", SKEncodedImageFormat.Jpeg)]
    public async Task Native_decode_and_reencode_preserve_pixels_without_appended_payload(string extension, SKEncodedImageFormat format)
    {
        var encoded = Image(format); var suffix = Encoding.UTF8.GetBytes("不应保留的尾随载荷"); var input = encoded.Concat(suffix).ToArray();
        using var result = await LegacyImageNormalizer.NormalizeAsync("image" + extension, input.Length, new MemoryStream(input), 10 * 1024 * 1024);
        var saved = result.ToArray(); Assert.False(saved.AsSpan().IndexOf(suffix) >= 0);
        using var codec = SKCodec.Create(result); Assert.NotNull(codec); Assert.Equal(format, codec.EncodedFormat); Assert.Equal(2, codec.Info.Width); Assert.Equal(3, codec.Info.Height);
        using var decoded = SKBitmap.Decode(saved); Assert.NotNull(decoded); Assert.True(decoded.GetPixel(0, 0).Red > 200);
    }

    /// <summary>伪图片、格式混淆、截断流与大小声明不符都必须失败。</summary>
    [Fact]
    public async Task Invalid_content_and_declared_sizes_are_rejected()
    {
        var png = Image(SKEncodedImageFormat.Png);
        await Assert.ThrowsAsync<InvalidOperationException>(() => LegacyImageNormalizer.NormalizeAsync("fake.png", 4, new MemoryStream("html"u8.ToArray()), 4096));
        await Assert.ThrowsAsync<InvalidOperationException>(() => LegacyImageNormalizer.NormalizeAsync("wrong.jpg", png.Length, new MemoryStream(png), 4096));
        await Assert.ThrowsAsync<InvalidOperationException>(() => LegacyImageNormalizer.NormalizeAsync("truncated.png", png.Length, new MemoryStream(png[..(png.Length / 2)]), 4096));
        await Assert.ThrowsAsync<InvalidOperationException>(() => LegacyImageNormalizer.NormalizeAsync("extra.png", png.Length - 1, new MemoryStream(png), 4096));
        await Assert.ThrowsAsync<InvalidOperationException>(() => LegacyImageNormalizer.NormalizeAsync("large.png", LegacyImageNormalizer.MaximumImageBytes + 1, new MemoryStream(), 100 * 1024 * 1024));
    }

    /// <summary>解析头部后就拒绝像素炸弹，不能先为攻击者声明的画布分配内存。</summary>
    [Fact]
    public async Task Large_png_header_is_rejected_before_bitmap_allocation()
    {
        var png = Image(SKEncodedImageFormat.Png);
        BinaryPrimitives.WriteInt32BigEndian(png.AsSpan(16, 4), 8192); BinaryPrimitives.WriteInt32BigEndian(png.AsSpan(20, 4), 8192);
        uint crc = uint.MaxValue;
        foreach (var value in png.AsSpan(12, 17)) { crc ^= value; for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) == 1 ? 0xedb88320u : 0); }
        BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(29, 4), ~crc);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => LegacyImageNormalizer.NormalizeAsync("bomb.png", png.Length, new MemoryStream(png), 4096));
        Assert.Contains("像素", error.Message);
    }

    /// <summary>EXIF旋转实际应用到像素尺寸，剥离元数据后仍保持手机照片方向。</summary>
    [Fact]
    public async Task Exif_orientation_is_applied_before_metadata_is_removed()
    {
        var jpeg = Image(SKEncodedImageFormat.Jpeg);
        var exif = Convert.FromHexString("45786966000049492A0008000000010012010300010000000600000000000000");
        var segment = new byte[] { 0xff, 0xe1, 0, (byte)(exif.Length + 2) }.Concat(exif).ToArray();
        var source = jpeg[..2].Concat(segment).Concat(jpeg[2..]).ToArray();
        using var normalized = await LegacyImageNormalizer.NormalizeAsync("phone.jpg", source.Length, new MemoryStream(source), 4096);
        using var codec = SKCodec.Create(normalized); Assert.Equal(3, codec.Info.Width); Assert.Equal(2, codec.Info.Height); Assert.Equal(SKEncodedOrigin.TopLeft, codec.EncodedOrigin);
    }

    /// <summary>测试图片由实际native编码器构造，能暴露缺失动态库或不兼容架构。</summary>
    private static byte[] Image(SKEncodedImageFormat format)
    {
        using var bitmap = new SKBitmap(2, 3); bitmap.Erase(SKColors.Red); using var image = SKImage.FromBitmap(bitmap); using var encoded = image.Encode(format, 90); return encoded.ToArray();
    }
}
