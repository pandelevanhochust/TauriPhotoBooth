using System.Text.Json;

namespace PhotoBooth.Core.Models;

/// <summary>
/// Bộ lọc màu của một preset trên CMS, đọc giống UI và trang CMS để bản in khớp bản xem trước:
/// - Preset CSS: các tham số kiểu CSS filter (brightness, contrast, saturate, sepia, grayscale, hue-rotate),
///   áp theo đúng thứ tự UI dùng (grayscale, sepia, contrast, brightness, saturate, hue-rotate).
///   Hai định dạng configJson: cũ {"grayscale":1,"contrast":1.2,"brightness":0.05,"saturation":1.1}
///   và CMS mới {"mode":"css","css":{"brightness":100,...}} (đơn vị %, hueRotate theo độ).
/// - Preset LUT: file .cube (lutUrl), trộn với ảnh gốc theo độ mạnh = texture.opacity / 100
///   (không có thì intensityDefault), như trang CMS xem trước.
/// </summary>
public sealed class PhotoFilterSpec
{
    public double Grayscale { get; set; }
    public double Sepia { get; set; }
    public double Contrast { get; set; } = 1;
    public double Brightness { get; set; } = 1;
    public double Saturate { get; set; } = 1;
    public double HueRotateDeg { get; set; }

    /// <summary>URL file .cube trên CMS (preset LUT).</summary>
    public string? LutUrl { get; set; }
    /// <summary>File .cube đã tải về máy (host điền trước khi ghép ảnh).</summary>
    public string? LutPath { get; set; }
    /// <summary>Độ mạnh LUT 0..1: 0 = ảnh gốc, 1 = áp hết.</summary>
    public double LutIntensity { get; set; } = 1;

    public bool HasColorAdjust =>
        Grayscale > 0 || Sepia > 0 || Math.Abs(Contrast - 1) > 1e-6 || Math.Abs(Brightness - 1) > 1e-6
        || Math.Abs(Saturate - 1) > 1e-6 || Math.Abs(HueRotateDeg) > 1e-6;

    public bool HasLut => !string.IsNullOrWhiteSpace(LutUrl) || !string.IsNullOrWhiteSpace(LutPath);

    public static bool IsCubeUrl(string? url) =>
        !string.IsNullOrWhiteSpace(url) && url.Split('?')[0].EndsWith(".cube", StringComparison.OrdinalIgnoreCase);

    /// <summary>Tìm preset theo mã (code) hoặc id trong mảng preset của CMS.</summary>
    public static JsonElement? FindPreset(JsonElement presets, string codeOrId)
    {
        if (presets.ValueKind != JsonValueKind.Array || string.IsNullOrWhiteSpace(codeOrId)) return null;
        foreach (var p in presets.EnumerateArray())
        {
            if (p.ValueKind != JsonValueKind.Object) continue;
            string? code = p.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
            string? id = p.TryGetProperty("id", out var i) ? i.ToString() : null;
            if (string.Equals(code, codeOrId, StringComparison.OrdinalIgnoreCase) || string.Equals(id, codeOrId, StringComparison.Ordinal))
            {
                return p;
            }
        }
        return null;
    }

    /// <summary>Đọc một preset của CMS. Trả về null nếu preset không làm đổi ảnh (ảnh gốc).</summary>
    public static PhotoFilterSpec? FromPreset(JsonElement preset)
    {
        var spec = new PhotoFilterSpec();
        string? lutUrl = preset.TryGetProperty("lutUrl", out var lu) && lu.ValueKind == JsonValueKind.String ? lu.GetString() : null;
        double intensityDefault = preset.TryGetProperty("intensityDefault", out var idf) && idf.ValueKind == JsonValueKind.Number ? idf.GetDouble() : 1;

        JsonElement config = default;
        bool hasConfig = false;
        if (preset.TryGetProperty("configJson", out var cj))
        {
            try
            {
                if (cj.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(cj.GetString()))
                {
                    config = JsonDocument.Parse(cj.GetString()!).RootElement.Clone();
                    hasConfig = config.ValueKind == JsonValueKind.Object;
                }
                else if (cj.ValueKind == JsonValueKind.Object)
                {
                    config = cj.Clone();
                    hasConfig = true;
                }
            }
            catch (JsonException)
            {
                hasConfig = false;
            }
        }

        string? mode = hasConfig && config.TryGetProperty("mode", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : null;

        if (IsCubeUrl(lutUrl))
        {
            spec.LutUrl = lutUrl;
            double? opacity = hasConfig && config.TryGetProperty("texture", out var tx) && tx.ValueKind == JsonValueKind.Object
                && tx.TryGetProperty("opacity", out var op) && op.ValueKind == JsonValueKind.Number ? op.GetDouble() : null;
            spec.LutIntensity = Math.Clamp(opacity.HasValue ? opacity.Value / 100.0 : intensityDefault, 0, 1);
            return spec.LutIntensity > 0 ? spec : null;
        }

        if (!hasConfig) return null;

        if (mode == "css")
        {
            if (!config.TryGetProperty("css", out var css) || css.ValueKind != JsonValueKind.Object) return null;
            double Pct(string key, double def) => css.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() / 100.0 : def;
            spec.Brightness = Pct("brightness", 1);
            spec.Contrast = Pct("contrast", 1);
            spec.Saturate = Pct("saturate", 1);
            spec.Sepia = Math.Clamp(Pct("sepia", 0), 0, 1);
            spec.Grayscale = Math.Clamp(Pct("grayscale", 0), 0, 1);
            spec.HueRotateDeg = css.TryGetProperty("hueRotate", out var hr) && hr.ValueKind == JsonValueKind.Number ? hr.GetDouble() : 0;
        }
        else if (mode == null)
        {
            // Định dạng cũ, cùng cách đọc với UI (backendApi.convertFilterConfigToCss)
            double? Num(string key) => config.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
            if (Num("grayscale") is double g) spec.Grayscale = Math.Clamp(g, 0, 1);
            if (Num("sepia") is double s) spec.Sepia = Math.Clamp(s, 0, 1);
            if (Num("contrast") is double c) spec.Contrast = c;
            if (Num("brightness") is double b) spec.Brightness = b > -1 && b < 1 ? 1 + b : b;
            if (Num("saturation") is double sat) spec.Saturate = sat;
            if (Num("hueRotate") is double h) spec.HueRotateDeg = h;
        }
        else
        {
            // "texture" không có file .cube (ảnh texture phủ lên): chưa hỗ trợ
            return null;
        }

        return spec.HasColorAdjust ? spec : null;
    }
}
