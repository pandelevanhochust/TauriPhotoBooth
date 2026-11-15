using System;
using System.Collections.Generic;
using System.IO;
using PhotoBooth.Core.Models;
using SkiaSharp;

namespace PhotoBooth.Imaging.Compositor;

/// <summary>
/// Chuẩn bị frame PNG để ảnh luôn nằm DƯỚI frame.
/// Frame của CMS có thể tô màu đánh số vào ô ảnh làm mẫu thay vì để trong suốt; những ô đó được khoét
/// trong suốt, còn hoạ tiết nằm ngoài ô (kể cả phần đè lên mép ô) giữ nguyên.
/// </summary>
public static class FrameOverlayHelper
{
    /// <summary>
    /// Giải mã frame luôn kèm kênh alpha. PNG không có alpha (ô đen đặc làm mẫu) giải mã thường sẽ ra bitmap Opaque,
    /// khi đó khoét trong suốt không có tác dụng.
    /// </summary>
    public static SKBitmap? DecodeWithAlpha(string path)
    {
        using var codec = SKCodec.Create(path);
        if (codec == null) return null;
        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        return SKBitmap.Decode(codec, info);
    }

    /// <summary>
    /// Lấy mẫu alpha của frame bên trong ô. Ô được coi là trong suốt khi phần lớn điểm mẫu có alpha thấp.
    /// Toạ độ ô nằm trong hệ canvasWidth x canvasHeight.
    /// </summary>
    public static bool IsHoleTransparent(SKBitmap overlay, HoleLocation hole, int canvasWidth, int canvasHeight)
    {
        float scaleX = (float)overlay.Width / canvasWidth;
        float scaleY = (float)overlay.Height / canvasHeight;
        const int grid = 5;
        int total = 0, transparent = 0;

        for (int gy = 1; gy <= grid; gy++)
        {
            for (int gx = 1; gx <= grid; gx++)
            {
                int px = (int)((hole.X + hole.Width * gx / (float)(grid + 1)) * scaleX);
                int py = (int)((hole.Y + hole.Height * gy / (float)(grid + 1)) * scaleY);
                if (px < 0 || py < 0 || px >= overlay.Width || py >= overlay.Height) continue;
                total++;
                if (overlay.GetPixel(px, py).Alpha < 64) transparent++;
            }
        }

        return total == 0 || transparent * 2 > total;
    }

    /// <summary>Khoét trong suốt các ô bị tô màu, sửa trực tiếp trên bitmap. Trả về số ô đã khoét.</summary>
    public static int OpenPaintedHoles(SKBitmap overlay, IReadOnlyList<HoleLocation> holes, int canvasWidth, int canvasHeight)
    {
        float scaleX = (float)overlay.Width / canvasWidth;
        float scaleY = (float)overlay.Height / canvasHeight;
        int opened = 0;

        using var canvas = new SKCanvas(overlay);
        using var clear = new SKPaint { BlendMode = SKBlendMode.Clear, IsAntialias = false };

        foreach (var hole in holes)
        {
            if (IsHoleTransparent(overlay, hole, canvasWidth, canvasHeight)) continue;
            canvas.DrawRect(new SKRect(
                hole.X * scaleX,
                hole.Y * scaleY,
                (hole.X + hole.Width) * scaleX,
                (hole.Y + hole.Height) * scaleY), clear);
            opened++;
        }

        canvas.Flush();
        return opened;
    }

    /// <summary>
    /// Ghi bản frame đã khoét ô ra file "{tên}.open-{hash}.png" cạnh file gốc (dùng lại nếu đã có).
    /// Frame không có ô tô màu thì trả về đường dẫn gốc.
    /// </summary>
    public static string EnsureOpenOverlayFile(string overlayPath, IReadOnlyList<HoleLocation> holes)
    {
        // FNV-1a ổn định giữa các lần chạy (HashCode.Combine bị ngẫu nhiên hoá theo tiến trình)
        uint hash = 2166136261;
        foreach (var h in holes)
        {
            foreach (int v in new[] { h.X, h.Y, h.Width, h.Height })
            {
                hash = (hash ^ (uint)v) * 16777619;
            }
        }

        string dir = Path.GetDirectoryName(overlayPath) ?? ".";
        string openPath = Path.Combine(dir, $"{Path.GetFileNameWithoutExtension(overlayPath)}.open-{hash:x8}.png");
        if (File.Exists(openPath)) return openPath;

        using var bitmap = DecodeWithAlpha(overlayPath);
        if (bitmap == null) return overlayPath;

        // Toạ độ ô nằm trong hệ pixel của chính frame PNG
        if (OpenPaintedHoles(bitmap, holes, bitmap.Width, bitmap.Height) == 0) return overlayPath;

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        string tmp = openPath + ".tmp";
        using (var fs = File.Create(tmp))
        {
            data.SaveTo(fs);
        }
        File.Move(tmp, openPath, overwrite: true);
        return openPath;
    }
}
