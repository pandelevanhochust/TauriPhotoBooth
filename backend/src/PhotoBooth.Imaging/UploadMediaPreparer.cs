using System;
using System.IO;
using SkiaSharp;

namespace PhotoBooth.Imaging;

/// <summary>Chuẩn bị ảnh trước khi gửi lên backend: thu nhỏ, xoay đúng chiều, mã hoá lại JPEG (bỏ toàn bộ EXIF, kể cả GPS).</summary>
public static class UploadMediaPreparer
{
    /// <summary>Ảnh lẻ cho khách tải: cạnh dài tối đa 2560px, JPEG 90.</summary>
    public const int OriginalMaxSide = 2560;
    public const int ThumbnailMaxSide = 400;

    public static void PrepareOriginal(string source, string target) => WriteResizedJpeg(source, target, OriginalMaxSide, 90);

    public static void PrepareThumbnail(string source, string target) => WriteResizedJpeg(source, target, ThumbnailMaxSide, 80);

    /// <summary>
    /// Thu nhỏ để cạnh dài không quá <paramref name="maxSide"/> (không phóng to), áp hướng xoay EXIF vào điểm ảnh,
    /// rồi ghi JPEG mới. Skia không chép metadata nên EXIF/GPS của ảnh gốc bị bỏ.
    /// </summary>
    public static void WriteResizedJpeg(string source, string target, int maxSide, int quality)
    {
        using var codec = SKCodec.Create(source) ?? throw new InvalidDataException($"Không đọc được ảnh: {source}");
        var info = codec.Info;
        float ratio = Math.Min(1f, (float)maxSide / Math.Max(info.Width, info.Height));

        var decodeSize = codec.GetScaledDimensions(ratio);
        var decodeInfo = new SKImageInfo(decodeSize.Width, decodeSize.Height, SKColorType.Rgba8888, SKAlphaType.Premul, SKColorSpace.CreateSrgb());
        using var decoded = new SKBitmap(decodeInfo);
        var result = codec.GetPixels(decodeInfo, decoded.GetPixels());
        if (result != SKCodecResult.Success && result != SKCodecResult.IncompleteInput)
        {
            throw new InvalidDataException($"Giải mã ảnh thất bại: {result}");
        }

        int w = Math.Max(1, (int)Math.Round(info.Width * ratio));
        int h = Math.Max(1, (int)Math.Round(info.Height * ratio));
        using var resized = decoded.Width == w && decoded.Height == h
            ? decoded.Copy()
            : decoded.Resize(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul, SKColorSpace.CreateSrgb()),
                new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));

        using var oriented = ApplyOrigin(resized, codec.EncodedOrigin);
        using var image = SKImage.FromBitmap(oriented);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, quality)
            ?? throw new InvalidDataException("Mã hoá JPEG thất bại");

        string? dir = Path.GetDirectoryName(target);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        string tmp = target + ".tmp";
        using (var fs = File.Create(tmp)) data.SaveTo(fs);
        File.Move(tmp, target, overwrite: true);
    }

    /// <summary>Xoay / lật điểm ảnh theo thẻ Orientation của EXIF, vì EXIF sẽ bị bỏ khi mã hoá lại.</summary>
    private static SKBitmap ApplyOrigin(SKBitmap src, SKEncodedOrigin origin)
    {
        if (origin is SKEncodedOrigin.TopLeft or SKEncodedOrigin.Default) return src.Copy();

        bool swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var dst = new SKBitmap(swap ? src.Height : src.Width, swap ? src.Width : src.Height, src.ColorType, src.AlphaType);
        using var canvas = new SKCanvas(dst);
        float w = src.Width, h = src.Height;
        // x' = a·x + b·y + c ; y' = d·x + e·y + f
        var (a, b, c, d, e, f) = origin switch
        {
            SKEncodedOrigin.TopRight => (-1f, 0f, w, 0f, 1f, 0f),     // lật ngang
            SKEncodedOrigin.BottomRight => (-1f, 0f, w, 0f, -1f, h),  // xoay 180°
            SKEncodedOrigin.BottomLeft => (1f, 0f, 0f, 0f, -1f, h),   // lật dọc
            SKEncodedOrigin.LeftTop => (0f, 1f, 0f, 1f, 0f, 0f),      // chuyển vị
            SKEncodedOrigin.RightTop => (0f, -1f, h, 1f, 0f, 0f),     // xoay 90° theo chiều kim đồng hồ
            SKEncodedOrigin.RightBottom => (0f, -1f, h, -1f, 0f, w),  // chuyển vị ngược
            SKEncodedOrigin.LeftBottom => (0f, 1f, 0f, -1f, 0f, w),   // xoay 90° ngược chiều kim đồng hồ
            _ => (1f, 0f, 0f, 0f, 1f, 0f),
        };
        canvas.SetMatrix(new SKMatrix(a, b, c, d, e, f, 0, 0, 1));
        canvas.DrawBitmap(src, 0, 0);
        return dst;
    }
}
