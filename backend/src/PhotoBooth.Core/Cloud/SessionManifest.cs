using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PhotoBooth.Core.Cloud;

/// <summary>Trạng thái gửi của một phiên trong hàng đợi.</summary>
public static class ManifestState
{
    /// <summary>Còn file chưa gửi xong.</summary>
    public const string Pending = "pending";
    /// <summary>Server báo đủ file (status Ready). Giữ manifest tới khi hết hạn để xoá bản trên máy.</summary>
    public const string Ready = "ready";
    /// <summary>Lỗi không thử lại được (vd. shareToken trùng). Cần người vận hành xử lý tay.</summary>
    public const string Failed = "failed";
    /// <summary>Server báo phiên đã hết hạn (410) nên không gửi được nữa. File trên máy vẫn giữ tới khi quá 7 ngày.</summary>
    public const string Expired = "expired";
}

public static class ManifestFileStatus
{
    public const string Pending = "pending";
    public const string Uploaded = "uploaded";
    /// <summary>Server từ chối vĩnh viễn (400/413/415) hoặc file nguồn đã mất. Không gửi lại.</summary>
    public const string Failed = "failed";
}

/// <summary>Cách chuẩn bị file trước khi gửi.</summary>
public static class FilePrep
{
    /// <summary>Gửi nguyên file.</summary>
    public const string None = "none";
    /// <summary>Ảnh lẻ: thu nhỏ cạnh dài ~2560px, JPEG 90, bỏ EXIF (GPS).</summary>
    public const string Original = "original";
    /// <summary>Ảnh nhỏ ~400px.</summary>
    public const string Thumbnail = "thumbnail";
}

/// <summary>Một file của phiên sẽ gửi lên backend.</summary>
public sealed class ManifestFile
{
    /// <summary>collage | print | original | filtered | timelapse | gif | thumbnail</summary>
    public string Type { get; set; } = "";
    public int Index { get; set; }
    /// <summary>File gốc trên máy.</summary>
    public string SourcePath { get; set; } = "";
    public string Prep { get; set; } = FilePrep.None;
    /// <summary>File đã chuẩn bị để gửi (trùng SourcePath khi Prep = none).</summary>
    public string? PreparedPath { get; set; }
    public string? ContentType { get; set; }
    public long Size { get; set; }
    public string Status { get; set; } = ManifestFileStatus.Pending;
    public int Attempts { get; set; }
    public string? LastError { get; set; }

    [JsonIgnore] public string Key => $"{Type}#{Index}";
}

/// <summary>
/// Manifest của một phiên chụp trong hàng đợi gửi (outbox). Lưu trên đĩa, đọc lại khi app khởi động,
/// nên hàng đợi chịu được tắt máy / Watchdog khởi động lại app bất kỳ lúc nào.
/// </summary>
public sealed class SessionManifest
{
    public int Version { get; set; } = 1;
    /// <summary>UUID gửi cho backend.</summary>
    public Guid SessionId { get; set; }
    /// <summary>Mã trong link QR (22 ký tự base62).</summary>
    public string ShareToken { get; set; } = "";
    public string ShareUrl { get; set; } = "";
    /// <summary>Mã phiên trên máy (tên thư mục SESSION_...).</summary>
    public string LocalSessionId { get; set; } = "";
    public DateTimeOffset CapturedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public int RetentionHours { get; set; } = 24;

    public string? OrderId { get; set; }
    public int? FrameId { get; set; }
    public string? LayoutId { get; set; }
    public int PrintQuantity { get; set; } = 1;

    /// <summary>Thư mục của phiên trên máy: xoá khi phiên đã gửi xong và hết hạn, hoặc khi quá 7 ngày dù chưa gửi được.</summary>
    public List<string> LocalDirectories { get; set; } = new();

    public string State { get; set; } = ManifestState.Pending;
    /// <summary>Backend đã nhận khai báo đủ danh sách file hiện tại (init thành công).</summary>
    public bool Declared { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset? NextAttemptAt { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset? LastErrorAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    public List<ManifestFile> Files { get; set; } = new();

    [JsonIgnore] public DateTimeOffset ExpiresAt => CapturedAt.AddHours(RetentionHours);

    /// <summary>Thứ tự gửi: ảnh ghép trước để khách quét QR thấy ngay, video sau cùng.</summary>
    public static int TypeRank(string type) => type switch
    {
        "collage" => 0,
        "print" => 1,
        "original" => 2,
        // Bản đã áp filter của ảnh lẻ (cùng Index với bản gốc): gửi sau toàn bộ ảnh gốc
        "filtered" => 3,
        "thumbnail" => 4,
        "timelapse" => 5,
        "gif" => 6,
        _ => 7,
    };

    public IEnumerable<ManifestFile> FilesInUploadOrder() =>
        Files.OrderBy(f => TypeRank(f.Type)).ThenBy(f => f.Index);
}

/// <summary>Sinh mã phiên và shareToken.</summary>
public static class ShareIds
{
    private const string Base62 = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
    public const int TokenLength = 22;

    /// <summary>22 ký tự [A-Za-z0-9] từ bộ sinh số ngẫu nhiên an toàn (~131 bit).</summary>
    public static string NewShareToken() => RandomNumberGenerator.GetString(Base62, TokenLength);

    public static string BuildShareUrl(string shareBaseUrl, string token) => shareBaseUrl.TrimEnd('/') + "/" + token;
}

/// <summary>Lưu manifest trong thư mục outbox, mỗi phiên một file {sessionId}.json, ghi kiểu nguyên tử.</summary>
public sealed class ManifestStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly Action<string> _log;

    public string Directory { get; }

    public ManifestStore(string directory, Action<string>? log = null)
    {
        Directory = directory;
        _log = log ?? (_ => { });
        System.IO.Directory.CreateDirectory(directory);
    }

    private string PathFor(Guid sessionId) => Path.Combine(Directory, sessionId.ToString("N") + ".json");

    public void Save(SessionManifest manifest) =>
        AtomicFile.WriteAllText(PathFor(manifest.SessionId), JsonSerializer.Serialize(manifest, JsonOptions));

    public void Delete(Guid sessionId)
    {
        try { File.Delete(PathFor(sessionId)); } catch (IOException) { }
    }

    public List<SessionManifest> LoadAll()
    {
        var result = new List<SessionManifest>();
        foreach (var tmp in System.IO.Directory.EnumerateFiles(Directory, "*.json.tmp"))
        {
            // Ghi dở lúc mất điện: file chính vẫn là bản cũ còn nguyên
            try { File.Delete(tmp); } catch (IOException) { }
        }
        foreach (var file in System.IO.Directory.EnumerateFiles(Directory, "*.json"))
        {
            try
            {
                var manifest = JsonSerializer.Deserialize<SessionManifest>(File.ReadAllText(file), JsonOptions);
                if (manifest != null && manifest.SessionId != Guid.Empty) result.Add(manifest);
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                _log($"[WARN] Upload queue: manifest hỏng {Path.GetFileName(file)}: {ex.Message}");
                try { File.Move(file, file + ".bad", overwrite: true); } catch (IOException) { }
            }
        }
        return result;
    }
}
