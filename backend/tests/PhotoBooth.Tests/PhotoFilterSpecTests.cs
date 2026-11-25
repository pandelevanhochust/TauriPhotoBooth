using System.Text.Json;
using PhotoBooth.Core.Models;
using Xunit;

namespace PhotoBooth.Tests;

public sealed class PhotoFilterSpecTests
{
    private static JsonElement Preset(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void LutPreset_UsesTextureOpacityAsIntensity()
    {
        var spec = PhotoFilterSpec.FromPreset(Preset(
            """{"code":"aqua2","lutUrl":"https://x/filters/a.cube","intensityDefault":0.6,"configJson":"{\"mode\": \"texture\", \"texture\": {\"opacity\": 60, \"blendMode\": \"overlay\"}}"}"""));
        Assert.NotNull(spec);
        Assert.Equal("https://x/filters/a.cube", spec!.LutUrl);
        Assert.Equal(0.6, spec.LutIntensity, 3);
        Assert.False(spec.HasColorAdjust);
    }

    [Fact]
    public void LutPreset_WithoutOpacity_FallsBackToIntensityDefault()
    {
        var spec = PhotoFilterSpec.FromPreset(Preset("""{"lutUrl":"https://x/b.CUBE?v=2","intensityDefault":0.8}"""));
        Assert.Equal(0.8, spec!.LutIntensity, 3);
    }

    [Fact]
    public void LegacyCssPreset_ReadsLikeUi()
    {
        var spec = PhotoFilterSpec.FromPreset(Preset(
            """{"code":"korean_soft","configJson":"{\"contrast\": 0.95, \"brightness\": 0.05, \"saturation\": 1.1}"}"""));
        Assert.NotNull(spec);
        Assert.Equal(0.95, spec!.Contrast, 3);
        Assert.Equal(1.05, spec.Brightness, 3); // độ lệch nhỏ cộng vào 1, như UI
        Assert.Equal(1.1, spec.Saturate, 3);
        Assert.Null(spec.LutUrl);
    }

    [Fact]
    public void CmsCssPreset_ReadsPercentUnits()
    {
        var spec = PhotoFilterSpec.FromPreset(Preset(
            """{"configJson":"{\"mode\":\"css\",\"css\":{\"brightness\":110,\"contrast\":100,\"saturate\":80,\"sepia\":20,\"grayscale\":0,\"blur\":0,\"hueRotate\":15}}"}"""));
        Assert.Equal(1.1, spec!.Brightness, 3);
        Assert.Equal(0.8, spec.Saturate, 3);
        Assert.Equal(0.2, spec.Sepia, 3);
        Assert.Equal(15, spec.HueRotateDeg, 3);
    }

    [Fact]
    public void OriginalPreset_ReturnsNull()
    {
        Assert.Null(PhotoFilterSpec.FromPreset(Preset("""{"code":"original","configJson":null}""")));
    }

    [Fact]
    public void FindPreset_MatchesCodeOrId()
    {
        var list = Preset("""[{"id":5,"code":"aqua2"},{"id":7,"code":"hk"}]""");
        Assert.NotNull(PhotoFilterSpec.FindPreset(list, "hk"));
        Assert.NotNull(PhotoFilterSpec.FindPreset(list, "5"));
        Assert.Null(PhotoFilterSpec.FindPreset(list, "nope"));
    }
}
