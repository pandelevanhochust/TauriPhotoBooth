using System;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using SkiaSharp;

namespace PhotoBooth.Imaging.Video;

/// <summary>
/// Ghi video MP4 (H.264, Media Foundation) của phiên chụp từ các khung JPEG kèm mốc thời gian.
/// Mỗi khung được đặt đúng thời điểm thật (khung thiếu thì giữ khung trước), nên video dài đúng bằng
/// thời gian chụp (<c>speed</c> = 1), hoặc ngắn đi <c>speed</c> lần (timelapse tua nhanh).
/// Mã hoá chạy trên luồng riêng; hàng đợi đầy thì bỏ khung.
/// Kích thước video lấy theo khung đầu tiên (tối đa <c>maxWidth</c>); khung khác tỉ lệ được đặt vừa giữa nền đen.
/// </summary>
public sealed class TimelapseRecorder : IDisposable
{
    private const int QueueLimit = 150;
    /// <summary>Khoảng trống dài nhất được lấp bằng khung đứng yên (vd. lúc máy ảnh tạm dừng live view để chụp).</summary>
    private const int MaxGapMs = 5000;

    private readonly string _outputPath;
    private readonly int _fps;
    private readonly int _maxWidth;
    private readonly int _speed;
    private readonly Action<string> _log;
    private readonly BlockingCollection<(byte[] Jpeg, long TimestampMs)> _queue = new(QueueLimit);
    private readonly TaskCompletionSource<string?> _done = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _finishRequested;
    private int _dropped;

    public string OutputPath => _outputPath;
    public long FramesWritten { get; private set; }

    public TimelapseRecorder(string outputPath, int fps = 24, int maxWidth = 1280, Action<string>? log = null, int speed = 1)
    {
        _speed = Math.Max(1, speed);
        _outputPath = outputPath;
        _fps = Math.Max(1, fps);
        _maxWidth = Math.Max(320, maxWidth);
        _log = log ?? (_ => { });

        string? dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        if (File.Exists(outputPath)) File.Delete(outputPath);

        var thread = new Thread(Run) { IsBackground = true, Name = "TimelapseEncoder" };
        thread.Start();
    }

    /// <summary>Thêm một khung JPEG chụp lúc <paramref name="timestampMs"/> (đồng hồ tăng dần, ms).</summary>
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

    /// <summary>Ngừng nhận khung, chờ mã hoá xong. Trả về đường dẫn MP4, hoặc null nếu không ghi được / quá ít khung.</summary>
    public Task<string?> FinishAsync()
    {
        if (Interlocked.Exchange(ref _finishRequested, 1) == 0) _queue.CompleteAdding();
        return _done.Task;
    }

    public void Dispose()
    {
        FinishAsync();
    }

