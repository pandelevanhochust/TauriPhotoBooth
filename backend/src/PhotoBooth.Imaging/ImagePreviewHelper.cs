using System;
using System.IO;
using SkiaSharp;

namespace PhotoBooth.Imaging;

/// <summary>Tạo bản xem trước nhỏ của ảnh gốc (ảnh DSLR 24MP quá nặng để gửi sang UI).</summary>
public static class ImagePreviewHelper
{
    /// <summary>Giải mã thu nhỏ để cạnh dài nhất không quá <paramref name="maxSide"/>, mã hoá lại JPEG.</summary>
    public static byte[] CreateJpegPreview(string path, int maxSide = 1600, int quality = 85)
    {
        using var codec = SKCodec.Create(path) ?? throw new InvalidDataException($"Không đọc được ảnh: {path}");
        var info = codec.Info;
        float ratio = Math.Min(1f, (float)maxSide / Math.Max(info.Width, info.Height));

        // Giải mã ở cỡ gần nhất mà codec hỗ trợ (JPEG giảm 1/2, 1/4, 1/8 rất nhanh), rồi thu nhỏ nốt
        var decodeSize = codec.GetScaledDimensions(ratio);
        var decodeInfo = new SKImageInfo(decodeSize.Width, decodeSize.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var decoded = new SKBitmap(decodeInfo);
        var result = codec.GetPixels(decodeInfo, decoded.GetPixels());
        if (result != SKCodecResult.Success && result != SKCodecResult.IncompleteInput)
        {
            throw new InvalidDataException($"Giải mã ảnh thất bại: {result}");
        }

        int targetW = Math.Max(1, (int)Math.Round(info.Width * ratio));
        int targetH = Math.Max(1, (int)Math.Round(info.Height * ratio));
        using var finalBitmap = decoded.Width == targetW && decoded.Height == targetH
            ? decoded.Copy()
            : decoded.Resize(new SKImageInfo(targetW, targetH), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));

        using var image = SKImage.FromBitmap(finalBitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, quality);
        return data.ToArray();
    }
}
