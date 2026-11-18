using System.Globalization;
using System.Text.RegularExpressions;

namespace PhotoBooth.Core.Storage;

/// <summary>Kết quả một lần dọn.</summary>
public sealed record StorageCleanupResult(int SessionDirectories, int LooseFiles, int FrameDirectories, int LogFiles, long BytesFreed)
{
    public bool NothingDeleted => SessionDirectories + LooseFiles + FrameDirectories + LogFiles == 0;
}

/// <summary>
/// Dọn file cũ trên máy kiosk: thư mục phiên chụp (SESSION_...), ảnh / video trong gallery và thư mục kiểm tra máy ảnh,
/// khung ảnh tạm của video bị bỏ lại, và log đã xoay vòng. Mọi thứ quá <see cref="MaxAge"/> (7 ngày) đều bị xoá.
/// Phiên còn trong hàng đợi gửi do hàng đợi tự xoá (kèm manifest), nên được bỏ qua qua <c>isProtected</c>.
/// </summary>
public sealed class StorageCleaner
{
    /// <summary>Tuổi tối đa của file trên máy.</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromDays(7);

    /// <summary>Khung JPEG tạm của video: bình thường xoá ngay sau khi dựng; còn lại lâu hơn mức này là bị bỏ lại.</summary>
    public static readonly TimeSpan FramesMaxAge = TimeSpan.FromHours(6);

    // SESSION_yyyyMMdd_HHmmss_xxxxxx, giờ UTC
    private static readonly Regex SessionName = new(@"^SESSION_(\d{8})_(\d{6})_", RegexOptions.Compiled);

    private readonly string _sessionsRoot;
    private readonly IReadOnlyList<string> _looseRoots;
    private readonly string _logDirectory;
    private readonly string _logPrefix;
    private readonly Func<DateTimeOffset> _now;
    private readonly Action<string> _log;

    /// <param name="sessionsRoot">Thư mục chứa các SESSION_...</param>
    /// <param name="looseRoots">Thư mục xoá theo tuổi từng file (gallery, kiểm tra máy ảnh); thư mục gốc không bị xoá.</param>
    /// <param name="logDirectory">Thư mục log; xoá log đã xoay vòng ({logPrefix}.N.log) quá cũ.</param>
    public StorageCleaner(
        string sessionsRoot,
        IEnumerable<string> looseRoots,
        string logDirectory,
        string logPrefix = "host_startup",
        Func<DateTimeOffset>? now = null,
        Action<string>? log = null)
    {
        _sessionsRoot = sessionsRoot;
        _looseRoots = looseRoots.Where(r => !string.IsNullOrWhiteSpace(r)).ToList();
        _logDirectory = logDirectory;
        _logPrefix = logPrefix;
        _now = now ?? (() => DateTimeOffset.UtcNow);
        _log = log ?? (_ => { });
    }

    /// <summary>Thời điểm chụp của thư mục phiên: lấy từ tên thư mục (UTC), không đọc được thì từ giờ ghi cuối.</summary>
    public static DateTimeOffset SessionAge(string directory)
    {
        DateTimeOffset lastWrite = new(Directory.GetLastWriteTimeUtc(directory), TimeSpan.Zero);
        var m = SessionName.Match(Path.GetFileName(directory));
        if (m.Success
            && DateTime.TryParseExact(m.Groups[1].Value + m.Groups[2].Value, "yyyyMMddHHmmss", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var fromName))
        {
            var named = new DateTimeOffset(fromName, TimeSpan.Zero);
            // Thư mục được chạm lại sau đó (in lại...) thì tính theo lần mới nhất
            return named > lastWrite ? named : lastWrite;
        }
        return lastWrite;
    }

