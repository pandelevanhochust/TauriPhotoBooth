using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ImageMagick;
using PhotoBooth.Core.Interfaces;
using PhotoBooth.Core.Models;

namespace PhotoBooth.Imaging.Compositor;

public class MagickCompositor : IImageComposer
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
            uint width = template.CanvasWidth > 0 ? (uint)template.CanvasWidth : (uint)DefaultWidth;
            uint height = template.CanvasHeight > 0 ? (uint)template.CanvasHeight : (uint)DefaultHeight;

            MagickColor bgColor = new MagickColor(MagickColors.White);

            using var baseImage = new MagickImage(bgColor, width, height);
            baseImage.Density = new Density(300, 300); // 300 DPI

            for (int i = 0; i < template.Holes.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (i >= rawPhotoPaths.Count) break;
                string photoPath = rawPhotoPaths[i];
                if (!File.Exists(photoPath)) continue;

                var hole = template.Holes[i];
                using var photo = new MagickImage(photoPath);

                // Auto-orient based on EXIF
                photo.AutoOrient();

                // Center crop resize
                photo.Resize(new MagickGeometry((uint)hole.Width, (uint)hole.Height)
                {
                    FillArea = true
                });
                photo.Crop((uint)hole.Width, (uint)hole.Height, Gravity.Center);
                photo.ResetPage();

                // Composite into hole
                baseImage.Composite(photo, hole.X, hole.Y, CompositeOperator.Over);
            }

            // Overlay template frame if available
            if (!string.IsNullOrEmpty(template.OverlayImagePath) && File.Exists(template.OverlayImagePath))
            {
                using var frameOverlay = new MagickImage(template.OverlayImagePath);
                frameOverlay.Resize(width, height);
                baseImage.Composite(frameOverlay, 0, 0, CompositeOperator.Over);
            }

            // Duplicate 2x6 strip if needed
            if (template.SupportedPaperType == PaperType.DoubleStrip2x6 && width == 1200)
            {
                using var leftHalf = baseImage.Clone();
                leftHalf.Crop(600, height, Gravity.West);
                baseImage.Composite(leftHalf, 600, 0, CompositeOperator.Over);
            }

            string dir = Path.GetDirectoryName(outputPath)!;
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            // Save JPEG 95%
            baseImage.Quality = 95;
            baseImage.Write(outputPath, MagickFormat.Jpeg);

            return outputPath;
        }, cancellationToken);
    }

    /// <summary>
    /// Creates an animated GIF recap slideshow from captured photos
    /// </summary>
    public Task<string> CreateAnimatedRecapAsync(
        IReadOnlyList<string> photoPaths,
        string outputGifPath,
        int delayMs = 60,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            using var collection = new MagickImageCollection();

            foreach (var path in photoPaths)
            {
                if (!File.Exists(path)) continue;
                var frame = new MagickImage(path);
                frame.AutoOrient();
                frame.Resize(600, 800);
                frame.AnimationDelay = (uint)(delayMs / 10);
                collection.Add(frame);
            }

            string dir = Path.GetDirectoryName(outputGifPath)!;
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            collection.Optimize();
            collection.Write(outputGifPath, MagickFormat.Gif);

            return outputGifPath;
        }, cancellationToken);
    }
}
