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

    /// <summary>
    /// In mã QR lên ảnh ghép tại vị trí của frame (theo % như CMS: tâm qrX/qrY so với chiều rộng/cao, cạnh qrSize so với chiều rộng),
    /// kèm một dòng chú thích nhỏ bên dưới nếu có. Ghi ra <paramref name="outputPath"/> (JPEG 95).
    /// </summary>
    public static void StampOnImage(string imagePath, string outputPath, string text, double xPct, double yPct, double sizePct, string? caption)
    {
        using var bitmap = SKBitmap.Decode(imagePath) ?? throw new InvalidDataException($"Không đọc được ảnh: {imagePath}");
        var m = Matrix(text);
        int n = m.GetLength(0);

        float side = (float)(Math.Clamp(sizePct, 3, 60) / 100.0 * bitmap.Width);
        // Mỗi ô là số nguyên px để máy in không làm nhoè cạnh ô
        int module = Math.Max(2, (int)Math.Floor(side / n));
        float qrSide = module * n;
        float cx = (float)(Math.Clamp(xPct, 0, 100) / 100.0 * bitmap.Width);
        float cy = (float)(Math.Clamp(yPct, 0, 100) / 100.0 * bitmap.Height);
        float left = MathF.Round(Math.Clamp(cx - qrSide / 2, 0, Math.Max(0, bitmap.Width - qrSide)));
        float top = MathF.Round(Math.Clamp(cy - qrSide / 2, 0, Math.Max(0, bitmap.Height - qrSide)));

        using (var canvas = new SKCanvas(bitmap))
        {
            float captionHeight = 0;
            using var font = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold) ?? SKTypeface.Default, Math.Max(10, qrSide * 0.075f));
            if (!string.IsNullOrWhiteSpace(caption))
            {
                // Thu nhỏ chữ cho vừa bề ngang mã QR
                float width = font.MeasureText(caption);
                if (width > qrSide * 0.96f) font.Size *= qrSide * 0.96f / width;
                captionHeight = font.Size * 1.2f;
                top = MathF.Max(0, MathF.Min(top, bitmap.Height - qrSide - captionHeight));
            }

            using (var bg = new SKPaint { Color = SKColors.White, IsAntialias = true })
            {
                canvas.DrawRoundRect(SKRect.Create(left, top, qrSide, qrSide + captionHeight), module, module, bg);
            }
            DrawModules(canvas, m, left, top, module);

            if (captionHeight > 0)
            {
                using var ink = new SKPaint { Color = SKColors.Black, IsAntialias = true };
                // Dòng chữ nằm sát dưới phần QR, trong lề trắng 4 ô
                // (lấn tối đa 1,5 ô vào lề trắng; còn 2,5 ô là đủ cho máy quét)
                canvas.DrawText(caption!, left + qrSide / 2, top + qrSide - module * 1.5f + font.Size * 0.95f, SKTextAlign.Center, font, ink);
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
