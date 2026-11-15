using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PhotoBooth.Core.Interfaces;
using PhotoBooth.Core.Models;
using SkiaSharp;

namespace PhotoBooth.Imaging.Compositor;

public class NativeSkiaCompositor : IImageComposer
{
    public const int DefaultWidth = 1200;
    public const int DefaultHeight = 1800;

    public Task<string> ComposePrintSheetAsync(
        FrameTemplate template,
        IReadOnlyList<string> rawPhotoPaths,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            int targetWidth = template.CanvasWidth > 0 ? template.CanvasWidth : DefaultWidth;
            int targetHeight = template.CanvasHeight > 0 ? template.CanvasHeight : DefaultHeight;

            var imageInfo = new SKImageInfo(targetWidth, targetHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var surface = SKSurface.Create(imageInfo);
            var canvas = surface.Canvas;

            // 1. Draw Background
            canvas.Clear(SKColors.White);

            // Create ColorFilter from template.FilterCode if specified
            using var colorFilter = GetColorFilter(template.FilterCode);

            // 2. Ảnh luôn nằm DƯỚI frame để hoạ tiết đè lên mép ô không bị che
            for (int i = 0; i < template.Holes.Count && i < rawPhotoPaths.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string photoPath = rawPhotoPaths[i];
                if (!File.Exists(photoPath)) continue;

                using var rawBitmap = SKBitmap.Decode(photoPath);
                if (rawBitmap == null) continue;

                RenderPhotoInHole(canvas, rawBitmap, template.Holes[i], colorFilter);
            }

            // 3. Frame phủ lên trên; ô bị tô màu mẫu được khoét trong suốt trước để lộ ảnh
            if (!string.IsNullOrEmpty(template.OverlayImagePath) && File.Exists(template.OverlayImagePath))
            {
                using var overlayBitmap = FrameOverlayHelper.DecodeWithAlpha(template.OverlayImagePath);
                if (overlayBitmap != null)
                {
                    FrameOverlayHelper.OpenPaintedHoles(overlayBitmap, template.Holes, targetWidth, targetHeight);
                    var destRect = new SKRect(0, 0, targetWidth, targetHeight);
                    using var paint = new SKPaint();
                    canvas.DrawBitmap(overlayBitmap, destRect, paint);
                }
            }

            // 4. If Strip2x6 layout and duplicate enabled, mirror/clone left strip to right strip
            if (template.SupportedPaperType == PaperType.DoubleStrip2x6 && targetWidth > 0)
            {
                // Left strip occupies X: 0 to halfW, right strip X: halfW to targetWidth
                int halfW = targetWidth / 2;
                using var currentSnap = surface.Snapshot();
                using var leftCrop = SKBitmap.FromImage(currentSnap);
                if (leftCrop != null)
                {
                    var srcRect = new SKRect(0, 0, halfW, targetHeight);
                    var destRect = new SKRect(halfW, 0, targetWidth, targetHeight);
                    using var paint = new SKPaint();
                    canvas.DrawBitmap(leftCrop, srcRect, destRect, paint);
                }
            }

            // Ensure destination folder exists
            string dir = Path.GetDirectoryName(outputPath)!;
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            // 5. Encode and Save to Disk (JPEG 95% quality)
            using var image = surface.Snapshot();
            using var data = image.Encode(SKEncodedImageFormat.Jpeg, 95);
            using var fileStream = File.OpenWrite(outputPath);
            data.SaveTo(fileStream);

            return outputPath;
        }, cancellationToken);
    }

    /// <summary>
    /// Vẽ ảnh phủ kín ô, có áp phóng to và độ lệch người dùng chỉnh ở bước chọn ảnh.
    /// Công thức khớp với bản xem trước ở UI (FavoriPhotoSelectScreen / photoTransform.ts).
    /// </summary>
    private static void RenderPhotoInHole(SKCanvas canvas, SKBitmap bitmap, HoleLocation hole, SKColorFilter? colorFilter)
    {
        if (hole.Width <= 0 || hole.Height <= 0 || bitmap.Width <= 0 || bitmap.Height <= 0) return;

        double userScale = double.IsFinite(hole.PhotoScale) ? Math.Clamp(hole.PhotoScale, 1.0, 5.0) : 1.0;
        double k = Math.Max((double)hole.Width / bitmap.Width, (double)hole.Height / bitmap.Height) * userScale;
        double drawW = bitmap.Width * k;
        double drawH = bitmap.Height * k;

        // Giới hạn độ lệch để ảnh luôn phủ kín ô
        double maxX = (drawW / hole.Width - 1) / 2;
        double maxY = (drawH / hole.Height - 1) / 2;
        double ox = double.IsFinite(hole.PhotoOffsetX) ? Math.Clamp(hole.PhotoOffsetX, -maxX, maxX) : 0;
        double oy = double.IsFinite(hole.PhotoOffsetY) ? Math.Clamp(hole.PhotoOffsetY, -maxY, maxY) : 0;

        double cx = hole.X + hole.Width / 2.0 + ox * hole.Width;
        double cy = hole.Y + hole.Height / 2.0 + oy * hole.Height;
        var destRect = new SKRect(
            (float)(cx - drawW / 2), (float)(cy - drawH / 2),
            (float)(cx + drawW / 2), (float)(cy + drawH / 2));
        var holeRect = new SKRect(hole.X, hole.Y, hole.X + hole.Width, hole.Y + hole.Height);

        using var paint = new SKPaint { IsAntialias = true, ColorFilter = colorFilter };
        using var image = SKImage.FromBitmap(bitmap);

        canvas.Save();
        if (hole.CornerRadius > 0)
        {
            using var clipPath = new SKPath();
            clipPath.AddRoundRect(holeRect, hole.CornerRadius, hole.CornerRadius);
            canvas.ClipPath(clipPath, antialias: true);
        }
        else
        {
            canvas.ClipRect(holeRect);
        }
        canvas.DrawImage(image, destRect, new SKSamplingOptions(SKCubicResampler.Mitchell), paint);
        canvas.Restore();
    }


    private static SKColorFilter? GetColorFilter(string? filterCode)
    {
        if (string.IsNullOrWhiteSpace(filterCode)) return null;

        string code = filterCode.ToLowerInvariant().Trim();
        if (code == "original" || code == "none" || code == "natural") return null;

        if (code == "classic_bw" || code.Contains("bw") || code.Contains("gray") || code.Contains("den_trang"))
        {
            // Grayscale chuẩn ITU-R BT.709 không làm đen hay cháy sáng ảnh
            return SKColorFilter.CreateColorMatrix(new float[]
            {
                0.2126f, 0.7152f, 0.0722f, 0, 0,
                0.2126f, 0.7152f, 0.0722f, 0, 0,
                0.2126f, 0.7152f, 0.0722f, 0, 0,
                0,       0,       0,       1, 0
            });
        }

        if (code == "vintage_warm" || code.Contains("vintage") || code.Contains("warm") || code.Contains("hoai_niem"))
        {
            // Warm Film / Sepia chuẩn theo tiêu chuẩn SVG/CSS Sepia filter (không bị vàng chói lọi)
            return SKColorFilter.CreateColorMatrix(new float[]
            {
                0.78f, 0.18f, 0.04f, 0, 0.03f,
                0.05f, 0.75f, 0.20f, 0, 0.02f,
                0.04f, 0.10f, 0.66f, 0, -0.01f,
                0,     0,     0,     1, 0
            });
        }

        if (code == "korean_soft" || code.Contains("korean") || code.Contains("soft") || code.Contains("trong_treo"))
        {
            // Korean Soft Glow: Nâng sáng nhẹ dịu, tôn da hồng hào, mịn màng
            return SKColorFilter.CreateColorMatrix(new float[]
            {
                1.02f, 0.01f, 0.01f, 0, 0.02f,
                0.01f, 1.02f, 0.01f, 0, 0.02f,
                0.01f, 0.01f, 1.05f, 0, 0.03f,
                0,     0,     0,     1, 0
            });
        }

        return null;
    }
}
