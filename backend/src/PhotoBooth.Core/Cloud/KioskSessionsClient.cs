using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PhotoBooth.Core.Cloud;

/// <summary>Kết quả một lần gọi API. Status = 0 nghĩa là lỗi mạng / hết thời gian chờ (không có phản hồi).</summary>
public sealed record ApiResult<T>(int Status, T? Value, string? Message)
{
    public bool IsSuccess => Status is >= 200 and < 300;

    /// <summary>Lỗi tạm thời: thử lại có backoff.</summary>
    public bool IsTransient => Status == 0 || Status == 408 || Status == 429 || Status >= 500;
}

public sealed record SessionFileRefDto(string Type, int Index);

public sealed record SessionFileStatusDto(string Type, int Index, bool Uploaded, long SizeBytes, string? ContentType);

/// <summary>Phản hồi init / get / complete.</summary>
public sealed record KioskSessionDto(
    Guid SessionId,
    string ShareToken,
    string ShareUrl,
    string Status,
    DateTimeOffset CapturedAt,
    DateTimeOffset ExpiresAt,
    List<SessionFileStatusDto>? Files,
    List<SessionFileRefDto>? Missing);

public sealed record KioskSessionConfigDto(
    string ShareBaseUrl,
    int RetentionHours,
    long MaxImageBytes,
    long MaxVideoBytes,
    int MaxFilesPerSession,
    string[]? FileTypes);

public sealed record UploadFileResultDto(string Type, int Index, long SizeBytes, string ContentType, string SessionStatus);

/// <summary>
/// Gọi API <c>/api/kiosk/sessions</c> của backend Favori (xem INTEGRATION.md).
/// Xác thực bằng header X-Api-Key + X-Device-Id. Không ghi khoá ra log.
/// </summary>
public sealed class KioskSessionsClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _http;
    private readonly Func<CloudSettings> _settings;

    public KioskSessionsClient(Func<CloudSettings> settings, HttpMessageHandler? handler = null)
    {
        _settings = settings;
        // Thời gian chờ tính riêng cho từng request bằng CancellationToken (video lớn cần lâu hơn)
        _http = handler == null ? new HttpClient() : new HttpClient(handler);
        _http.Timeout = Timeout.InfiniteTimeSpan;
    }

    public void Dispose() => _http.Dispose();

    private HttpRequestMessage NewRequest(HttpMethod method, string path)
    {
        var s = _settings();
        var request = new HttpRequestMessage(method, s.BaseUrl.TrimEnd('/') + path);
        request.Headers.TryAddWithoutValidation("X-Api-Key", s.ApiKey);
        request.Headers.TryAddWithoutValidation("X-Device-Id", s.EffectiveDeviceId);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    public Task<ApiResult<KioskSessionConfigDto>> GetConfigAsync(CancellationToken ct) =>
        SendAsync<KioskSessionConfigDto>(() => NewRequest(HttpMethod.Get, "/api/kiosk/sessions/config"), TimeSpan.FromSeconds(15), ct);

    public Task<ApiResult<KioskSessionDto>> InitAsync(SessionManifest m, CancellationToken ct)
    {
        var body = new
        {
            sessionId = m.SessionId,
            shareToken = m.ShareToken,
            orderId = m.OrderId,
            deviceId = _settings().EffectiveDeviceId,
            frameId = m.FrameId,
            layoutId = m.LayoutId,
            capturedAt = m.CapturedAt.UtcDateTime,
            printQuantity = Math.Max(1, m.PrintQuantity),
            files = m.Files
                .Where(f => f.Status != ManifestFileStatus.Failed)
                .Select(f => new { type = f.Type, index = f.Index, contentType = f.ContentType, size = f.Size > 0 ? f.Size : (long?)null })
                .ToList(),
        };
        string json = JsonSerializer.Serialize(body, JsonOptions);
        return SendAsync<KioskSessionDto>(() =>
        {
            var req = NewRequest(HttpMethod.Post, "/api/kiosk/sessions");
            req.Content = new StringContent(json, Encoding.UTF8, "application/json");
            return req;
        }, TimeSpan.FromSeconds(30), ct);
    }

    public Task<ApiResult<KioskSessionDto>> GetSessionAsync(Guid sessionId, CancellationToken ct) =>
        SendAsync<KioskSessionDto>(() => NewRequest(HttpMethod.Get, $"/api/kiosk/sessions/{sessionId}"), TimeSpan.FromSeconds(30), ct);

    public Task<ApiResult<KioskSessionDto>> CompleteAsync(Guid sessionId, CancellationToken ct) =>
        SendAsync<KioskSessionDto>(() => NewRequest(HttpMethod.Post, $"/api/kiosk/sessions/{sessionId}/complete"), TimeSpan.FromSeconds(30), ct);

    /// <summary>Gửi một file (multipart: type, index, file). Tốc độ gửi giới hạn theo MaxUploadKBps.</summary>
    public async Task<ApiResult<UploadFileResultDto>> UploadFileAsync(Guid sessionId, ManifestFile file, string path, CancellationToken ct)
    {
        var s = _settings();
        long length = new FileInfo(path).Length;
        long bytesPerSecond = s.MaxUploadKBps > 0 ? s.MaxUploadKBps * 1024L : 0;
        // Đủ thời gian cho file lớn ở tốc độ giới hạn, cộng thêm 2 phút
        var timeout = TimeSpan.FromMinutes(2) + (bytesPerSecond > 0
            ? TimeSpan.FromSeconds(length / (double)bytesPerSecond * 2)
            : TimeSpan.FromSeconds(length / (256.0 * 1024)));

        FileStream? stream = null;
        try
        {
            return await SendAsync<UploadFileResultDto>(() =>
            {
                stream?.Dispose();
                stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, useAsync: true);
                var content = new StreamContent(new ThrottledReadStream(stream, bytesPerSecond), 64 * 1024);
                content.Headers.ContentType = new MediaTypeHeaderValue(file.ContentType ?? "application/octet-stream");
                content.Headers.ContentLength = length;
                var form = new MultipartFormDataContent
                {
                    { new StringContent(file.Type), "type" },
                    { new StringContent(file.Index.ToString(System.Globalization.CultureInfo.InvariantCulture)), "index" },
                    { content, "file", Path.GetFileName(path) },
                };
                var req = NewRequest(HttpMethod.Post, $"/api/kiosk/sessions/{sessionId}/files");
                req.Content = form;
                return req;
            }, timeout, ct);
        }
        finally
        {
            stream?.Dispose();
        }
    }

    private async Task<ApiResult<T>> SendAsync<T>(Func<HttpRequestMessage> build, TimeSpan timeout, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try
        {
            using var request = build();
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, cts.Token);
            string text = await response.Content.ReadAsStringAsync(cts.Token);
            int status = (int)response.StatusCode;
            if (status is >= 200 and < 300)
            {
                T? value = default;
                if (!string.IsNullOrWhiteSpace(text))
                {
                    try { value = JsonSerializer.Deserialize<T>(text, JsonOptions); }
                    catch (JsonException ex) { return new ApiResult<T>(status, default, "Phản hồi không đọc được: " + ex.Message); }
                }
                return new ApiResult<T>(status, value, null);
            }
            return new ApiResult<T>(status, default, ReadMessage(text) ?? response.ReasonPhrase);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new ApiResult<T>(0, default, "Hết thời gian chờ server");
        }
        catch (HttpRequestException ex)
        {
            return new ApiResult<T>(0, default, "Lỗi mạng: " + ex.Message);
        }
        catch (IOException ex)
        {
            return new ApiResult<T>(0, default, "Lỗi đọc file / mạng: " + ex.Message);
        }
    }

    private static string? ReadMessage(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        try
        {
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String)
            {
                return m.GetString();
            }
        }
        catch (JsonException) { }
        return text.Length > 200 ? text[..200] : text;
    }
}

