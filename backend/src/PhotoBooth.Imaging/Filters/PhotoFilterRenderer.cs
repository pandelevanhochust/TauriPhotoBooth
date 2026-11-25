using System;
using System.Globalization;
using System.IO;
using PhotoBooth.Core.Models;
using SkiaSharp;

namespace PhotoBooth.Imaging.Filters;

/// <summary>LUT 3D đọc từ file .cube (Adobe / Resolve), cùng cách đọc và áp với trang CMS (filterConfig.ts).</summary>
public sealed class CubeLut
{
    public int Size { get; }
    /// <summary>Size^3 * 3 giá trị RGB, chỉ số đỏ thay đổi nhanh nhất.</summary>
    public float[] Data { get; }
    public float[] DomainMin { get; }
    public float[] DomainMax { get; }

    private CubeLut(int size, float[] data, float[] min, float[] max)
    {
        Size = size;
        Data = data;
        DomainMin = min;
        DomainMax = max;
    }

    public static CubeLut Load(string path) => Parse(File.ReadAllText(path));

    /// <summary>
    /// Dữ liệu LUT dạng gọn gửi sang UI: mỗi giá trị (cắt về 0..1) thành số 16 bit không dấu, little-endian, cùng thứ tự
    /// với <see cref="Data"/>. Nhỏ hơn file .cube dạng chữ khoảng 4–5 lần và UI không phải đọc chữ.
    /// </summary>
    public byte[] ToUInt16Bytes()
    {
        var bytes = new byte[Data.Length * 2];
        for (int i = 0; i < Data.Length; i++)
        {
            ushort v = (ushort)Math.Round(Math.Clamp(Data[i], 0f, 1f) * 65535f);
            bytes[i * 2] = (byte)v;
            bytes[i * 2 + 1] = (byte)(v >> 8);
        }
        return bytes;
    }

    public static CubeLut Parse(string text)
    {
        int size = 0, written = 0;
        float[]? data = null;
        float[] min = { 0, 0, 0 }, max = { 1, 1, 1 };

        foreach (var rawLine in text.Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            char first = line[0];
            bool numeric = first == '-' || first == '.' || first == '+' || char.IsDigit(first);
            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (!numeric)
            {
                string key = parts[0].ToUpperInvariant();
                if (key == "LUT_1D_SIZE") throw new InvalidDataException("Chỉ hỗ trợ LUT 3D");
                if (key == "LUT_3D_SIZE")
                {
                    size = int.Parse(parts[1], CultureInfo.InvariantCulture);
                    if (size < 2 || size > 129) throw new InvalidDataException($"LUT_3D_SIZE không hợp lệ: {size}");
                    data = new float[size * size * size * 3];
                }
                else if ((key == "DOMAIN_MIN" || key == "DOMAIN_MAX") && parts.Length >= 4)
                {
                    var triple = new[]
                    {
                        float.Parse(parts[1], CultureInfo.InvariantCulture),
                        float.Parse(parts[2], CultureInfo.InvariantCulture),
                        float.Parse(parts[3], CultureInfo.InvariantCulture),
                    };
                    if (key == "DOMAIN_MIN") min = triple; else max = triple;
                }
                continue;
            }
            if (data == null) throw new InvalidDataException("Thiếu LUT_3D_SIZE trước dữ liệu màu");
            if (parts.Length < 3 || written + 3 > data.Length) throw new InvalidDataException("Dữ liệu LUT không khớp LUT_3D_SIZE");
            for (int c = 0; c < 3; c++) data[written++] = float.Parse(parts[c], CultureInfo.InvariantCulture);
        }

        if (data == null || written != data.Length) throw new InvalidDataException("File .cube thiếu dữ liệu");
        for (int c = 0; c < 3; c++)
        {
            if (max[c] <= min[c]) throw new InvalidDataException("DOMAIN_MAX phải lớn hơn DOMAIN_MIN");
        }
        return new CubeLut(size, data, min, max);
    }

