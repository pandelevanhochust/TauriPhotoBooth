namespace PhotoBooth.Core.Cloud;

/// <summary>Tình trạng hàng đợi cho màn hình chẩn đoán của người vận hành.</summary>
public sealed record UploadQueueStatus(
    bool Enabled,
    bool Configured,
    string BaseUrl,
    string DeviceId,
    bool HasApiKey,
    string ShareBaseUrl,
    int PendingSessions,
    int PendingFiles,
    int ReadySessions,
    int FailedSessions,
    double? OldestPendingMinutes,
    bool Warning,
    string? WarningMessage,
    string? LastError,
    DateTimeOffset? LastErrorAt,
    DateTimeOffset? LastSuccessAt,
    string Activity,
    bool PausedForCapture);

/// <summary>Tiến độ gửi của một phiên (màn hình cuối chờ gửi xong mới hiện QR).</summary>
public sealed record SessionUploadProgress(string State, int UploadedFiles, int SkippedFiles, int TotalFiles);

/// <summary>
/// Hàng đợi gửi phiên chụp lên backend (outbox). Chạy nền, gửi tuần tự từng file của từng phiên,
/// thử lại có backoff tới khi phiên hết hạn, rồi xoá phiên khỏi máy.
/// Không chặn luồng chụp: tạm dừng khi khách đang ở bước chụp (<c>isBusy</c>).
/// </summary>
public sealed class SessionUploadQueue : IDisposable
{
    private static readonly TimeSpan BackoffStart = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan BackoffMax = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan AuthPause = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan ConfigRefresh = TimeSpan.FromHours(1);
    private static readonly TimeSpan WarnAfter = TimeSpan.FromHours(1);
    private static readonly TimeSpan IdleWait = TimeSpan.FromSeconds(15);

    private readonly ManifestStore _store;
    private readonly KioskSessionsClient _client;
    private readonly Func<CloudSettings> _getSettings;
    private readonly Action<CloudSettings> _saveSettings;
    private readonly Action<ManifestFile, string> _prepare;
    private readonly Func<bool> _isBusy;
    private readonly Action<string> _log;
    private readonly Func<DateTimeOffset> _now;

    private readonly object _gate = new();
    private readonly List<SessionManifest> _manifests = new();
    /// <summary>File đến trước khi phiên vào hàng đợi (vd. video mã hoá xong trước bước in).</summary>
    private readonly Dictionary<Guid, List<ManifestFile>> _lateFiles = new();
    private readonly SemaphoreSlim _signal = new(0, int.MaxValue);
    private readonly CancellationTokenSource _cts = new();
    private Task? _loop;

    private DateTimeOffset _authPausedUntil = DateTimeOffset.MinValue;
    private DateTimeOffset _nextConfigFetch = DateTimeOffset.MinValue;
    private string? _lastError;
    private DateTimeOffset? _lastErrorAt;
    private DateTimeOffset? _lastSuccessAt;
    private DateTimeOffset _lastWarnLog = DateTimeOffset.MinValue;
    private string _activity = "Đang khởi động";
    private bool _pausedForCapture;

    public SessionUploadQueue(
        ManifestStore store,
        KioskSessionsClient client,
        Func<CloudSettings> getSettings,
        Action<CloudSettings> saveSettings,
        Action<ManifestFile, string> prepare,
        Func<bool>? isBusy = null,
        Action<string>? log = null,
        Func<DateTimeOffset>? now = null)
    {
        _store = store;
        _client = client;
        _getSettings = getSettings;
        _saveSettings = saveSettings;
        _prepare = prepare;
        _isBusy = isBusy ?? (() => false);
        _log = log ?? (_ => { });
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>Đọc lại các manifest trên đĩa và bắt đầu vòng gửi nền.</summary>
    public void Start()
    {
        LoadFromDisk();
        _loop ??= Task.Run(() => RunAsync(_cts.Token));
    }

    /// <summary>Đọc lại các manifest trên đĩa (không chạy vòng gửi; test gọi TickAsync trực tiếp).</summary>
    internal void LoadFromDisk()
    {
        lock (_gate)
        {
            _manifests.Clear();
            _manifests.AddRange(_store.LoadAll());
        }
        int pending = _manifests.Count(m => m.State == ManifestState.Pending);
        _log($"[INFO] Upload queue: {_manifests.Count} phiên trong outbox ({pending} chưa gửi xong)");
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _loop?.Wait(TimeSpan.FromSeconds(3)); } catch (AggregateException) { }
        _cts.Dispose();
    }