    private void Run()
    {
        bool comInitialized = CoInitializeEx(IntPtr.Zero, COINIT_MULTITHREADED) >= 0;
        MediaFoundationH264Writer? writer = null;
        byte[]? frameBuffer = null;
        long startMs = 0;
        try
        {
            // Khoảng trống dài nhất tính theo thời gian của video (đã chia cho tốc độ tua)
            long maxGapMs = Math.Max(1000 / _fps, MaxGapMs / _speed);
            foreach (var (jpeg, realMs) in _queue.GetConsumingEnumerable())
            {
                // Tua nhanh: thời điểm trong video = thời điểm thật / speed
                long timestampMs = realMs / _speed;
                if (writer != null)
                {
                    // Số khung lẽ ra đã có tới thời điểm này; khung đến dày hơn fps thì bỏ bớt
                    long target = ((timestampMs - startMs) * _fps + 500) / 1000 + 1;
                    long gapLimit = writer.FramesWritten + maxGapMs * _fps / 1000;
                    if (target > gapLimit)
                    {
                        startMs += (target - gapLimit) * 1000 / _fps;
                        target = gapLimit;
                    }
                    if (target <= writer.FramesWritten) continue;
                }

                using var image = SKImage.FromEncodedData(jpeg);
                if (image == null) continue;

                if (writer == null)
                {
                    var (w, h) = EncoderSize(image.Width, image.Height, _maxWidth);
                    writer = new MediaFoundationH264Writer(_outputPath, w, h, _fps);
                    frameBuffer = new byte[w * h * 4];
                    startMs = timestampMs;
                    _log($"[INFO] Session video started {w}x{h}@{_fps}fps x{_speed} -> {_outputPath}");
                }

                // Khung trước giữ đến hết khoảng trống, rồi tới khung mới
                long frames = Math.Max(1, ((timestampMs - startMs) * _fps + 500) / 1000 + 1 - writer.FramesWritten);
                for (long i = 1; i < frames; i++) writer.WriteFrame(frameBuffer!);
                RenderBottomUp(image, writer.Width, writer.Height, frameBuffer!);
                writer.WriteFrame(frameBuffer!);
                FramesWritten = writer.FramesWritten;
            }

            if (writer == null || writer.FramesWritten < 2)
            {
                writer?.Dispose();
                writer = null;
                TryDelete();
                _log("[INFO] Session video skipped: not enough frames");
                _done.TrySetResult(null);
                return;
            }

            writer.FinalizeWriter();
            writer.Dispose();
            writer = null;
            try
            {
                // moov lên đầu file để điện thoại (iPhone) phát ngay khi đang tải
                Mp4FastStart.Process(_outputPath);
            }
            catch (Exception ex)
            {
                _log($"[WARN] Session video faststart skipped: {ex.Message}");
            }
            _log($"[INFO] Session video saved: {_outputPath} ({FramesWritten} frames = {FramesWritten / (double)_fps:0.0}s, {_dropped} dropped)");
            _done.TrySetResult(_outputPath);
        }
        catch (Exception ex)
        {
            _log($"[ERROR] Session video encoding failed: {ex.Message}");
            try { writer?.Dispose(); } catch { }
            TryDelete();
            _done.TrySetResult(null);
        }
        finally
        {
            if (comInitialized) CoUninitialize();
        }
    }

    private static (int W, int H) EncoderSize(int width, int height, int maxWidth)
    {
        int w = Math.Max(2, width), h = Math.Max(2, height);
        if (w > maxWidth)
        {
            h = (int)Math.Round(h * (maxWidth / (double)w));
            w = maxWidth;
        }
        return (Math.Max(2, w - w % 2), Math.Max(2, h - h % 2));
    }

    /// <summary>Vẽ ảnh vừa khung (giữ tỉ lệ, nền đen) rồi chép ra BGRA theo thứ tự hàng từ dưới lên.</summary>
    private static void RenderBottomUp(SKImage image, int width, int height, byte[] destination)
    {
        var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var bitmap = new SKBitmap(info);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Black);
            float scale = Math.Min(width / (float)image.Width, height / (float)image.Height);
            float dw = image.Width * scale, dh = image.Height * scale;
            var dest = SKRect.Create((width - dw) / 2, (height - dh) / 2, dw, dh);
            canvas.DrawImage(image, dest, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        }

        int stride = width * 4;
        IntPtr pixels = bitmap.GetPixels();
        for (int y = 0; y < height; y++)
        {
            Marshal.Copy(pixels + y * bitmap.RowBytes, destination, (height - 1 - y) * stride, stride);
        }
    }

    private void TryDelete()
    {
        try { if (File.Exists(_outputPath)) File.Delete(_outputPath); } catch { }
    }

    private const int COINIT_MULTITHREADED = 0;
    [DllImport("ole32.dll", ExactSpelling = true)] private static extern int CoInitializeEx(IntPtr reserved, int coInit);
    [DllImport("ole32.dll", ExactSpelling = true)] private static extern void CoUninitialize();
}