    /// <summary>Áp LUT lên ảnh tại chỗ (nội suy trilinear), trộn với ảnh gốc theo <paramref name="intensity"/> 0..1.</summary>
    public void Apply(SKBitmap bitmap, double intensity)
    {
        float mix = (float)Math.Clamp(intensity, 0, 1);
        if (mix <= 0) return;
        if (bitmap.ColorType != SKColorType.Rgba8888 && bitmap.ColorType != SKColorType.Bgra8888)
        {
            throw new NotSupportedException($"Định dạng ảnh không hỗ trợ: {bitmap.ColorType}");
        }
        bool bgra = bitmap.ColorType == SKColorType.Bgra8888;
        int ri = bgra ? 2 : 0, bi = bgra ? 0 : 2;

        int n1 = Size - 1;
        float sr = n1 / (DomainMax[0] - DomainMin[0]);
        float sg = n1 / (DomainMax[1] - DomainMin[1]);
        float sb = n1 / (DomainMax[2] - DomainMin[2]);
        int stride = Size * 3, slice = Size * Size * 3;
        var d = Data;

        Span<byte> px = bitmap.GetPixelSpan();
        for (int i = 0; i + 3 < px.Length; i += 4)
        {
            float r = Math.Clamp((px[i + ri] / 255f - DomainMin[0]) * sr, 0, n1);
            float g = Math.Clamp((px[i + 1] / 255f - DomainMin[1]) * sg, 0, n1);
            float b = Math.Clamp((px[i + bi] / 255f - DomainMin[2]) * sb, 0, n1);
            int r0 = (int)r, g0 = (int)g, b0 = (int)b;
            int r1 = Math.Min(r0 + 1, n1), g1 = Math.Min(g0 + 1, n1), b1 = Math.Min(b0 + 1, n1);
            float fr = r - r0, fg = g - g0, fb = b - b0;

            int i000 = r0 * 3 + g0 * stride + b0 * slice, i100 = r1 * 3 + g0 * stride + b0 * slice;
            int i010 = r0 * 3 + g1 * stride + b0 * slice, i110 = r1 * 3 + g1 * stride + b0 * slice;
            int i001 = r0 * 3 + g0 * stride + b1 * slice, i101 = r1 * 3 + g0 * stride + b1 * slice;
            int i011 = r0 * 3 + g1 * stride + b1 * slice, i111 = r1 * 3 + g1 * stride + b1 * slice;

            for (int c = 0; c < 3; c++)
            {
                float c00 = d[i000 + c] + (d[i100 + c] - d[i000 + c]) * fr;
                float c10 = d[i010 + c] + (d[i110 + c] - d[i010 + c]) * fr;
                float c01 = d[i001 + c] + (d[i101 + c] - d[i001 + c]) * fr;
                float c11 = d[i011 + c] + (d[i111 + c] - d[i011 + c]) * fr;
                float c0 = c00 + (c10 - c00) * fg;
                float c1 = c01 + (c11 - c01) * fg;
                float mapped = Math.Clamp(c0 + (c1 - c0) * fb, 0, 1) * 255f;
                int idx = i + (c == 0 ? ri : c == 1 ? 1 : bi);
                float v = px[idx] + (mapped - px[idx]) * mix;
                px[idx] = (byte)Math.Clamp((int)(v + 0.5f), 0, 255);
            }
        }
    }
}

/// <summary>Đổi tham số kiểu CSS filter sang ma trận màu Skia, theo công thức của chuẩn W3C Filter Effects.</summary>
public static class CssColorMatrix
{
    /// <summary>Ma trận 4x5 (Skia, phần cộng theo thang 0..1) áp theo thứ tự UI: grayscale, sepia, contrast, brightness, saturate, hue-rotate.</summary>
    public static float[]? Build(PhotoFilterSpec spec)
    {
        if (!spec.HasColorAdjust) return null;
        double[,] m = Identity();
        if (spec.Grayscale > 0) m = Mul(Grayscale(spec.Grayscale), m);
        if (spec.Sepia > 0) m = Mul(Sepia(spec.Sepia), m);
        if (Math.Abs(spec.Contrast - 1) > 1e-6) m = Mul(Linear(spec.Contrast, 0.5 - 0.5 * spec.Contrast), m);
        if (Math.Abs(spec.Brightness - 1) > 1e-6) m = Mul(Linear(spec.Brightness, 0), m);
        if (Math.Abs(spec.Saturate - 1) > 1e-6) m = Mul(Saturate(spec.Saturate), m);
        if (Math.Abs(spec.HueRotateDeg) > 1e-6) m = Mul(HueRotate(spec.HueRotateDeg), m);

        var result = new float[20];
        for (int r = 0; r < 4; r++)
        {
            for (int c = 0; c < 5; c++) result[r * 5 + c] = (float)m[r, c];
        }
        return result;
    }

