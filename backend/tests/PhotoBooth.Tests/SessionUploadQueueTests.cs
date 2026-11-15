using System.Net;
using System.Text;
using System.Text.Json;
using PhotoBooth.Core.Cloud;

namespace PhotoBooth.Tests;

/// <summary>Hàng đợi gửi phiên chụp: thứ tự gửi, bảng mã lỗi trong INTEGRATION.md, hết hạn, khởi động lại.</summary>
public sealed class SessionUploadQueueTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pb-upload-tests-" + Guid.NewGuid().ToString("N"));
    private readonly FakeServer _server = new();
    private CloudSettings _settings = new() { BaseUrl = "https://api.test", ApiKey = "test-key", DeviceId = "KIOSK-T", MaxUploadKBps = 0 };
    private DateTimeOffset _now = new(2026, 11, 15, 8, 0, 0, TimeSpan.Zero);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    private SessionUploadQueue NewQueue(Func<bool>? busy = null)
    {
        var client = new KioskSessionsClient(() => _settings, _server);
        var queue = new SessionUploadQueue(
            new ManifestStore(Path.Combine(_root, "Outbox")),
            client,
            () => _settings,
            s => _settings = s,
            (f, target) => File.Copy(f.SourcePath, target, true),
            busy,
            null,
            () => _now);
        queue.LoadFromDisk();
        return queue;
    }

    private SessionManifest NewSession(string local = "SESSION_20261115_080000_aaaaaa")
    {
        string sessDir = Path.Combine(_root, "Sessions", local);
        string galDir = Path.Combine(_root, "Gallery", local);
        Directory.CreateDirectory(sessDir);
        Directory.CreateDirectory(galDir);
        File.WriteAllBytes(Path.Combine(sessDir, "composite_final.jpg"), new byte[] { 0xFF, 0xD8, 0xFF, 1, 2, 3 });
        File.WriteAllBytes(Path.Combine(sessDir, "shot_1.jpg"), new byte[] { 0xFF, 0xD8, 0xFF, 4 });
        File.WriteAllBytes(Path.Combine(sessDir, "shot_2.jpg"), new byte[] { 0xFF, 0xD8, 0xFF, 5 });
        File.WriteAllBytes(Path.Combine(sessDir, "timelapse.mp4"), new byte[] { 0, 0, 0, 8, (byte)'f', (byte)'t', (byte)'y', (byte)'p' });
        return new SessionManifest
        {
            SessionId = Guid.NewGuid(),
            ShareToken = ShareIds.NewShareToken(),
            LocalSessionId = local,
            CapturedAt = _now,
            LocalDirectories = { sessDir, galDir },
            Files =
            {
                new() { Type = "timelapse", Index = 0, SourcePath = Path.Combine(sessDir, "timelapse.mp4"), ContentType = "video/mp4" },
                new() { Type = "original", Index = 1, SourcePath = Path.Combine(sessDir, "shot_2.jpg"), Prep = FilePrep.Original },
                new() { Type = "original", Index = 0, SourcePath = Path.Combine(sessDir, "shot_1.jpg"), Prep = FilePrep.Original },
                new() { Type = "thumbnail", Index = 0, SourcePath = Path.Combine(sessDir, "composite_final.jpg"), Prep = FilePrep.Thumbnail },
                new() { Type = "collage", Index = 0, SourcePath = Path.Combine(sessDir, "composite_final.jpg"), ContentType = "image/jpeg" },
            },
        };
    }

    private static async Task Drain(SessionUploadQueue queue, int ticks = 6)
    {
        for (int i = 0; i < ticks; i++) await queue.TickAsync(CancellationToken.None);
    }

    [Fact]
    public void ShareToken_Is22Base62Chars()
    {
        var token = ShareIds.NewShareToken();
        Assert.Equal(22, token.Length);
        Assert.Matches("^[A-Za-z0-9]{22}$", token);
        Assert.NotEqual(token, ShareIds.NewShareToken());
        Assert.Equal("https://x/s/abc", ShareIds.BuildShareUrl("https://x/s/", "abc"));
    }

    [Fact]
    public void Backoff_StartsAround30s_CapsAt10Minutes()
    {
        var rnd = new Random(1);
        var first = SessionUploadQueue.Backoff(1, rnd);
        Assert.InRange(first.TotalSeconds, 24, 36);
        Assert.InRange(SessionUploadQueue.Backoff(20, rnd).TotalSeconds, 480, 600);
    }

    [Fact]
    public void SafeDirectory_OnlyMatchesSessionFolderName()
    {
        Assert.True(SessionUploadQueue.IsSafeSessionDirectory(@"D:\G\2026-11-15\SESSION_1_abc", "SESSION_1_abc"));
        Assert.False(SessionUploadQueue.IsSafeSessionDirectory(@"D:\G\2026-11-15", "SESSION_1_abc"));
        Assert.False(SessionUploadQueue.IsSafeSessionDirectory(@"D:\G\foo", "foo"));
        Assert.False(SessionUploadQueue.IsSafeSessionDirectory("", "SESSION_1_abc"));
    }

    [Fact]
    public async Task HappyPath_UploadsInOrder_ThenReady()
    {
        var queue = NewQueue();
        var m = NewSession();
        queue.Enqueue(m);
        await Drain(queue);

        Assert.Equal(1, queue.GetStatus().ReadySessions);
        Assert.Equal(
            new[] { "collage#0", "original#0", "original#1", "thumbnail#0", "timelapse#0" },
            _server.Uploads.Select(u => u.Key).ToArray());
        Assert.All(_server.Requests, r => Assert.Equal("test-key", r.ApiKey));
        Assert.All(_server.Requests, r => Assert.Equal("KIOSK-T", r.DeviceId));
        // Init khai báo đủ 5 file kèm dung lượng
        Assert.Equal(5, _server.LastInitFiles);
        Assert.Equal("https://api.test/s", _settings.CachedShareBaseUrl);
    }

    [Fact]
    public async Task NetworkError_SchedulesBackoff_AndRecovers()
    {
        var queue = NewQueue();
        queue.Enqueue(NewSession());
        _server.Fail = (req, n) => req.Path.EndsWith("/files") ? HttpStatusCode.ServiceUnavailable : null;
        await Drain(queue, 2);

        var st = queue.GetStatus();
        Assert.Equal(1, st.PendingSessions);
        Assert.Contains("503", st.LastError);

        // Chưa tới giờ thử lại: không gửi gì thêm
        int before = _server.Requests.Count;
        _now += TimeSpan.FromSeconds(5);
        await Drain(queue, 2);
        Assert.Equal(before, _server.Requests.Count);

        _server.Fail = null;
        _now += TimeSpan.FromMinutes(1);
        await Drain(queue);
        Assert.Equal(1, queue.GetStatus().ReadySessions);
    }

    [Fact]
    public async Task Unauthorized_PausesQueue()
    {
        var queue = NewQueue();
        queue.Enqueue(NewSession());
        _server.Fail = (_, _) => HttpStatusCode.Unauthorized;
        await Drain(queue, 3);

        int count = _server.Requests.Count;
        await Drain(queue, 3);
        Assert.Equal(count, _server.Requests.Count);
        Assert.Contains("401", queue.GetStatus().LastError);

        // Sửa khoá trong admin -> RetryNow -> gửi tiếp
        _server.Fail = null;
        queue.RetryNow();
        await Drain(queue);
        Assert.Equal(1, queue.GetStatus().ReadySessions);
    }

    [Fact]
    public async Task UnsupportedFile_IsSkipped_OthersStillSent()
    {
        var queue = NewQueue();
        queue.Enqueue(NewSession());
        _server.Fail = (req, _) => req.Path.EndsWith("/files") && req.UploadKey == "timelapse#0" ? HttpStatusCode.UnsupportedMediaType : null;
        await Drain(queue);

        var st = queue.GetStatus();
        Assert.Equal(1, st.ReadySessions);
        Assert.Equal(4, _server.Uploads.Count(u => u.Status == 200));
    }

    [Fact]
    public async Task SessionMissingOnServer_ReInitsAndContinues()
    {
        var queue = NewQueue();
        queue.Enqueue(NewSession());
        int fails = 0;
        _server.Fail = (req, _) => req.Path.EndsWith("/files") && req.UploadKey == "original#0" && fails++ == 0 ? HttpStatusCode.NotFound : null;
        await Drain(queue, 8);

        Assert.Equal(1, queue.GetStatus().ReadySessions);
        Assert.Equal(2, _server.Requests.Count(r => r.Method == "POST" && r.Path == "/api/kiosk/sessions"));
    }

    [Fact]
    public async Task TokenConflictOnInit_MarksFailed_DoesNotRetry()
    {
        var queue = NewQueue();
        queue.Enqueue(NewSession());
        _server.Fail = (req, _) => req.Path == "/api/kiosk/sessions" && req.Method == "POST" ? HttpStatusCode.Conflict : null;
        await Drain(queue, 4);

        var st = queue.GetStatus();
        Assert.Equal(1, st.FailedSessions);
        Assert.True(st.Warning);
        Assert.Equal(1, _server.Requests.Count(r => r.Path == "/api/kiosk/sessions" && r.Method == "POST"));
    }

    [Fact]
    public async Task Gone_DeletesLocalSession()
    {
        var queue = NewQueue();
        var m = NewSession();
        queue.Enqueue(m);
        _server.Fail = (req, _) => req.Path == "/api/kiosk/sessions" && req.Method == "POST" ? HttpStatusCode.Gone : null;
        await Drain(queue, 2);

        Assert.All(m.LocalDirectories, d => Assert.False(Directory.Exists(d)));
        Assert.Equal(0, queue.GetStatus().PendingSessions);
    }

    [Fact]
    public async Task Expiry_DeletesLocalDirectories_EvenIfNotUploaded()
    {
        var queue = NewQueue();
        var m = NewSession();
        _server.Fail = (_, _) => HttpStatusCode.ServiceUnavailable;
        queue.Enqueue(m);
        await Drain(queue, 2);
        Assert.True(Directory.Exists(m.LocalDirectories[0]));

        _now += TimeSpan.FromHours(24) + TimeSpan.FromMinutes(1);
        await Drain(queue, 1);
        Assert.All(m.LocalDirectories, d => Assert.False(Directory.Exists(d)));
        Assert.Empty(Directory.GetFiles(Path.Combine(_root, "Outbox")));
    }

    [Fact]
    public async Task StaleSession_RaisesWarningAfterOneHour()
    {
        var queue = NewQueue();
        queue.Enqueue(NewSession());
        _server.Fail = (_, _) => null;
        _settings.Enabled = false;
        _now += TimeSpan.FromMinutes(61);
        await Drain(queue, 1);
        Assert.True(queue.GetStatus().Warning);
    }

    [Fact]
    public async Task Restart_ResumesFromDisk_SkipsFilesServerAlreadyHas()
    {
        var queue = NewQueue();
        var m = NewSession();
        queue.Enqueue(m);
        // Gửi được ảnh ghép thì "mất điện"
        _server.Fail = (req, _) => req.Path.EndsWith("/files") && req.UploadKey != "collage#0" ? HttpStatusCode.ServiceUnavailable : null;
        await Drain(queue, 2);
        Assert.Single(_server.Uploads, u => u.Status == 200);

        // Manifest trên đĩa bị mất dấu "đã gửi" (vd. mất điện trước khi ghi), server vẫn có file
        var path = Directory.GetFiles(Path.Combine(_root, "Outbox")).Single();
        var json = File.ReadAllText(path).Replace("\"uploaded\"", "\"pending\"").Replace("\"declared\": true", "\"declared\": false");
        File.WriteAllText(path, json);

        _server.Fail = null;
        var restarted = NewQueue();
        _now += TimeSpan.FromMinutes(5);
        await Drain(restarted);

        Assert.Equal(1, restarted.GetStatus().ReadySessions);
        // Ảnh ghép không bị gửi lại lần hai
        Assert.Single(_server.Uploads, u => u.Key == "collage#0" && u.Status == 200);
    }

    [Fact]
    public async Task PausesWhileCustomerIsShooting()
    {
        bool busy = true;
        var queue = NewQueue(() => busy);
        queue.Enqueue(NewSession());
        await Drain(queue, 3);
        Assert.Empty(_server.Requests);
        Assert.True(queue.GetStatus().PausedForCapture);

        busy = false;
        await Drain(queue);
        Assert.Equal(1, queue.GetStatus().ReadySessions);
    }

    [Fact]
    public async Task LateVideo_AttachedBeforeEnqueue_IsDeclaredAndSent()
    {
        var queue = NewQueue();
        var m = NewSession();
        var video = m.Files.Single(f => f.Type == "timelapse");
        m.Files.Remove(video);
        queue.AttachFile(m.SessionId, video);
        queue.Enqueue(m);
        await Drain(queue);
        Assert.Contains(_server.Uploads, u => u.Key == "timelapse#0" && u.Status == 200);
    }

    // ------------------------------------------------------------------ server giả

    private sealed record Req(string Method, string Path, string? ApiKey, string? DeviceId, string? UploadKey);

    private sealed class FakeServer : HttpMessageHandler
    {
        private readonly Dictionary<Guid, Dictionary<string, long>> _sessions = new();
        private readonly Dictionary<Guid, HashSet<string>> _declared = new();
        public List<Req> Requests { get; } = new();
        public List<(string Key, int Status)> Uploads { get; } = new();
        public int LastInitFiles { get; private set; }
        public Func<Req, int, HttpStatusCode?>? Fail { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            string path = request.RequestUri!.AbsolutePath;
            string? uploadKey = null;
            long uploadSize = 0;
            if (request.Content is MultipartFormDataContent form)
            {
                string type = "", index = "";
                foreach (var part in form)
                {
                    string name = part.Headers.ContentDisposition?.Name?.Trim('"') ?? "";
                    if (name == "type") type = await part.ReadAsStringAsync(ct);
                    else if (name == "index") index = await part.ReadAsStringAsync(ct);
                    else if (name == "file") uploadSize = (await part.ReadAsByteArrayAsync(ct)).Length;
                }
                uploadKey = $"{type}#{index}";
            }
            string? body = request.Content is StringContent sc ? await sc.ReadAsStringAsync(ct) : null;
            var req = new Req(request.Method.Method, path,
                request.Headers.TryGetValues("X-Api-Key", out var k) ? k.First() : null,
                request.Headers.TryGetValues("X-Device-Id", out var d) ? d.First() : null,
                uploadKey);
            Requests.Add(req);

            var fail = Fail?.Invoke(req, Requests.Count);
            if (fail != null)
            {
                if (uploadKey != null) Uploads.Add((uploadKey, (int)fail));
                return Json(fail.Value, new { message = "fake " + (int)fail });
            }

            if (path == "/api/kiosk/sessions/config")
            {
                return Json(HttpStatusCode.OK, new { shareBaseUrl = "https://api.test/s/", retentionHours = 24, maxImageBytes = 1000000, maxVideoBytes = 1000000, maxFilesPerSession = 40, fileTypes = new[] { "collage" } });
            }
            if (path == "/api/kiosk/sessions" && request.Method == HttpMethod.Post)
            {
                using var doc = JsonDocument.Parse(body!);
                var id = doc.RootElement.GetProperty("sessionId").GetGuid();
                var files = doc.RootElement.GetProperty("files").EnumerateArray()
                    .Select(f => $"{f.GetProperty("type").GetString()}#{f.GetProperty("index").GetInt32()}").ToList();
                LastInitFiles = files.Count;
                if (!_sessions.ContainsKey(id)) _sessions[id] = new();
                if (!_declared.TryGetValue(id, out var set)) _declared[id] = set = new();
                foreach (var f in files) set.Add(f);
                return Json(HttpStatusCode.Created, SessionDto(id));
            }
            var parts = path.Split('/');
            var sid = Guid.Parse(parts[4]);
            if (path.EndsWith("/files"))
            {
                if (!_sessions.ContainsKey(sid)) return Json(HttpStatusCode.NotFound, new { message = "no session" });
                if (!_declared[sid].Contains(uploadKey!)) return Json(HttpStatusCode.Conflict, new { message = "not declared" });
                _sessions[sid][uploadKey!] = uploadSize;
                Uploads.Add((uploadKey!, 200));
                var sp = uploadKey!.Split('#');
                return Json(HttpStatusCode.OK, new { type = sp[0], index = int.Parse(sp[1]), sizeBytes = uploadSize, contentType = "image/jpeg", sessionStatus = "Partial" });
            }
            if (path.EndsWith("/complete"))
            {
                if (!_sessions.ContainsKey(sid)) return Json(HttpStatusCode.NotFound, new { message = "no session" });
                return Json(HttpStatusCode.OK, SessionDto(sid));
            }
            return Json(HttpStatusCode.NotFound, new { message = "route" });
        }

        private object SessionDto(Guid id)
        {
            var have = _sessions[id];
            var declared = _declared.TryGetValue(id, out var d) ? d : new HashSet<string>();
            var missing = declared.Where(k => !have.ContainsKey(k)).Select(k => new { type = k.Split('#')[0], index = int.Parse(k.Split('#')[1]) }).ToList();
            return new
            {
                sessionId = id,
                shareToken = "t",
                shareUrl = "https://api.test/s/t",
                status = missing.Count == 0 ? "Ready" : have.Count == 0 ? "Pending" : "Partial",
                capturedAt = DateTime.UtcNow,
                expiresAt = DateTime.UtcNow.AddHours(24),
                files = have.Select(kv => new { type = kv.Key.Split('#')[0], index = int.Parse(kv.Key.Split('#')[1]), uploaded = true, sizeBytes = kv.Value, contentType = "image/jpeg" }).ToList(),
                missing,
            };
        }

        private static HttpResponseMessage Json(HttpStatusCode code, object body) => new(code)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
        };
    }
}