/// <summary>Stream đọc có giới hạn tốc độ (byte/giây), để upload chạy nền không chiếm hết đường mạng.</summary>
public sealed class ThrottledReadStream : Stream
{
    private readonly Stream _inner;
    private readonly long _bytesPerSecond;
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
    private long _sent;

    public ThrottledReadStream(Stream inner, long bytesPerSecond)
    {
        _inner = inner;
        _bytesPerSecond = bytesPerSecond;
    }

    public override bool CanRead => true;
    public override bool CanSeek => _inner.CanSeek;
    public override bool CanWrite => false;
    public override long Length => _inner.Length;
    public override long Position { get => _inner.Position; set => _inner.Position = value; }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override int Read(byte[] buffer, int offset, int count) =>
        ReadAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult();

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
        await ReadAsync(buffer.AsMemory(offset, count), ct);

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        if (_bytesPerSecond > 0)
        {
            // Mỗi lần đọc tối đa ~1/8 giây dữ liệu, rồi chờ nếu đang gửi nhanh hơn giới hạn
            int chunk = (int)Math.Max(4096, Math.Min(buffer.Length, _bytesPerSecond / 8));
            buffer = buffer[..Math.Min(buffer.Length, chunk)];
            double aheadMs = _sent * 1000.0 / _bytesPerSecond - _clock.Elapsed.TotalMilliseconds;
            if (aheadMs > 1) await Task.Delay(TimeSpan.FromMilliseconds(aheadMs), ct);
        }
        int read = await _inner.ReadAsync(buffer, ct);
        _sent += read;
        return read;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _inner.Dispose();
        base.Dispose(disposing);
    }
}
