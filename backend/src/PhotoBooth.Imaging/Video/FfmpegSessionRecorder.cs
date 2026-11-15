using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PhotoBooth.Imaging.Video;

/// <summary>Tìm ffmpeg.exe trên máy kiosk.</summary>
public static class FfmpegLocator
{
    /// <summary>Biến môi trường chỉ thẳng tới ffmpeg.exe (ưu tiên cao nhất).</summary>
    public const string EnvVar = "HIHIBOOTH_FFMPEG";

    /// <summary>
    /// Thứ tự tìm: biến môi trường, cạnh file chạy của app, đường dẫn FavoriCurrent đang dùng,
    /// <c>C:\ffmpeg\bin</c>, rồi PATH. Trả về null khi không có.
    /// </summary>
    public static string? Find()
    {
        var candidates = new List<string?>
        {
            Environment.GetEnvironmentVariable(EnvVar),
            Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe"),
            Path.Combine(AppContext.BaseDirectory, "ffmpeg", "ffmpeg.exe"),
            @"C:\ffmpeg-7.1.1-full_build\bin\ffmpeg.exe",
            @"C:\ffmpeg\bin\ffmpeg.exe",
        };
        foreach (string dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try { candidates.Add(Path.Combine(dir.Trim().Trim('"'), "ffmpeg.exe")); } catch (ArgumentException) { }
        }
        foreach (string? candidate in candidates)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate)) return Path.GetFullPath(candidate);
            }
            catch (Exception) { }
        }
        return null;
    }
}

/// <summary>Kết quả dựng video phiên: đường dẫn từng file, null nếu file đó không dựng được.</summary>
public sealed record SessionVideoResult(string? FullVideo, string? FastVideo);

/// <summary>
/// Ghi video phiên chụp theo cách của FavoriCurrent: trong lúc chụp chỉ ghi từng khung live view thành file JPEG,
/// chụp xong mới chạy ffmpeg (libx264) dựng MP4. Lúc chụp không tốn CPU mã hoá nên live view không bị giật.
/// Mỗi khung kèm mốc thời gian thật, nên video đầy đủ dài đúng bằng thời gian chụp; bản timelapse là
/// cùng các khung đó tua nhanh <c>speed</c> lần. Một lần chạy ffmpeg xuất cả hai file.
/// </summary>
public sealed class FfmpegSessionRecorder : IDisposable
{
    private const int QueueLimit = 150;
    /// <summary>Khoảng trống dài nhất giữ nguyên một khung (vd. máy ảnh tạm dừng live view để chụp).</summary>
    private const int MaxGapMs = 5000;
    private const string Preset = "ultrafast";
    private const int Crf = 30;
    private static readonly TimeSpan EncodeTimeout = TimeSpan.FromMinutes(5);

    private readonly string _ffmpegPath;
    private readonly string _framesDir;
    private readonly int _fps;
    private readonly int _maxWidth;
    private readonly Action<string> _log;
    private readonly BlockingCollection<(byte[] Jpeg, long TimestampMs)> _queue = new(QueueLimit);
    private readonly List<(string File, long TimestampMs)> _frames = new();
    private readonly Thread _writer;
    private int _finishRequested;
    private int _dropped;

    public int FramesWritten { get; private set; }

    public FfmpegSessionRecorder(string ffmpegPath, string framesDir, int fps = 15, int maxWidth = 1280, Action<string>? log = null)
    {
        _ffmpegPath = ffmpegPath;
        _framesDir = framesDir;
        _fps = Math.Max(1, fps);
        _maxWidth = Math.Max(320, maxWidth);
        _log = log ?? (_ => { });

        // Khung của lần ghi trước (app bị tắt giữa chừng) không được lẫn vào video mới
        if (Directory.Exists(_framesDir)) Directory.Delete(_framesDir, true);
        Directory.CreateDirectory(_framesDir);

        _writer = new Thread(WriteLoop) { IsBackground = true, Name = "SessionFrameWriter" };
        _writer.Start();
        _log($"[INFO] Session video (ffmpeg): ghi khung vào {_framesDir}");
    }