    /// <summary>Gốc link QR hiện tại (lấy từ server, đã lưu lại; dùng được khi offline).</summary>
    public string ShareBaseUrl => _getSettings().ShareBaseUrl;

    /// <summary>Thêm phiên vào hàng đợi (ghi manifest xuống đĩa trước rồi mới gửi).</summary>
    public void Enqueue(SessionManifest manifest)
    {
        lock (_gate)
        {
            if (_lateFiles.Remove(manifest.SessionId, out var late))
            {
                foreach (var f in late) Upsert(manifest, f);
            }
            manifest.CreatedAt = manifest.CreatedAt == default ? _now() : manifest.CreatedAt;
            manifest.State = ManifestState.Pending;
            manifest.Declared = false;
            _manifests.RemoveAll(m => m.SessionId == manifest.SessionId);
            _manifests.Add(manifest);
            _store.Save(manifest);
        }
        _log($"[INFO] Upload queue: thêm phiên {manifest.LocalSessionId} ({manifest.Files.Count} file) -> {manifest.ShareUrl}");
        Signal();
    }

    /// <summary>Thêm / thay một file cho phiên (vd. video vừa mã hoá xong). Phiên chưa vào hàng đợi thì giữ lại chờ.</summary>
    public void AttachFile(Guid sessionId, ManifestFile file)
    {
        lock (_gate)
        {
            var manifest = _manifests.FirstOrDefault(m => m.SessionId == sessionId);
            if (manifest == null)
            {
                if (!_lateFiles.TryGetValue(sessionId, out var list)) _lateFiles[sessionId] = list = new List<ManifestFile>();
                list.RemoveAll(f => f.Key == file.Key);
                list.Add(file);
                return;
            }
            Upsert(manifest, file);
            if (manifest.State == ManifestState.Ready) manifest.State = ManifestState.Pending;
            manifest.NextAttemptAt = null;
            _store.Save(manifest);
        }
        Signal();
    }

    /// <summary>Thử lại ngay mọi phiên đang chờ (nút trong màn hình admin, hoặc sau khi sửa cấu hình).</summary>
    public void RetryNow()
    {
        lock (_gate)
        {
            _authPausedUntil = DateTimeOffset.MinValue;
            _nextConfigFetch = DateTimeOffset.MinValue;
            foreach (var m in _manifests.Where(m => m.State == ManifestState.Pending)) m.NextAttemptAt = null;
        }
        Signal();
    }

    public void Signal() => _signal.Release();

    public UploadQueueStatus GetStatus()
    {
        var s = _getSettings();
        var now = _now();
        lock (_gate)
        {
            var pending = _manifests.Where(m => m.State == ManifestState.Pending).ToList();
            var failed = _manifests.Where(m => m.State == ManifestState.Failed).ToList();
            var waiting = pending.Concat(failed).ToList();
            double? oldest = waiting.Count == 0 ? null : (now - waiting.Min(m => m.CapturedAt)).TotalMinutes;
            string? warning = null;
            if (failed.Count > 0)
            {
                warning = $"{failed.Count} phiên bị server từ chối, cần xử lý tay (xem log).";
            }
            else if (oldest is double minutes && minutes >= WarnAfter.TotalMinutes)
            {
                warning = $"Có phiên chờ gửi đã {FormatMinutes(minutes)}. Kiểm tra mạng / cấu hình trước khi phiên hết hạn {s.RetentionHours} giờ.";
            }
            return new UploadQueueStatus(
                s.Enabled,
                s.IsConfigured,
                s.BaseUrl,
                s.EffectiveDeviceId,
                !string.IsNullOrWhiteSpace(s.ApiKey),
                s.ShareBaseUrl,
                pending.Count,
                pending.Sum(m => m.Files.Count(f => f.Status == ManifestFileStatus.Pending)),
                _manifests.Count(m => m.State == ManifestState.Ready),
                failed.Count,
                oldest,
                warning != null,
                warning,
                _lastError,
                _lastErrorAt,
                _lastSuccessAt,
                _activity,
                _pausedForCapture);
        }
    }

