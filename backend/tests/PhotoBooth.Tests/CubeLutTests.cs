using System;
using PhotoBooth.Imaging.Filters;
using Xunit;

namespace PhotoBooth.Tests;

public sealed class CubeLutTests
{
    private const string Lut2 = """
        TITLE "t"
        LUT_3D_SIZE 2
        DOMAIN_MIN 0 0 0
        DOMAIN_MAX 1 1 1
        0 0 0
        1 0 0
        0 1 0
        1 1 0
        0 0 1
        1 0 1
        0 1 1
        1.2 -0.1 0.5
        """;

    [Fact]
    public void ToUInt16Bytes_KeepsOrderAndClamps()
    {
        var lut = CubeLut.Parse(Lut2);
        byte[] bytes = lut.ToUInt16Bytes();
        Assert.Equal(2 * 2 * 2 * 3 * 2, bytes.Length);

        ushort At(int i) => (ushort)(bytes[i * 2] | (bytes[i * 2 + 1] << 8));
        // Dòng thứ 2 (đỏ = 1): little-endian, cùng thứ tự với file
        Assert.Equal(65535, At(3));
        Assert.Equal(0, At(4));
        // Dòng cuối: 1.2 cắt về 1, -0.1 cắt về 0, 0.5 làm tròn
        Assert.Equal(65535, At(21));
        Assert.Equal(0, At(22));
        Assert.Equal((ushort)Math.Round(0.5 * 65535), At(23));
    }
}