    /// <summary>Thêm một khung JPEG chụp lúc <paramref name="timestampMs"/> (đồng hồ tăng dần, ms). Không chặn luồng gọi.</summary>
    public bool AddFrame(byte[] jpeg, long timestampMs)
    {
        if (Volatile.Read(ref _finishRequested) != 0 || jpeg.Length == 0) return false;
        try
        {
            if (_queue.TryAdd((jpeg, timestampMs))) return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        Interlocked.Increment(ref _dropped);
        return false;
    }

    private void WriteLoop()
    {
        foreach (var (jpeg, timestampMs) in _queue.GetConsumingEnumerable())
        {
            try
            {
                string name = $"frame_{_frames.Count + 1:D6}.jpg";
                File.WriteAllBytes(Path.Combine(_framesDir, name), jpeg);
                _frames.Add((name, timestampMs));
                FramesWritten = _frames.Count;
            }
            catch (Exception ex)
            {
                // Đĩa đầy / lỗi ghi: bỏ khung này, video vẫn dựng từ các khung đã có
                Interlocked.Increment(ref _dropped);
                if (_dropped % 100 == 1) _log($"[WARN] Session video: không ghi được khung ({ex.Message})");
            }
        }
    }

    /// <summary>
    /// Ngừng nhận khung rồi chạy ffmpeg dựng video đầy đủ và (nếu có <paramref name="fastOutputPath"/>) bản tua nhanh.
    /// Xong thì xoá thư mục khung. Không ném lỗi: file nào không dựng được thì trả về null.
    /// </summary>
    public async Task<SessionVideoResult> FinishAsync(string fullOutputPath, string? fastOutputPath, int fastSpeed)
    {
        if (Interlocked.Exchange(ref _finishRequested, 1) == 0) _queue.CompleteAdding();
        await Task.Run(() => _writer.Join());

        try
        {
            if (_frames.Count < 2)
            {
                _log("[INFO] Session video skipped: not enough frames");
                return new SessionVideoResult(null, null);
            }

            string listPath = Path.Combine(_framesDir, "frames.txt");
            await File.WriteAllTextAsync(listPath, BuildConcatList(_frames, _fps, MaxGapMs), new UTF8Encoding(false));

            foreach (string? output in new[] { fullOutputPath, fastOutputPath })
            {
                if (output == null) continue;
                string? dir = Path.GetDirectoryName(output);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                if (File.Exists(output)) File.Delete(output);
            }

            string args = BuildArguments("frames.txt", fullOutputPath, fastOutputPath, _fps, _maxWidth, Math.Max(1, fastSpeed), Preset, Crf);
            var watch = Stopwatch.StartNew();
            (int exitCode, string stderr) = await RunFfmpegAsync(args);
            if (exitCode != 0)
            {
                _log($"[ERROR] Session video: ffmpeg thoát mã {exitCode}. {Tail(stderr)}");
            }

            // ffmpeg lỗi giữa chừng có thể để lại file dở: chỉ nhận file khi ffmpeg thoát bình thường
            bool ok = exitCode == 0;
            string? full = ok && IsUsable(fullOutputPath) ? fullOutputPath : null;
            string? fast = ok && fastOutputPath != null && IsUsable(fastOutputPath) ? fastOutputPath : null;
            _log($"[INFO] Session video saved (ffmpeg, {watch.ElapsedMilliseconds} ms): {_frames.Count} khung, {_dropped} bị bỏ, " +
                 $"đầy đủ={(full != null ? "ok" : "lỗi")}, tua nhanh={(fastOutputPath == null ? "tắt" : fast != null ? "ok" : "lỗi")}");
            return new SessionVideoResult(full, fast);
        }
        catch (Exception ex)
        {
            _log($"[ERROR] Session video (ffmpeg) failed: {ex.Message}");
            return new SessionVideoResult(null, null);
        }
        finally
        {
            TryDeleteFrames();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _finishRequested, 1) == 0) _queue.CompleteAdding();
    }

