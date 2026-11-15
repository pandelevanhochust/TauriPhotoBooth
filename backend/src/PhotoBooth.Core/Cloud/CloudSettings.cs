using System.Text.Json;
using System.Text.Json.Serialization;

namespace PhotoBooth.Core.Cloud;

/// <summary>
/// Cấu hình gửi phiên chụp lên backend Favori (file <c>cloud.json</c> trong thư mục dữ liệu).
/// Khoá API chỉ nằm trong file này trên máy kiosk, không bao giờ ghi ra log.
/// </summary>
public sealed class CloudSettings
{
    /// <summary>Bật/tắt việc gửi ảnh. Tắt thì vẫn in QR nhưng không gửi gì.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Gốc API backend, ví dụ "https://api.favori.vn" (không có "/" cuối).</summary>
    public string BaseUrl { get; set; } = "http://129.221.221.113:8080";

    /// <summary>Header X-Api-Key (Kiosk:ApiKey của backend).</summary>
    public string ApiKey { get; set; } = "";

    /// <summary>Header X-Device-Id. Trống thì dùng tên máy tính.</summary>
    public string DeviceId { get; set; } = "";

    /// <summary>Gốc link QR lấy từ GET /api/kiosk/sessions/config, lưu lại để dựng link khi offline.</summary>
    public string CachedShareBaseUrl { get; set; } = "";

    /// <summary>Gốc link QR dự phòng khi chưa từng lấy được cấu hình từ server. Trống thì dùng BaseUrl + "/s".</summary>
    public string DefaultShareBaseUrl { get; set; } = "";

    public int RetentionHours { get; set; } = 24;
    public long MaxImageBytes { get; set; } = 25L * 1024 * 1024;
    public long MaxVideoBytes { get; set; } = 100L * 1024 * 1024;

    /// <summary>Giới hạn tốc độ gửi (KB/s) để không chiếm hết đường mạng. 0 = không giới hạn.</summary>
    public int MaxUploadKBps { get; set; } = 2048;

    [JsonIgnore] public bool IsConfigured => Enabled && !string.IsNullOrWhiteSpace(BaseUrl) && !string.IsNullOrWhiteSpace(ApiKey);

    [JsonIgnore] public string EffectiveDeviceId => string.IsNullOrWhiteSpace(DeviceId) ? Environment.MachineName : DeviceId.Trim();

    /// <summary>Gốc link QR: ưu tiên giá trị server trả về, rồi giá trị cấu hình, cuối cùng là BaseUrl + "/s".</summary>
    [JsonIgnore]
    public string ShareBaseUrl
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(CachedShareBaseUrl)) return CachedShareBaseUrl.TrimEnd('/');
            if (!string.IsNullOrWhiteSpace(DefaultShareBaseUrl)) return DefaultShareBaseUrl.TrimEnd('/');
            return BaseUrl.TrimEnd('/') + "/s";
        }
    }

    public CloudSettings Clone() => (CloudSettings)MemberwiseClone();

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static CloudSettings Load(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                return JsonSerializer.Deserialize<CloudSettings>(File.ReadAllText(path), JsonOptions) ?? new CloudSettings();
            }
        }
        catch
        {
            // File hỏng: dùng mặc định, lần lưu sau sẽ ghi lại
        }
        return new CloudSettings();
    }

    public void Save(string path) => AtomicFile.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
}

/// <summary>Ghi file kiểu nguyên tử: ghi file tạm, flush xuống đĩa rồi đổi tên, để mất điện giữa chừng không làm hỏng file cũ.</summary>
public static class AtomicFile
{
    public static void WriteAllText(string path, string content)
    {
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        string tmp = path + ".tmp";
        using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var writer = new StreamWriter(fs, new System.Text.UTF8Encoding(false)))
        {
            writer.Write(content);
            writer.Flush();
            fs.Flush(flushToDisk: true);
        }
        File.Move(tmp, path, overwrite: true);
    }
}
