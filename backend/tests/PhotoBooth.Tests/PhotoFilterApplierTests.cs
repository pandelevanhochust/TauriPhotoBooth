using PhotoBooth.Core.Models;
using PhotoBooth.Imaging;
using PhotoBooth.Imaging.Filters;
using SkiaSharp;
using Xunit;

namespace PhotoBooth.Tests;

/// <summary>Bản ảnh lẻ đã áp filter gửi cho khách: cùng phép tính màu với bản in.</summary>
public sealed class PhotoFilterApplierTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "pb-filter-tests-" + Guid.NewGuid().ToString("N"));

    public PhotoFilterApplierTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private static SKBitmap Solid(byte r, byte g, byte b)
    {
        var bmp = new SKBitmap(new SKImageInfo(8, 8, SKColorType.Rgba8888, SKAlphaType.Premul));
        bmp.Erase(new SKColor(r, g, b));
        return bmp;
    }

    // LUT 2x2x2 đảo màu: (r,g,b) -> (1-r,1-g,1-b); chỉ số đỏ đổi nhanh nhất
    private const string InvertCube = """
        LUT_3D_SIZE 2
        1 1 1
        0 1 1
        1 0 1
        0 0 1
        1 1 0
        0 1 0
        1 0 0
        0 0 0
        """;

    [Fact]
    public void HasEffect_FalseForOriginalAndEmptySpec()
    {
        Assert.False(PhotoFilterApplier.HasEffect(null));
        Assert.False(PhotoFilterApplier.HasEffect(new PhotoFilterSpec()));
        Assert.True(PhotoFilterApplier.HasEffect(new PhotoFilterSpec { Grayscale = 1 }));
        Assert.True(PhotoFilterApplier.HasEffect(new PhotoFilterSpec { LutUrl = "https://x/a.cube" }));
    }

    [Fact]
    public void Grayscale_MakesChannelsEqual()
    {
        using var bmp = Solid(200, 100, 50);
        PhotoFilterApplier.Apply(bmp, new PhotoFilterSpec { Grayscale = 1 }, null);
        var c = bmp.GetPixel(3, 3);
        Assert.InRange(Math.Abs(c.Red - c.Green), 0, 2);
        Assert.InRange(Math.Abs(c.Green - c.Blue), 0, 2);
        Assert.NotEqual(200, c.Red);
    }

    [Fact]
    public void Lut_InvertsColors_AtFullIntensity_AndBlendsAtHalf()
    {
        var lut = CubeLut.Parse(InvertCube);

        using (var full = Solid(200, 100, 50))
        {
            PhotoFilterApplier.Apply(full, new PhotoFilterSpec { LutUrl = "https://x/a.cube", LutIntensity = 1 }, lut);
            var c = full.GetPixel(2, 2);
            Assert.InRange(c.Red, 54, 56);
            Assert.InRange(c.Green, 154, 156);
            Assert.InRange(c.Blue, 204, 206);
        }

        using var half = Solid(200, 100, 50);
        PhotoFilterApplier.Apply(half, new PhotoFilterSpec { LutUrl = "https://x/a.cube", LutIntensity = 0.5 }, lut);
        var h = half.GetPixel(2, 2);
        Assert.InRange(h.Red, 126, 129); // giữa 200 và 55
    }

    [Fact]
    public void WriteResizedJpeg_AppliesProcessAndResizes()
    {
        string source = Path.Combine(_dir, "src.jpg");
        using (var big = new SKBitmap(new SKImageInfo(3000, 2000, SKColorType.Rgba8888, SKAlphaType.Premul)))
        {
            big.Erase(new SKColor(200, 100, 50));
            using var image = SKImage.FromBitmap(big);
            using var data = image.Encode(SKEncodedImageFormat.Jpeg, 95);
            File.WriteAllBytes(source, data.ToArray());
        }

        string target = Path.Combine(_dir, "out", "filtered.jpg");
        UploadMediaPreparer.WriteResizedJpeg(source, target, 1000, 90, b => PhotoFilterApplier.Apply(b, new PhotoFilterSpec { Grayscale = 1 }, null));

        using var result = SKBitmap.Decode(target);
        Assert.Equal(1000, result.Width);
        Assert.Equal(667, result.Height);
        var c = result.GetPixel(500, 300);
        Assert.InRange(Math.Abs(c.Red - c.Green), 0, 4); // JPEG làm lệch nhẹ
        Assert.InRange(Math.Abs(c.Green - c.Blue), 0, 4);

        // Không truyền process: ảnh giữ màu gốc
        string plain = Path.Combine(_dir, "out", "plain.jpg");
        UploadMediaPreparer.WriteResizedJpeg(source, plain, 1000, 90);
        using var p = SKBitmap.Decode(plain);
        Assert.True(p.GetPixel(500, 300).Red > p.GetPixel(500, 300).Blue + 80);
    }
}