    /// <summary>
    /// Danh sách cho concat demuxer của ffmpeg: mỗi khung hiện tới khi có khung kế tiếp (tối đa <paramref name="maxGapMs"/>),
    /// khung cuối hiện một nhịp. Nhờ vậy video dài đúng thời gian thật dù live view có lúc dừng.
    /// </summary>
    public static string BuildConcatList(IReadOnlyList<(string File, long TimestampMs)> frames, int fps, int maxGapMs)
    {
        var sb = new StringBuilder("ffconcat version 1.0\n");
        double frameSeconds = 1.0 / Math.Max(1, fps);
        for (int i = 0; i < frames.Count; i++)
        {
            double seconds = i + 1 < frames.Count
                ? Math.Clamp(frames[i + 1].TimestampMs - frames[i].TimestampMs, 1, Math.Max(1, maxGapMs)) / 1000.0
                : frameSeconds;
            sb.Append("file '").Append(frames[i].File).Append("'\n");
            sb.Append("duration ").Append(seconds.ToString("0.###", CultureInfo.InvariantCulture)).Append('\n');
        }
        // concat demuxer bỏ qua duration của mục cuối nếu không lặp lại file đó
        if (frames.Count > 0) sb.Append("file '").Append(frames[^1].File).Append("'\n");
        return sb.ToString();
    }

    /// <summary>Tham số ffmpeg: đọc danh sách khung một lần, xuất video đầy đủ và (tuỳ chọn) bản tua nhanh.</summary>
    public static string BuildArguments(string listFile, string fullOutput, string? fastOutput, int fps, int maxWidth, int fastSpeed, string preset, int crf)
    {
        string scale = $"scale=trunc(min({maxWidth}\\,iw)/2)*2:-2";
        string codec = $"-c:v libx264 -preset {preset} -crf {crf} -pix_fmt yuv420p -movflags +faststart -an";
        var sb = new StringBuilder($"-y -hide_banner -loglevel error -f concat -safe 0 -i \"{listFile}\"");
        sb.Append($" -vf \"fps={fps},{scale}\" {codec} \"{fullOutput}\"");
        if (fastOutput != null)
        {
            sb.Append($" -vf \"setpts=PTS/{fastSpeed},fps={fps},{scale}\" {codec} \"{fastOutput}\"");
        }
        return sb.ToString();
    }

    private async Task<(int ExitCode, string Stderr)> RunFfmpegAsync(string arguments)
    {
        var info = new ProcessStartInfo
        {
            FileName = _ffmpegPath,
            Arguments = arguments,
            WorkingDirectory = _framesDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        using var process = new Process { StartInfo = info };
        process.Start();
        try { process.PriorityClass = ProcessPriorityClass.BelowNormal; } catch (Exception) { }

        Task<string> stderr = process.StandardError.ReadToEndAsync();
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(EncodeTimeout);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (Exception) { }
            return (-1, "ffmpeg quá thời gian chờ");
        }
        await stdout;
        return (process.ExitCode, await stderr);
    }

    private static bool IsUsable(string path)
    {
        try { return File.Exists(path) && new FileInfo(path).Length > 1024; } catch (Exception) { return false; }
    }

    private static string Tail(string text)
    {
        text = text.Trim();
        return text.Length <= 400 ? text : text[^400..];
    }

    private void TryDeleteFrames()
    {
        try { if (Directory.Exists(_framesDir)) Directory.Delete(_framesDir, true); }
        catch (Exception ex) { _log($"[WARN] Session video: không xoá được thư mục khung ({ex.Message})"); }
    }
}