    // Ma trận 5x5 (hàng cuối 0 0 0 0 1) để nhân nối tiếp được
    private static double[,] Identity()
    {
        var m = new double[5, 5];
        for (int i = 0; i < 5; i++) m[i, i] = 1;
        return m;
    }

    private static double[,] Rgb(double[] v)
    {
        var m = Identity();
        for (int r = 0; r < 3; r++)
        {
            for (int c = 0; c < 3; c++) m[r, c] = v[r * 3 + c];
        }
        return m;
    }

    private static double[,] Mul(double[,] a, double[,] b)
    {
        var m = new double[5, 5];
        for (int r = 0; r < 5; r++)
        {
            for (int c = 0; c < 5; c++)
            {
                double s = 0;
                for (int k = 0; k < 5; k++) s += a[r, k] * b[k, c];
                m[r, c] = s;
            }
        }
        return m;
    }

    private static double[,] Linear(double slope, double intercept)
    {
        var m = Identity();
        for (int i = 0; i < 3; i++)
        {
            m[i, i] = slope;
            m[i, 4] = intercept;
        }
        return m;
    }

    private static double[,] Grayscale(double amount)
    {
        double a = 1 - Math.Clamp(amount, 0, 1);
        return Rgb(new[]
        {
            0.2126 + 0.7874 * a, 0.7152 - 0.7152 * a, 0.0722 - 0.0722 * a,
            0.2126 - 0.2126 * a, 0.7152 + 0.2848 * a, 0.0722 - 0.0722 * a,
            0.2126 - 0.2126 * a, 0.7152 - 0.7152 * a, 0.0722 + 0.9278 * a,
        });
    }

    private static double[,] Sepia(double amount)
    {
        double a = 1 - Math.Clamp(amount, 0, 1);
        return Rgb(new[]
        {
            0.393 + 0.607 * a, 0.769 - 0.769 * a, 0.189 - 0.189 * a,
            0.349 - 0.349 * a, 0.686 + 0.314 * a, 0.168 - 0.168 * a,
            0.272 - 0.272 * a, 0.534 - 0.534 * a, 0.131 + 0.869 * a,
        });
    }

    private static double[,] Saturate(double s) => Rgb(new[]
    {
        0.213 + 0.787 * s, 0.715 - 0.715 * s, 0.072 - 0.072 * s,
        0.213 - 0.213 * s, 0.715 + 0.285 * s, 0.072 - 0.072 * s,
        0.213 - 0.213 * s, 0.715 - 0.715 * s, 0.072 + 0.928 * s,
    });

    private static double[,] HueRotate(double degrees)
    {
        double rad = degrees * Math.PI / 180, cos = Math.Cos(rad), sin = Math.Sin(rad);
        return Rgb(new[]
        {
            0.213 + cos * 0.787 - sin * 0.213, 0.715 - cos * 0.715 - sin * 0.715, 0.072 - cos * 0.072 + sin * 0.928,
            0.213 - cos * 0.213 + sin * 0.143, 0.715 + cos * 0.285 + sin * 0.140, 0.072 - cos * 0.072 - sin * 0.283,
            0.213 - cos * 0.213 - sin * 0.787, 0.715 - cos * 0.715 + sin * 0.715, 0.072 + cos * 0.928 + sin * 0.072,
        });
    }
}