    /// <param name="isProtected">Đường dẫn thư mục này đang được hàng đợi gửi dùng (true = bỏ qua, hàng đợi tự xoá).</param>
    public StorageCleanupResult Run(Func<string, bool>? isProtected = null)
    {
        isProtected ??= _ => false;
        var now = _now();
        int sessions = 0, files = 0, frames = 0, logs = 0;
        long bytes = 0;

        // 1. Thư mục phiên
        if (Directory.Exists(_sessionsRoot))
        {
            foreach (var dir in SafeEnumerateDirectories(_sessionsRoot))
            {
                if (!SessionName.IsMatch(Path.GetFileName(dir))) continue; // chỉ đụng thư mục đúng tên phiên

                // Khung video bị bỏ lại: dọn cả với phiên còn mới
                string frameDir = Path.Combine(dir, "video_frames");
                if (Directory.Exists(frameDir) && now - new DateTimeOffset(Directory.GetLastWriteTimeUtc(frameDir), TimeSpan.Zero) >= FramesMaxAge)
                {
                    long size = DirectorySize(frameDir);
                    if (TryDeleteDirectory(frameDir)) { frames++; bytes += size; }
                }

                if (isProtected(dir)) continue;
                if (now - SessionAge(dir) < MaxAge) continue;
                long total = DirectorySize(dir);
                if (TryDeleteDirectory(dir)) { sessions++; bytes += total; }
            }
        }

        // 2. Gallery, thư mục kiểm tra máy ảnh: theo tuổi từng file
        foreach (var root in _looseRoots)
        {
            if (!Directory.Exists(root)) continue;
            foreach (var file in SafeEnumerateFiles(root))
            {
                try
                {
                    if (now - new DateTimeOffset(File.GetLastWriteTimeUtc(file), TimeSpan.Zero) < MaxAge) continue;
                    if (IsUnderProtected(file, root, isProtected)) continue;
                    long size = new FileInfo(file).Length;
                    File.Delete(file);
                    files++;
                    bytes += size;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    _log($"[WARN] Dọn file: không xoá được {file}: {ex.Message}");
                }
            }
            RemoveEmptyDirectories(root, isProtected);
        }

        // 3. Log đã xoay vòng
        if (Directory.Exists(_logDirectory))
        {
            foreach (var file in Directory.EnumerateFiles(_logDirectory, _logPrefix + ".*.log"))
            {
                try
                {
                    if (now - new DateTimeOffset(File.GetLastWriteTimeUtc(file), TimeSpan.Zero) < MaxAge) continue;
                    long size = new FileInfo(file).Length;
                    File.Delete(file);
                    logs++;
                    bytes += size;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    _log($"[WARN] Dọn log: không xoá được {file}: {ex.Message}");
                }
            }
        }

        var result = new StorageCleanupResult(sessions, files, frames, logs, bytes);
        if (!result.NothingDeleted)
        {
            _log($"[INFO] Dọn bộ nhớ: xoá {sessions} thư mục phiên, {files} file gallery/kiểm tra, {frames} thư mục khung video, {logs} log cũ; giải phóng {bytes / 1048576.0:0.#} MB");
        }
        return result;
    }

    private static bool IsUnderProtected(string file, string root, Func<string, bool> isProtected)
    {
        string? dir = Path.GetDirectoryName(file);
        string fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        while (!string.IsNullOrEmpty(dir))
        {
            string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dir));
            if (string.Equals(full, fullRoot, StringComparison.OrdinalIgnoreCase)) return false;
            if (isProtected(full)) return true;
            dir = Path.GetDirectoryName(full);
        }
        return false;
    }

    /// <summary>Xoá thư mục con rỗng (từ trong ra ngoài), không xoá thư mục gốc và thư mục đang được hàng đợi dùng.</summary>
    private void RemoveEmptyDirectories(string root, Func<string, bool> isProtected)
    {
        foreach (var dir in SafeEnumerateDirectories(root))
        {
            RemoveEmptyDirectories(dir, isProtected);
            try
            {
                if (isProtected(Path.TrimEndingDirectorySeparator(Path.GetFullPath(dir)))) continue;
                if (!Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _log($"[WARN] Dọn thư mục rỗng: không xoá được {dir}: {ex.Message}");
            }
        }
    }

    private bool TryDeleteDirectory(string dir)
    {
        try
        {
            Directory.Delete(dir, recursive: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log($"[WARN] Dọn bộ nhớ: không xoá được {dir}: {ex.Message}");
            return false;
        }
    }

    private static long DirectorySize(string dir)
    {
        try { return Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return 0; }
    }

    private static IEnumerable<string> SafeEnumerateDirectories(string dir)
    {
        try { return Directory.EnumerateDirectories(dir).ToList(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return Array.Empty<string>(); }
    }

    private static IEnumerable<string> SafeEnumerateFiles(string dir)
    {
        try { return Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).ToList(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return Array.Empty<string>(); }
    }
}