    /// <summary>Tiến độ của một phiên; null khi phiên chưa vào hàng đợi (hoặc đã hết hạn).</summary>
    public SessionUploadProgress? GetSessionProgress(Guid sessionId)
    {
        lock (_gate)
        {
            var m = _manifests.FirstOrDefault(x => x.SessionId == sessionId);
            if (m == null) return null;
            return new SessionUploadProgress(
                m.State,
                m.Files.Count(f => f.Status == ManifestFileStatus.Uploaded),
                m.Files.Count(f => f.Status == ManifestFileStatus.Failed),
                m.Files.Count);
        }
    }

    private static string FormatMinutes(double minutes) =>
        minutes >= 60 ? $"{(int)(minutes / 60)} giờ {(int)(minutes % 60)} phút" : $"{(int)minutes} phút";

    private static void Upsert(SessionManifest manifest, ManifestFile file)
    {
        var existing = manifest.Files.FirstOrDefault(f => f.Key == file.Key);
        if (existing != null && existing.Status == ManifestFileStatus.Uploaded && existing.SourcePath == file.SourcePath) return;
        if (existing != null) manifest.Files.Remove(existing);
        manifest.Files.Add(file);
        manifest.Declared = false;
    }

    // ------------------------------------------------------------------ vòng gửi

    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TimeSpan wait = IdleWait;
            try
            {
                wait = await TickAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                SetError("Lỗi hàng đợi: " + ex.Message);
                _log($"[ERROR] Upload queue: {ex}");
                wait = BackoffStart;
            }

