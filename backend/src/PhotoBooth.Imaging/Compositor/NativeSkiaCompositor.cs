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

            // 2. Render each hole with Center-Crop and ColorFilter
            for (int i = 0; i < template.Holes.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (i >= rawPhotoPaths.Count) break;
                string photoPath = rawPhotoPaths[i];
                if (!File.Exists(photoPath)) continue;

                var hole = template.Holes[i];
                using var rawBitmap = SKBitmap.Decode(photoPath);
                if (rawBitmap == null) continue;

                // Render with Center-Crop Cover and preset color filter into hole coordinates
                RenderCenterCrop(canvas, rawBitmap, hole.X, hole.Y, hole.Width, hole.Height, hole.CornerRadius, colorFilter);
            }

            // 3. Render Frame Overlay if template image exists
            if (!string.IsNullOrEmpty(template.OverlayImagePath) && File.Exists(template.OverlayImagePath))
            {
                using var overlayBitmap = SKBitmap.Decode(template.OverlayImagePath);
                if (overlayBitmap != null)
                {
                    var destRect = new SKRect(0, 0, targetWidth, targetHeight);
                    using var paint = new SKPaint();
                    canvas.DrawBitmap(overlayBitmap, destRect, paint);
                }
            }

            // 4. If Strip2x6 layout and duplicate enabled, mirror/clone left strip to right strip
            if (template.SupportedPaperType == PaperType.DoubleStrip2x6 && targetWidth == 1200)
            {
                // Left strip occupies X: 0 to 600, right strip X: 600 to 1200
                using var currentSnap = surface.Snapshot();
                using var leftCrop = SKBitmap.FromImage(currentSnap);
                if (leftCrop != null)
                {
                    var srcRect = new SKRect(0, 0, 600, targetHeight);
                    var destRect = new SKRect(600, 0, 1200, targetHeight);
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

    private static void RenderCenterCrop(SKCanvas canvas, SKBitmap bitmap, int x, int y, int width, int height, int cornerRadius, SKColorFilter? colorFilter = null)
    {
        float targetRatio = (float)width / height;
        float srcRatio = (float)bitmap.Width / bitmap.Height;

        SKRect srcRect;
        if (srcRatio > targetRatio)
        {
            // Bitmap is wider than target slot: crop left & right
            float cropWidth = bitmap.Height * targetRatio;
            float cropX = (bitmap.Width - cropWidth) / 2f;
            srcRect = new SKRect(cropX, 0, cropX + cropWidth, bitmap.Height);
        }
        else
        {
            // Bitmap is taller than target slot: crop top & bottom
            float cropHeight = bitmap.Width / targetRatio;
            float cropY = (bitmap.Height - cropHeight) / 2f;
            srcRect = new SKRect(0, cropY, bitmap.Width, cropY + cropHeight);
        }

        var destRect = new SKRect(x, y, x + width, y + height);
        using var paint = new SKPaint
        {
            IsAntialias = true,
            ColorFilter = colorFilter
        };

        if (cornerRadius > 0)
        {
            canvas.Save();
            using var roundRectPath = new SKPath();
            roundRectPath.AddRoundRect(destRect, cornerRadius, cornerRadius);
            canvas.ClipPath(roundRectPath, antialias: true);
            canvas.DrawBitmap(bitmap, srcRect, destRect, paint);
            canvas.Restore();
        }
        else
        {
            canvas.DrawBitmap(bitmap, srcRect, destRect, paint);
        }
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
