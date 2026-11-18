using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using PhotoBooth.Core.Cloud;
using PhotoBooth.Core.Storage;
using PhotoBooth.Imaging;

namespace PhotoBooth.Host;

/// <summary>
/// Gửi phiên chụp lên backend Favori và dựng link QR tải ảnh (xem docs/adr/0007-session-upload-outbox.md).
/// QR dựng ngay trên máy, không gọi mạng; việc gửi chạy nền qua <see cref="SessionUploadQueue"/>.
/// </summary>
public partial class MainWindow
{

    private readonly object _cloudLock = new();
    private CloudSettings _cloudSettings = new();
    private string _cloudSettingsPath = "";
    private SessionUploadQueue? _uploadQueue;

    // Mã của phiên đang chụp gửi cho backend (khác mã thư mục SESSION_...)
    private Guid _cloudSessionId;
    private string _shareToken = "";
    private DateTimeOffset _capturedAt;

    // Khách đang ở bước chụp: hàng đợi tạm dừng để không làm giật live view
    private volatile bool _captureStepActive;
    private long _captureStepSince;

    private CloudSettings CloudSettingsSnapshot()
    {
        lock (_cloudLock) return _cloudSettings.Clone();
    }

    private void SaveCloudSettings(CloudSettings settings)
    {
        lock (_cloudLock)
        {
            _cloudSettings = settings.Clone();
            try { _cloudSettings.Save(_cloudSettingsPath); }
            catch (Exception ex) { App.Log($"[WARN] Không lưu được cloud.json: {ex.Message}"); }
        }
    }

    private void InitializeCloudUpload(string baseDataDir)
    {
        _cloudSettingsPath = Path.Combine(baseDataDir, "cloud.json");
        bool existed = File.Exists(_cloudSettingsPath);
        _cloudSettings = CloudSettings.Load(_cloudSettingsPath);
        if (!existed) SaveCloudSettings(_cloudSettings);

        var client = new KioskSessionsClient(CloudSettingsSnapshot);
        _uploadQueue = new SessionUploadQueue(
            new ManifestStore(Path.Combine(baseDataDir, "Outbox"), App.Log),
            client,
            CloudSettingsSnapshot,
            SaveCloudSettings,
            PrepareUploadFile,
            IsCaptureBusy,
            App.Log);
        _uploadQueue.Start();

        var s = CloudSettingsSnapshot();
        App.Log($"[INFO] Cloud upload: {(s.IsConfigured ? "bật" : s.Enabled ? "chưa cấu hình khoá API" : "tắt")}, server {s.BaseUrl}, thiết bị {s.EffectiveDeviceId}, link QR {s.ShareBaseUrl}");
    }

    private System.Threading.Timer? _storageCleanupTimer;

    /// <summary>
    /// Dọn file cũ quá 7 ngày (phiên chụp, gallery, ảnh kiểm tra máy ảnh, khung video bị bỏ lại, log cũ): sau khi mở app 2 phút,
    /// rồi mỗi 6 giờ. Phiên còn trong hàng đợi gửi do hàng đợi tự xoá. Không chạy khi khách đang chụp.
    /// </summary>
    private void StartStorageCleaner(string baseDataDir)
    {
        var cleaner = new StorageCleaner(
            Path.Combine(baseDataDir, "Sessions"),
            new[] { GetLocalGalleryRootDir(), Path.Combine(baseDataDir, "CameraTest") },
            baseDataDir,
            "host_startup",
            log: App.Log);

        void RunOnce()
        {
            if (IsCaptureBusy()) return;
            var thread = Thread.CurrentThread;
            var old = thread.Priority;
            try
            {
                thread.Priority = ThreadPriority.BelowNormal;
                cleaner.Run(dir => _uploadQueue?.ReferencesDirectory(dir) == true);
            }
            catch (Exception ex)
            {
                App.Log($"[WARN] Dọn bộ nhớ thất bại: {ex.Message}");
            }
            finally
            {
                thread.Priority = old;
            }
        }

        _storageCleanupTimer = new System.Threading.Timer(_ => RunOnce(), null, TimeSpan.FromMinutes(2), TimeSpan.FromHours(6));
    }

    private bool IsCaptureBusy()
    {
        // Phòng khi UI không kịp báo rời bước chụp (vd. tải lại trang): tự bỏ cờ sau 20 phút
        if (_captureStepActive && Environment.TickCount64 - Interlocked.Read(ref _captureStepSince) > 20 * 60 * 1000)
        {
            _captureStepActive = false;
        }
        return _captureStepActive || IsRecordingSessionVideo;
    }

    /// <summary>Thu nhỏ ảnh lẻ / tạo ảnh nhỏ trước khi gửi. Chạy ở mức ưu tiên thấp để không tranh CPU với live view.</summary>
    private static void PrepareUploadFile(ManifestFile file, string target)
    {
        var thread = Thread.CurrentThread;
        var old = thread.Priority;
        try
        {
            thread.Priority = ThreadPriority.BelowNormal;
            if (file.Prep == FilePrep.Thumbnail) UploadMediaPreparer.PrepareThumbnail(file.SourcePath, target);
            else UploadMediaPreparer.PrepareOriginal(file.SourcePath, target);
        }
        finally
        {
            thread.Priority = old;
        }
    }