            try
            {
                await _signal.WaitAsync(wait, ct);
                // Gộp các tín hiệu dồn lại thành một lượt
                while (_signal.CurrentCount > 0) await _signal.WaitAsync(0, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>Một lượt: dọn phiên hết hạn, lấy cấu hình, gửi phiên đến hạn sớm nhất. Trả về thời gian chờ tới lượt sau.</summary>
    internal async Task<TimeSpan> TickAsync(CancellationToken ct)
    {
        var now = _now();
        PurgeExpired(now);
        LogWarningIfStale(now);

        var settings = _getSettings();
        if (!settings.Enabled)
        {
            _activity = "Đã tắt gửi ảnh lên server";
            return TimeSpan.FromMinutes(1);
        }
        if (!settings.IsConfigured)
        {
            _activity = "Chưa cấu hình địa chỉ server / khoá API";
            return TimeSpan.FromMinutes(1);
        }
        if (now < _authPausedUntil)
        {
            _activity = "Tạm dừng: server từ chối khoá API";
            return _authPausedUntil - now;
        }
        if (_isBusy())
        {
            _pausedForCapture = true;
            _activity = "Tạm dừng trong lúc khách đang chụp";
            return TimeSpan.FromSeconds(5);
        }
        _pausedForCapture = false;

        if (now >= _nextConfigFetch) await RefreshConfigAsync(ct);

        SessionManifest? next;
        lock (_gate)
        {
            next = _manifests
                .Where(m => m.State == ManifestState.Pending && (m.NextAttemptAt == null || m.NextAttemptAt <= now))
                .OrderBy(m => m.CapturedAt)
                .FirstOrDefault();
        }
        if (next == null)
        {
            DateTimeOffset? soonest;
            lock (_gate)
            {
                soonest = _manifests.Where(m => m.State == ManifestState.Pending && m.NextAttemptAt != null)
                    .Select(m => m.NextAttemptAt).Min();
            }
            _activity = soonest == null ? "Không có gì cần gửi" : $"Chờ thử lại lúc {soonest.Value.ToLocalTime():HH:mm:ss}";
            if (soonest == null) return IdleWait;
            var delay = soonest.Value - now;
            return delay < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : delay < IdleWait ? delay : IdleWait;
        }

        await ProcessAsync(next, ct);
        return TimeSpan.Zero;
    }

    private async Task RefreshConfigAsync(CancellationToken ct)
    {
        var res = await _client.GetConfigAsync(ct);
        if (res.Status == 401)
        {
            PauseForAuth(res.Message);
            return;
        }
        if (!res.IsSuccess || res.Value == null)
        {
            // Dùng giá trị đã lưu; thử lại sau ít phút
            _nextConfigFetch = _now() + TimeSpan.FromMinutes(5);
            return;
        }
        _nextConfigFetch = _now() + ConfigRefresh;
        var s = _getSettings().Clone();
        bool changed = s.CachedShareBaseUrl != res.Value.ShareBaseUrl
            || s.RetentionHours != res.Value.RetentionHours
            || s.MaxImageBytes != res.Value.MaxImageBytes
            || s.MaxVideoBytes != res.Value.MaxVideoBytes;
        if (!changed) return;
        if (!string.IsNullOrWhiteSpace(res.Value.ShareBaseUrl)) s.CachedShareBaseUrl = res.Value.ShareBaseUrl.TrimEnd('/');
        if (res.Value.RetentionHours > 0) s.RetentionHours = res.Value.RetentionHours;
        if (res.Value.MaxImageBytes > 0) s.MaxImageBytes = res.Value.MaxImageBytes;
        if (res.Value.MaxVideoBytes > 0) s.MaxVideoBytes = res.Value.MaxVideoBytes;
        _saveSettings(s);
        _log($"[INFO] Upload queue: cấu hình server shareBaseUrl={s.CachedShareBaseUrl}, retention={s.RetentionHours}h");
    }

    private async Task ProcessAsync(SessionManifest m, CancellationToken ct)
    {
        _activity = $"Đang gửi phiên {m.LocalSessionId}";

        // 1. Chuẩn bị file (thu nhỏ ảnh lẻ, ảnh nhỏ) trước khi khai báo, để khai báo được dung lượng thật
        if (!PrepareFiles(m)) return;

        // 2. Khai báo phiên (idempotent): lần đầu, sau khi thêm file, hoặc khi server mất phiên
        if (!m.Declared && !await DeclareAsync(m, ct)) return;

        // 3. Gửi tuần tự từng file còn thiếu
        List<ManifestFile> ordered;
        lock (_gate) ordered = m.FilesInUploadOrder().ToList();
        foreach (var file in ordered)
        {
            if (file.Status != ManifestFileStatus.Pending) continue;
            if (ct.IsCancellationRequested) return;
            if (_isBusy())
            {
                // Khách vừa bắt đầu chụp: dừng, lượt sau gửi tiếp (không tính là lỗi)
                _pausedForCapture = true;
                return;
            }
            if (!await UploadOneAsync(m, file, ct)) return;
        }

        // 4. Chốt phiên
        var done = await _client.CompleteAsync(m.SessionId, ct);
        if (!done.IsSuccess || done.Value == null)
        {
            HandleSessionError(m, done.Status, done.Message, "complete");
            return;
        }

        var missing = done.Value.Missing ?? new List<SessionFileRefDto>();
        bool resend = false;
        lock (_gate)
        {
            foreach (var miss in missing)
            {
                var f = m.Files.FirstOrDefault(x => x.Type == miss.Type && x.Index == miss.Index);
                if (f != null && f.Status == ManifestFileStatus.Uploaded)
                {
                    f.Status = ManifestFileStatus.Pending;
                    resend = true;
                }
            }
            if (resend)
            {
                Save(m);
            }
            else
            {
                m.State = ManifestState.Ready;
                m.CompletedAt = _now();
                m.Attempts = 0;
                m.NextAttemptAt = null;
                m.LastError = null;
                Save(m);
            }
        }
        if (!resend)
        {
            _lastSuccessAt = _now();
            int failed = m.Files.Count(f => f.Status == ManifestFileStatus.Failed);
            _log($"[INFO] Upload queue: phiên {m.LocalSessionId} đã gửi xong ({done.Value.Status}" +
                 (failed > 0 ? $", {failed} file bị bỏ qua" : "") + ")");
        }
    }

    /// <summary>Chuẩn bị các file cần thu nhỏ. File nguồn mất trước khi khai báo thì bỏ khỏi danh sách.</summary>
    private bool PrepareFiles(SessionManifest m)
    {
        bool changed = false;
        List<ManifestFile> files;
        lock (_gate) files = m.Files.ToList();
        foreach (var file in files)
        {
            if (file.Status != ManifestFileStatus.Pending) continue;
            if (file.Prep == FilePrep.None)
            {
                file.PreparedPath = file.SourcePath;
            }
            else if (string.IsNullOrEmpty(file.PreparedPath) || !File.Exists(file.PreparedPath))
            {
                if (!File.Exists(file.SourcePath))
                {
                    MarkMissingSource(m, file);
                    changed = true;
                    continue;
                }
                string dir = Path.Combine(Path.GetDirectoryName(file.SourcePath) ?? ".", "upload");
                Directory.CreateDirectory(dir);
                string target = Path.Combine(dir, $"{file.Type}-{file.Index:00}.jpg");
                try
                {
                    _prepare(file, target);
                    file.PreparedPath = target;
                    file.ContentType = "image/jpeg";
                }
                catch (Exception ex)
                {
                    file.LastError = "Không chuẩn bị được file: " + ex.Message;
                    _log($"[WARN] Upload queue: {m.LocalSessionId} {file.Key}: {file.LastError}");
                    MarkMissingSource(m, file);
                    changed = true;
                    continue;
                }
                changed = true;
            }

            string path = file.PreparedPath!;
            if (!File.Exists(path))
            {
                MarkMissingSource(m, file);
                changed = true;
                continue;
            }
            long size = new FileInfo(path).Length;
            if (size != file.Size)
            {
                file.Size = size;
                if (m.Declared && file.Status == ManifestFileStatus.Pending) m.Declared = false;
                changed = true;
            }
            file.ContentType ??= GuessContentType(path);
        }
        if (changed)
        {
            lock (_gate) Save(m);
        }
        return true;
    }

    private void MarkMissingSource(SessionManifest m, ManifestFile file)
    {
        lock (_gate) MarkMissingSourceLocked(m, file);
    }

    private void MarkMissingSourceLocked(SessionManifest m, ManifestFile file)
    {
        if (!m.Declared)
        {
            // Chưa khai báo với server: bỏ hẳn file khỏi phiên, phiên vẫn Ready được
            m.Files.Remove(file);
            _log($"[WARN] Upload queue: {m.LocalSessionId} bỏ {file.Key} vì không còn file {file.SourcePath}");
        }
        else
        {
            file.Status = ManifestFileStatus.Failed;
            file.LastError ??= "Không còn file trên máy";
        }
    }

    private async Task<bool> DeclareAsync(SessionManifest m, CancellationToken ct)
    {
        var res = await _client.InitAsync(m, ct);
        if (!res.IsSuccess || res.Value == null)
        {
            HandleSessionError(m, res.Status, res.Message, "init");
            return false;
        }
        lock (_gate)
        {
            m.Declared = true;
            if (!string.IsNullOrWhiteSpace(res.Value.ShareUrl)) m.ShareUrl = res.Value.ShareUrl;
            // Khởi động lại giữa chừng: server cho biết file nào đã có, chỉ gửi phần còn thiếu
            var missing = (res.Value.Missing ?? new List<SessionFileRefDto>())
                .Select(x => $"{x.Type}#{x.Index}").ToHashSet();
            foreach (var uploaded in res.Value.Files ?? new List<SessionFileStatusDto>())
            {
                if (!uploaded.Uploaded) continue;
                var f = m.Files.FirstOrDefault(x => x.Type == uploaded.Type && x.Index == uploaded.Index);
                if (f != null && f.Status == ManifestFileStatus.Pending && !missing.Contains(f.Key) && f.Size == uploaded.SizeBytes)
                {
                    f.Status = ManifestFileStatus.Uploaded;
                }
            }
            Save(m);
        }
        return true;
    }

    private async Task<bool> UploadOneAsync(SessionManifest m, ManifestFile file, CancellationToken ct)
    {
        var s = _getSettings();
        long limit = file.Type is "timelapse" or "gif" ? s.MaxVideoBytes : s.MaxImageBytes;
        if (limit > 0 && file.Size > limit)
        {
            FailFile(m, file, $"File {file.Size / 1024 / 1024} MB vượt giới hạn {limit / 1024 / 1024} MB của server");
            return true;
        }

        _activity = $"Đang gửi {file.Type} #{file.Index} của phiên {m.LocalSessionId}";
        var res = await _client.UploadFileAsync(m.SessionId, file, file.PreparedPath!, ct);
        file.Attempts++;
        if (res.IsSuccess)
        {
            lock (_gate)
            {
                file.Status = ManifestFileStatus.Uploaded;
                file.LastError = null;
                m.Attempts = 0;
                Save(m);
            }
            _lastSuccessAt = _now();
            return true;
        }

        switch (res.Status)
        {
            case 400:
            case 413:
            case 415:
                // Không thử lại file này; vẫn gửi các file khác
                FailFile(m, file, $"HTTP {res.Status}: {res.Message}");
                return true;
            case 404:
            case 409:
                // 404: server mất phiên; 409: file chưa khai báo. Khai báo lại rồi gửi tiếp
                lock (_gate)
                {
                    m.Declared = false;
                    m.Attempts++;
                    m.NextAttemptAt = m.Attempts <= 2 ? null : _now() + Backoff(m.Attempts);
                    Save(m);
                }
                SetError($"Phiên {m.LocalSessionId}: HTTP {res.Status} khi gửi {file.Key} ({res.Message}), khai báo lại");
                return false;
            default:
                HandleSessionError(m, res.Status, res.Message, $"gửi {file.Key}");
                return false;
        }
    }

    private void FailFile(SessionManifest m, ManifestFile file, string reason)
    {
        lock (_gate)
        {
            file.Status = ManifestFileStatus.Failed;
            file.LastError = reason;
            Save(m);
        }
        SetError($"Phiên {m.LocalSessionId}: bỏ {file.Key}: {reason}");
        _log($"[ERROR] Upload queue: {m.LocalSessionId} bỏ {file.Key}: {reason}");
    }

    /// <summary>Xử lý lỗi theo bảng mã lỗi trong INTEGRATION.md.</summary>
    private void HandleSessionError(SessionManifest m, int status, string? message, string step)
    {
        string text = status == 0 ? message ?? "Lỗi mạng" : $"HTTP {status}: {message}";
        switch (status)
        {
            case 401:
                PauseForAuth(message);
                return;
            case 410:
                _log($"[WARN] Upload queue: phiên {m.LocalSessionId} đã hết hạn trên server ({step}), xoá khỏi máy");
                Expire(m);
                return;
            case 400:
            case 409 when step == "init":
                // 400: lỗi lập trình; 409 khi init: shareToken trùng. QR đã in không đổi được, không tự sinh token khác
                lock (_gate)
                {
                    m.State = ManifestState.Failed;
                    m.LastError = $"{step}: {text}";
                    m.LastErrorAt = _now();
                    Save(m);
                }
                SetError($"Phiên {m.LocalSessionId} bị từ chối ({step}): {text}");
                _log($"[ERROR] Upload queue: phiên {m.LocalSessionId} (sessionId {m.SessionId}, token {m.ShareToken}) bị từ chối khi {step}: {text}. Cần xử lý tay.");
                return;
            case 404 when step == "complete":
                lock (_gate)
                {
                    m.Declared = false;
                    Save(m);
                }
                break;
        }

        // Lỗi tạm thời (mạng, timeout, 429, 5xx...): thử lại có backoff
        lock (_gate)
        {
            m.Attempts++;
            m.NextAttemptAt = _now() + Backoff(m.Attempts);
            m.LastError = $"{step}: {text}";
            m.LastErrorAt = _now();
            Save(m);
        }
        SetError($"Phiên {m.LocalSessionId} ({step}): {text}. Thử lại lúc {m.NextAttemptAt!.Value.ToLocalTime():HH:mm:ss}");
        _log($"[WARN] Upload queue: {m.LocalSessionId} {step} lỗi: {text}; thử lại sau {(m.NextAttemptAt.Value - _now()).TotalSeconds:0}s (lần {m.Attempts})");
    }

    private void PauseForAuth(string? message)
    {
        _authPausedUntil = _now() + AuthPause;
        SetError("Server từ chối khoá API (401). Kiểm tra khoá API trong Admin. " + message);
        _log("[ERROR] Upload queue: 401 Unauthorized, tạm dừng 10 phút. Kiểm tra cấu hình khoá API.");
    }

    /// <summary>Lũy thừa có jitter: 30s, 60s, 120s... tối đa 10 phút.</summary>
    public static TimeSpan Backoff(int attempt, Random? random = null)
    {
        double seconds = BackoffStart.TotalSeconds * Math.Pow(2, Math.Clamp(attempt - 1, 0, 10));
        seconds = Math.Min(seconds, BackoffMax.TotalSeconds);
        double jitter = 0.8 + (random ?? Random.Shared).NextDouble() * 0.4;
        return TimeSpan.FromSeconds(Math.Min(seconds * jitter, BackoffMax.TotalSeconds));
    }

    // ------------------------------------------------------------------ hết hạn

    private void PurgeExpired(DateTimeOffset now)
    {
        List<SessionManifest> expired;
        lock (_gate) expired = _manifests.Where(m => now >= m.ExpiresAt).ToList();
        foreach (var m in expired)
        {
            _log($"[INFO] Upload queue: phiên {m.LocalSessionId} quá {m.RetentionHours} giờ (state {m.State}), xoá khỏi máy");
            Expire(m);
        }
    }

    private void Expire(SessionManifest m)
    {
        foreach (var dir in m.LocalDirectories)
        {
            if (!IsSafeSessionDirectory(dir, m.LocalSessionId)) continue;
            try
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _log($"[WARN] Upload queue: không xoá được {dir}: {ex.Message}");
            }
        }
        lock (_gate)
        {
            _manifests.Remove(m);
            _store.Delete(m.SessionId);
        }
    }

    /// <summary>Chỉ xoá thư mục mang đúng tên phiên (SESSION_...), tránh xoá nhầm thư mục khác.</summary>
    public static bool IsSafeSessionDirectory(string dir, string localSessionId)
    {
        if (string.IsNullOrWhiteSpace(dir) || string.IsNullOrWhiteSpace(localSessionId)) return false;
        if (!localSessionId.StartsWith("SESSION_", StringComparison.Ordinal)) return false;
        string name = Path.GetFileName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(dir)));
        return string.Equals(name, localSessionId, StringComparison.OrdinalIgnoreCase);
    }

    private void LogWarningIfStale(DateTimeOffset now)
    {
        if (now - _lastWarnLog < TimeSpan.FromMinutes(15)) return;
        var status = GetStatus();
        if (!status.Warning) return;
        _lastWarnLog = now;
        _log($"[WARN] Upload queue: {status.WarningMessage} ({status.PendingSessions} phiên chờ, lỗi gần nhất: {status.LastError})");
    }

    private void Save(SessionManifest m)
    {
        // Phiên đã bị xoá (hết hạn) trong lúc đang gửi thì không ghi lại
        if (_manifests.Contains(m)) _store.Save(m);
    }

    private void SetError(string message)
    {
        _lastError = message;
        _lastErrorAt = _now();
    }

    private static string GuessContentType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".webp" => "image/webp",
        ".gif" => "image/gif",
        ".mp4" => "video/mp4",
        _ => "application/octet-stream",
    };
}
