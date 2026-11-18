using System;
using System.IO;
using QRCoder;
using SkiaSharp;

namespace PhotoBooth.Imaging;

/// <summary>Sinh mã QR ngay trên máy (không cần mạng): ảnh PNG cho màn hình, hoặc in đè lên ảnh ghép.</summary>
public static class QrCodeRenderer
{
    /// <summary>Ma trận QR (đã gồm 4 ô lề trắng mỗi phía). Mức sửa lỗi M: chịu được vết mờ nhẹ trên bản in.</summary>
    private static bool[,] Matrix(string text)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
        int n = data.ModuleMatrix.Count;
        var m = new bool[n, n];
        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++) m[x, y] = data.ModuleMatrix[y][x];
        }
        return m;
    }

    /// <summary>Ảnh PNG của mã QR, cạnh xấp xỉ <paramref name="size"/> px (làm tròn theo số ô).</summary>
    public static byte[] RenderPng(string text, int size = 480)
    {
        var m = Matrix(text);
        int n = m.GetLength(0);
        int module = Math.Max(1, size / n);
        int side = module * n;
        using var surface = SKSurface.Create(new SKImageInfo(side, side));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.White);
        DrawModules(canvas, m, 0, 0, module);
        using var image = surface.Snapshot();
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        return png.ToArray();
    }

    /// <summary>Ma trận của QRCoder có sẵn bấy nhiêu ô lề trắng mỗi phía.</summary>
    private const int MatrixQuietModules = 4;

    /// <summary>
    /// Lề trắng giữ lại quanh mã khi in lên ảnh ghép. Chuẩn là 4 ô; dùng 2 ô để phần mã đen to hơn trong ô QR của frame
    /// (máy quét điện thoại vẫn đọc được trên nền trắng). Tăng lên nếu thấy khó quét.
    /// </summary>
    private const int StampQuietModules = 2;

    /// <summary>
    /// In mã QR lên ảnh ghép tại ô QR của frame (theo % như CMS: tâm qrX/qrY so với chiều rộng/cao, cạnh qrSize so với chiều rộng).
    /// Hộp trắng chứa mã phủ kín đúng ô QR (không thu nhỏ theo số ô), các ô đen chia đều trong hộp và làm tròn theo px
    /// để cạnh ô sắc nét khi in. Ghi ra <paramref name="outputPath"/> (JPEG 95).
    /// </summary>
    public static void StampOnImage(string imagePath, string outputPath, string text, double xPct, double yPct, double sizePct)
    {
        using var bitmap = SKBitmap.Decode(imagePath) ?? throw new InvalidDataException($"Không đọc được ảnh: {imagePath}");
        var m = Matrix(text);
        int n = m.GetLength(0);
        int core = n - 2 * MatrixQuietModules;
        int total = core + 2 * StampQuietModules;

        float side = (float)(Math.Clamp(sizePct, 3, 60) / 100.0 * bitmap.Width);
        float cx = (float)(Math.Clamp(xPct, 0, 100) / 100.0 * bitmap.Width);
        float cy = (float)(Math.Clamp(yPct, 0, 100) / 100.0 * bitmap.Height);
        float left = MathF.Round(Math.Clamp(cx - side / 2, 0, Math.Max(0, bitmap.Width - side)));
        float top = MathF.Round(Math.Clamp(cy - side / 2, 0, Math.Max(0, bitmap.Height - side)));
        float module = side / total;

        using (var canvas = new SKCanvas(bitmap))
        {
            using var white = new SKPaint { Color = SKColors.White, IsAntialias = false };
            canvas.DrawRect(SKRect.Create(left, top, MathF.Round(left + side) - left, MathF.Round(top + side) - top), white);

            using var ink = new SKPaint { Color = SKColors.Black, IsAntialias = false, Style = SKPaintStyle.Fill };
            for (int y = 0; y < core; y++)
            {
                float y0 = MathF.Round(top + (y + StampQuietModules) * module);
                float y1 = MathF.Round(top + (y + StampQuietModules + 1) * module);
                for (int x = 0; x < core; x++)
                {
                    if (!m[x + MatrixQuietModules, y + MatrixQuietModules]) continue;
                    float x0 = MathF.Round(left + (x + StampQuietModules) * module);
                    float x1 = MathF.Round(left + (x + StampQuietModules + 1) * module);
                    canvas.DrawRect(x0, y0, x1 - x0, y1 - y0, ink);
                }
            }
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var jpg = image.Encode(SKEncodedImageFormat.Jpeg, 95);
        string tmp = outputPath + ".tmp";
        using (var fs = File.Create(tmp)) jpg.SaveTo(fs);
        File.Move(tmp, outputPath, overwrite: true);
    }

    private static void DrawModules(SKCanvas canvas, bool[,] m, float left, float top, int module)
    {
        int n = m.GetLength(0);
        using var ink = new SKPaint { Color = SKColors.Black, IsAntialias = false, Style = SKPaintStyle.Fill };
        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++)
            {
                if (m[x, y]) canvas.DrawRect(left + x * module, top + y * module, module, module, ink);
            }
        }
    }
}
