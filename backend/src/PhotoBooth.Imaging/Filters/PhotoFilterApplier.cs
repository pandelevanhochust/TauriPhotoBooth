using System;
using PhotoBooth.Core.Models;
using SkiaSharp;

namespace PhotoBooth.Imaging.Filters;

/// <summary>
/// Áp bộ lọc của preset (ma trận màu CSS và/hoặc LUT) lên cả một ảnh, cùng thứ tự với bản in
/// (<c>NativeSkiaCompositor</c>: ma trận màu trước, rồi LUT) để bản ảnh lẻ gửi cho khách khớp với ảnh trong bản in.
/// </summary>
public static class PhotoFilterApplier
{
    /// <summary>Bộ lọc này có làm đổi ảnh không (ảnh gốc / preset rỗng thì không).</summary>
    public static bool HasEffect(PhotoFilterSpec? spec) => spec != null && (spec.HasColorAdjust || spec.HasLut);

    /// <summary>Áp bộ lọc lên <paramref name="bitmap"/> tại chỗ (Rgba8888 hoặc Bgra8888). <paramref name="lut"/> là LUT đã nạp của preset (nếu có).</summary>
    public static void Apply(SKBitmap bitmap, PhotoFilterSpec spec, CubeLut? lut)
    {
        float[]? matrix = CssColorMatrix.Build(spec);
        if (matrix != null)
        {
            using var source = bitmap.Copy();
            using var canvas = new SKCanvas(bitmap);
            using var paint = new SKPaint { ColorFilter = SKColorFilter.CreateColorMatrix(matrix) };
            canvas.Clear(SKColors.Transparent);
            canvas.DrawBitmap(source, 0, 0, paint);
        }
        if (lut != null) lut.Apply(bitmap, spec.LutIntensity);
    }
}