    /// <summary>Phiên mới: sinh sessionId (UUID) và shareToken để in QR ngay cả khi mất mạng.</summary>
    private void BeginCloudSession()
    {
        _cloudSessionId = Guid.NewGuid();
        _shareToken = ShareIds.NewShareToken();
        _capturedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Có gửi ảnh lên server cho phiên này không (đã bật và có khoá API). Không thì không in QR.</summary>
    private bool CloudActive => _uploadQueue != null && CloudSettingsSnapshot().IsConfigured && !string.IsNullOrEmpty(_shareToken);

    private string CurrentShareUrl() => ShareIds.BuildShareUrl(CloudSettingsSnapshot().ShareBaseUrl, _shareToken);

    private static double? ReadDouble(JsonElement payload, string name) =>
        payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.Number
            ? el.GetDouble()
            : null;

    private static string? ReadString(JsonElement payload, string name) =>
        payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;

    /// <summary>
    /// In QR lên ảnh ghép nếu frame có vị trí QR (qrX/qrY/qrSize từ CMS). Trả về đường dẫn ảnh dùng để in
    /// (ảnh ghép gốc composite_final.jpg không bị sửa).
    /// </summary>
    private string StampQrForPrint(string compositePath, string shareUrl, JsonElement payload)
    {
        double? qx = ReadDouble(payload, "qrX"), qy = ReadDouble(payload, "qrY"), qs = ReadDouble(payload, "qrSize");
        if (qx == null || qy == null || qs == null || qs <= 0) return compositePath;
        try
        {
            string printPath = Path.Combine(Path.GetDirectoryName(compositePath)!, "composite_print.jpg");
            QrCodeRenderer.StampOnImage(compositePath, printPath, shareUrl, qx.Value, qy.Value, qs.Value);
            return printPath;
        }
        catch (Exception ex)
        {
            App.Log($"[WARN] In QR lên ảnh thất bại: {ex.Message}");
            return compositePath;
        }
    }

    private static string QrDataUrl(string shareUrl)
    {
        try
        {
            return "data:image/png;base64," + Convert.ToBase64String(QrCodeRenderer.RenderPng(shareUrl, 480));
        }
        catch (Exception ex)
        {
            App.Log($"[WARN] Tạo QR thất bại: {ex.Message}");
            return "";
        }
    }

    private static readonly Regex ShotNumber = new(@"shot_(\d+)\.jpg$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Đưa phiên vào hàng đợi gửi: ảnh ghép, toàn bộ ảnh đã chụp, ảnh nhỏ (video gắn thêm khi mã hoá xong).</summary>
    private void EnqueueCloudSession(string collagePath, string shareUrl, JsonElement payload)
    {
        if (_uploadQueue == null) return;
        int? frameId = int.TryParse(ReadString(payload, "frameId"), out var fid) ? fid : (int?)ReadDouble(payload, "frameId");
        int copies = (int)(ReadDouble(payload, "copies") ?? 1);

        var files = new List<ManifestFile>
        {
            new() { Type = "collage", Index = 0, SourcePath = collagePath, ContentType = "image/jpeg" },
        };
        var shots = _capturedRawPhotos
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => ShotNumber.Match(p) is { Success: true } m ? int.Parse(m.Groups[1].Value) : int.MaxValue)
            .ToList();
        for (int i = 0; i < shots.Count; i++)
        {
            files.Add(new ManifestFile { Type = "original", Index = i, SourcePath = shots[i], Prep = FilePrep.Original });
        }
        files.Add(new ManifestFile { Type = "thumbnail", Index = 0, SourcePath = collagePath, Prep = FilePrep.Thumbnail });

        var manifest = new SessionManifest
        {
            SessionId = _cloudSessionId,
            ShareToken = _shareToken,
            ShareUrl = shareUrl,
            LocalSessionId = _currentSessionId,
            CapturedAt = _capturedAt,
            RetentionHours = CloudSettingsSnapshot().RetentionHours,
            FrameId = frameId,
            LayoutId = ReadString(payload, "layoutId"),
            PrintQuantity = Math.Clamp(copies, 1, 20),
            LocalDirectories = new List<string> { _currentSessionDir, _localGalleryDir }.Where(d => !string.IsNullOrEmpty(d)).ToList(),
            Files = files,
        };
        try
        {
            _uploadQueue.Enqueue(manifest);
        }
        catch (Exception ex)
        {
            App.Log($"[ERROR] Không đưa được phiên {_currentSessionId} vào hàng đợi gửi: {ex.Message}");
        }
    }

    /// <summary>Video phiên mã hoá xong: gắn vào phiên đã/sắp vào hàng đợi.</summary>
    /// <param name="index">0 = video đầy đủ của phiên, 1 = timelapse tua nhanh.</param>
    private void AttachTimelapseToCloud(Guid cloudSessionId, string videoPath, int index)
    {
        if (_uploadQueue == null || cloudSessionId == Guid.Empty || !File.Exists(videoPath)) return;
        _uploadQueue.AttachFile(cloudSessionId, new ManifestFile
        {
            Type = "timelapse",
            Index = index,
            SourcePath = videoPath,
            ContentType = "video/mp4",
        });
    }

    private const string SessionMediaHost = "media.hihibooth.local";

    /// <summary>Thư mục phiên (SESSION_...) đang mã hoá video; null khi không có video nào đang mã hoá.</summary>
    private string? _videoEncodingSession;

    /// <summary>
    /// Tiến độ của phiên vừa chụp cho màn hình cuối: video đã mã hoá xong chưa (kèm link xem trước)
    /// và hàng đợi đã gửi xong phiên này chưa.
    /// </summary>
    private object BuildSessionProgressDto()
    {
        string dirName = Path.GetFileName(_currentSessionDir);
        bool videoEncoding = !string.IsNullOrEmpty(dirName)
            && (Volatile.Read(ref _videoEncodingSession) == dirName || IsRecordingSessionVideo);
        // Xem trước ưu tiên timelapse tua nhanh (ngắn, lặp lại đẹp hơn), không có thì dùng video đầy đủ
        string? previewFile = videoEncoding || string.IsNullOrEmpty(_currentSessionDir) ? null
            : File.Exists(Path.Combine(_currentSessionDir, FastTimelapseFile)) ? FastTimelapseFile
            : File.Exists(Path.Combine(_currentSessionDir, SessionVideoFile)) ? SessionVideoFile
            : null;
        string videoUrl = previewFile != null ? $"https://{SessionMediaHost}/{Uri.EscapeDataString(dirName)}/{previewFile}" : "";

        var progress = CloudActive ? _uploadQueue?.GetSessionProgress(_cloudSessionId) : null;
        return new
        {
            cloudActive = CloudActive,
            state = progress?.State ?? "none",
            uploadedFiles = progress?.UploadedFiles ?? 0,
            skippedFiles = progress?.SkippedFiles ?? 0,
            totalFiles = progress?.TotalFiles ?? 0,
            videoEncoding,
            videoUrl,
        };
    }

    private object BuildCloudStatusDto()
    {
        var st = _uploadQueue?.GetStatus();
        var s = CloudSettingsSnapshot();
        return new
        {
            enabled = s.Enabled,
            configured = s.IsConfigured,
            baseUrl = s.BaseUrl,
            deviceId = s.DeviceId,
            effectiveDeviceId = s.EffectiveDeviceId,
            hasApiKey = !string.IsNullOrWhiteSpace(s.ApiKey),
            shareBaseUrl = s.ShareBaseUrl,
            maxUploadKBps = s.MaxUploadKBps,
            retentionHours = s.RetentionHours,
            pendingSessions = st?.PendingSessions ?? 0,
            pendingFiles = st?.PendingFiles ?? 0,
            readySessions = st?.ReadySessions ?? 0,
            failedSessions = st?.FailedSessions ?? 0,
            oldestPendingMinutes = st?.OldestPendingMinutes,
            warning = st?.Warning ?? false,
            warningMessage = st?.WarningMessage,
            lastError = st?.LastError,
            lastErrorAt = st?.LastErrorAt,
            lastSuccessAt = st?.LastSuccessAt,
            activity = st?.Activity ?? "Chưa khởi động",
            pausedForCapture = st?.PausedForCapture ?? false,
        };
    }

    /// <summary>IPC cloud_configure: đổi cấu hình từ màn hình admin. apiKey bỏ trống = giữ khoá cũ.</summary>
    private object ConfigureCloud(JsonElement payload)
    {
        var s = CloudSettingsSnapshot();
        if (payload.ValueKind == JsonValueKind.Object)
        {
            if (payload.TryGetProperty("enabled", out var en) && en.ValueKind is JsonValueKind.True or JsonValueKind.False) s.Enabled = en.GetBoolean();
            string? baseUrl = ReadString(payload, "baseUrl")?.Trim().TrimEnd('/');
            if (!string.IsNullOrEmpty(baseUrl) && baseUrl != s.BaseUrl)
            {
                s.BaseUrl = baseUrl;
                s.CachedShareBaseUrl = ""; // server khác: lấy lại gốc link QR
            }
            string? apiKey = ReadString(payload, "apiKey")?.Trim();
            if (!string.IsNullOrEmpty(apiKey)) s.ApiKey = apiKey;
            string? deviceId = ReadString(payload, "deviceId");
            if (deviceId != null) s.DeviceId = deviceId.Trim();
            if (ReadDouble(payload, "maxUploadKBps") is double kbps) s.MaxUploadKBps = Math.Clamp((int)kbps, 0, 100_000);
        }
        SaveCloudSettings(s);
        App.Log($"[INFO] Cloud upload: cập nhật cấu hình (server {s.BaseUrl}, thiết bị {s.EffectiveDeviceId}, bật={s.Enabled})");
        _uploadQueue?.RetryNow();
        return BuildCloudStatusDto();
    }
}
