using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using PhotoBooth.Core.Data;
using PhotoBooth.Core.Interfaces;
using PhotoBooth.Core.Models;
using PhotoBooth.Infrastructure.Storage;
using PhotoBooth.Hardware.Camera;
using PhotoBooth.Hardware.Camera.Canon;
using PhotoBooth.Hardware.Printer;
using PhotoBooth.Host.Printing;
using PhotoBooth.Host.Services;
using PhotoBooth.Imaging.Compositor;
using PhotoBooth.Imaging.Video;
using PhotoBooth.Hardware.Camera.Fujifilm;

namespace PhotoBooth.Host;

public partial class MainWindow : Window
{
    private KeyboardHook? _keyboardHook;
    private CameraManager? _camera;

    /// <summary>Video đầy đủ của bước chụp: 15 khung/giây, mỗi khung đặt đúng thời điểm thật (không tua nhanh).</summary>
    private const int TimelapseFps = 15;
    private const int TimelapseSampleMs = 1000 / TimelapseFps - 3;
    private TimelapseRecorder? _timelapse;
    /// <summary>Timelapse tua nhanh ghi song song với video đầy đủ, từ cùng các khung hình.</summary>
    private TimelapseRecorder? _timelapseFast;
    private const int TimelapseFastSpeed = 8;
    private const string SessionVideoFile = "timelapse.mp4";
    private const string FastTimelapseFile = "timelapse_fast.mp4";
    private long _lastTimelapseFrameTicks;
    private IPaperCounter _paperCounter = null!;
    private WindowsPrinterService _printerService = null!;
    private IImageComposer _skiaCompositor = null!;
    private MagickCompositor _magickCompositor = null!;
    private DatabaseService _dbService = null!;
    private System.Threading.Timer? _heartbeatTimer;
    private string _heartbeatFile = "";

    private string _currentSessionId = "";
    private string _currentSessionDir = "";
    private string _localGalleryDir = "";
    private string _kioskMode = "FreeEvent";
    private readonly List<string> _capturedRawPhotos = new();
    private string _lastCompositePath = "";

    private static string GetLocalGalleryRootDir()
    {
        try
        {
            if (Directory.Exists(@"D:\"))
            {
                string dPath = @"D:\HihiBooth_Gallery";
                if (!Directory.Exists(dPath)) Directory.CreateDirectory(dPath);
                return dPath;
            }
        }
        catch { }

        string picturesPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "HihiBooth_Gallery");
        if (!Directory.Exists(picturesPath)) Directory.CreateDirectory(picturesPath);
        return picturesPath;
    }

    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            App.Log("[INFO] MainWindow_Loaded start");
            // 1. Install Low-Level Keyboard Hook for Kiosk Security
            App.Log("[INFO] Step 1: Keyboard Hook");
            _keyboardHook = new KeyboardHook(allowEscapeToExit: true);
            _keyboardHook.EscapePressed += () =>
            {
                Dispatcher.Invoke(() =>
                {
                    if (WindowState == WindowState.Maximized)
                    {
                        WindowState = WindowState.Normal;
                        Topmost = false;
                        WindowStyle = WindowStyle.SingleBorderWindow;
                    }
                    else
                    {
                        WindowState = WindowState.Maximized;
                        Topmost = true;
                        WindowStyle = WindowStyle.None;
                    }
                });
            };
            _keyboardHook.F2Pressed += () =>
            {
                Dispatcher.Invoke(() =>
                {
                    App.Log("[INFO] Global F2 pressed: opening admin modal");
                    SendEventToWeb("open_admin_modal", new { });
                });
            };

            // 2. Initialize Core Services
            App.Log("[INFO] Step 2: Core Services Init");
            string baseDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "HihiBooth", "Data");
            if (!Directory.Exists(baseDataDir)) Directory.CreateDirectory(baseDataDir);

            _paperCounter = new AtomicPaperCounter(Path.Combine(baseDataDir, "paper_counter.json"));
            await _paperCounter.InitializeAsync();
            App.Log("[INFO] Step 2.1: Paper counter initialized");

            _printerService = new WindowsPrinterService(_paperCounter, Path.Combine(baseDataDir, "printer.json"), App.Log);
            var printerOverview = await _printerService.GetOverviewAsync();
            App.Log($"[INFO] Step 2.1b: Printer: {printerOverview.Message.Replace('\n', ' ')}");
            _skiaCompositor = new NativeSkiaCompositor();
            _magickCompositor = new MagickCompositor();
            _dbService = new DatabaseService(Path.Combine(baseDataDir, "store.db"));
            await _dbService.InitializeAsync();
            App.Log("[INFO] Step 2.2: DB initialized");

            // Hàng đợi gửi phiên chụp lên backend (đọc lại các phiên chưa gửi xong từ lần chạy trước)
            InitializeCloudUpload(baseDataDir);

            // Prefetch CMS frames & presets in background so they are ready instantly for UI
            _ = Task.Run(async () =>
            {
                try
                {
                    await GetOrFetchFramesJsonAsync();
                    await GetOrFetchPresetsJsonAsync();
                    App.Log("[INFO] CMS Frames and Presets prefetch completed.");
                }
                catch (Exception ex)
                {
                    App.Log($"[WARN] CMS prefetch background error: {ex.Message}");
                }
            });

            // Start Watchdog Heartbeat
            _heartbeatFile = Path.Combine(baseDataDir, "heartbeat.txt");
            _heartbeatTimer = new System.Threading.Timer(_ =>
            {
                try
                {
                    using var stream = new FileStream(_heartbeatFile, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
                    using var writer = new StreamWriter(stream);
                    writer.Write(DateTime.UtcNow.ToString("o"));
                }
                catch { }
            }, null, TimeSpan.Zero, TimeSpan.FromSeconds(2));

            // 3. Initialize Camera (ready on demand)
            App.Log("[INFO] Step 3: Camera Init");
            // Canon EOS qua EDSDK; không có máy thì driver tự dò lại mỗi 2 giây, UI dùng webcam thay thế
            // Canon (EDSDK) ưu tiên trước, rồi Fujifilm (X SDK); máy nào đang cắm thì dùng máy đó
            _camera = new CameraManager(App.Log, new CanonEdsdkCamera(App.Log), new FujifilmXsdkCamera(App.Log));
            _camera.FrameArrived += OnCameraFrameArrived;
            _camera.InfoChanged += (_, info) => PostEventToWeb("camera_status_changed", ToCameraStatusDto(info));
            var camInit = await _camera.InitializeAsync();
            App.Log($"[INFO] Step 3.1: Camera init: {(camInit.IsSuccess ? "connected" : camInit.UserMessage)}");

            // 4. Initialize WebView2
            App.Log("[INFO] Step 4: WebView2 Init");
            await InitializeWebView2Async();
            App.Log("[INFO] Step 4.1: WebView2 initialized successfully");
        }
        catch (Exception ex)
        {
            App.Log($"[ERROR] MainWindow_Loaded exception: {ex}");
            MessageBox.Show($"Lỗi khởi tạo Kiosk: {ex.Message}", "HihiBooth Kiosk Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task InitializeWebView2Async()
    {
        App.Log("[INFO] InitializeWebView2Async: Creating Environment");
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string webViewDataFolder = Path.Combine(localAppData, "HihiBooth", "WebView2Data");
        var options = new CoreWebView2EnvironmentOptions("--allow-running-insecure-content --disable-web-security --use-fake-ui-for-media-stream");
        var env = await CoreWebView2Environment.CreateAsync(null, webViewDataFolder, options);
        App.Log("[INFO] InitializeWebView2Async: Ensuring CoreWebView2");
        await MainWebView.EnsureCoreWebView2Async(env);
        App.Log("[INFO] InitializeWebView2Async: CoreWebView2 ready");

        // Auto-grant camera and microphone permissions for WebRTC getUserMedia
        MainWebView.CoreWebView2.PermissionRequested += (sender, args) =>
        {
            if (args.PermissionKind == CoreWebView2PermissionKind.Camera ||
                args.PermissionKind == CoreWebView2PermissionKind.Microphone)
            {
                args.State = CoreWebView2PermissionState.Allow;
            }
        };

        // Security & Kiosk configurations
        MainWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
        MainWebView.CoreWebView2.Settings.AreDevToolsEnabled = true; // Enabled for diagnostics
        MainWebView.CoreWebView2.Settings.IsZoomControlEnabled = false;

        MainWebView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
        MainWebView.CoreWebView2.NavigationCompleted += (s, args) =>
        {
            App.Log($"[INFO] NavigationCompleted. Success={args.IsSuccess}");
            Dispatcher.Invoke(() =>
            {
                Activate();
                Focus();
                MainWebView.Focus();
            });
        };

        // Check if Vite dev server is running with non-blocking socket probe
        string devUrl = "http://localhost:5173";
        bool isDevRunning = false;
#if DEBUG
        // Chỉ bản Debug mới dùng Vite dev server; bản phát hành luôn chạy ui/dist đóng gói kèm
        try
        {
            using var tcp = new System.Net.Sockets.TcpClient();
            var connectTask = tcp.ConnectAsync("127.0.0.1", 5173);
            if (await Task.WhenAny(connectTask, Task.Delay(150)) == connectTask && tcp.Connected)
            {
                isDevRunning = true;
            }
        }
        catch { }
#endif

        // Video phiên vừa chụp cho màn hình cuối: https://media.hihibooth.local/{SESSION_...}/timelapse.mp4
        try
        {
            string sessionsRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "HihiBooth", "Data", "Sessions");
            Directory.CreateDirectory(sessionsRoot);
            MainWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(SessionMediaHost, sessionsRoot, CoreWebView2HostResourceAccessKind.Allow);
        }
        catch (Exception ex)
        {
            App.Log($"[WARN] Map session media folder: {ex.Message}");
        }

        if (isDevRunning)
        {
            App.Log($"[INFO] Navigating to DevUrl: {devUrl}");
            MainWebView.CoreWebView2.Navigate(devUrl);
        }
        else
        {
            string? distFolder = FindDistFolder();
            App.Log($"[INFO] Found distFolder: '{distFolder}'");
            if (!string.IsNullOrEmpty(distFolder) && Directory.Exists(distFolder))
            {
                MainWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "app.hihibooth.local",
                    distFolder,
                    CoreWebView2HostResourceAccessKind.Allow);
                App.Log("[INFO] SetVirtualHostNameToFolderMapping done. Navigating to https://app.hihibooth.local/index.html");
                MainWebView.CoreWebView2.Navigate("https://app.hihibooth.local/index.html");
            }
            else
            {
                App.Log("[WARN] distFolder not found. Navigating to fallback string");
                MainWebView.CoreWebView2.NavigateToString("<html><body style='background:#111;color:#fff;display:flex;align-items:center;justify-content:center;height:100vh;font-family:sans-serif;'><h1>HihiBooth Kiosk Ready. UI dist pending.</h1></body></html>");
            }
        }
    }

    private static string? FindDistFolder()
    {
        // 1. Direct local path (production deployment)
        string local = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ui", "dist");
        if (Directory.Exists(local) && File.Exists(Path.Combine(local, "index.html"))) return local;

        // 2. Search upwards up to 8 levels (development builds)
        var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        for (int i = 0; i < 8 && dir != null; i++)
        {
            string candidate = Path.Combine(dir.FullName, "ui", "dist");
            if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "index.html")))
            {
                return candidate;
            }
            dir = dir.Parent;
        }

        // 3. Workspace fallback
        string abs = @"D:\WORK\TauriPhotoBooth\ui\dist";
        if (Directory.Exists(abs) && File.Exists(Path.Combine(abs, "index.html"))) return abs;

        return null;
    }

    private long _lastFrameSentTicks;
    private int _framePostPending;

    /// <summary>Live view từ máy ảnh (luồng EDSDK): gửi sang UI tối đa ~30 khung/giây, bỏ khung khi UI chưa nhận kịp.</summary>
    private void OnCameraFrameArrived(object? sender, FrameLeaseEventArgs e)
    {
        long now = Environment.TickCount64;
        AddTimelapseFrame(e.JpegData.Span, now);
        if (now - _lastFrameSentTicks < 30) return;
        if (Interlocked.CompareExchange(ref _framePostPending, 1, 0) != 0) return;
        _lastFrameSentTicks = now;

        string base64 = Convert.ToBase64String(e.JpegData.Span);
        Dispatcher.BeginInvoke(() =>
        {
            try
            {
                if (MainWebView?.CoreWebView2 != null)
                {
                    MainWebView.CoreWebView2.PostWebMessageAsJson(
                        JsonSerializer.Serialize(new { @event = "camera_frame", data = new { base64 } }));
                }
            }
            catch { }
            finally
            {
                Interlocked.Exchange(ref _framePostPending, 0);
            }
        });
    }

    /// <summary>Lấy mẫu khung cho timelapse (tối đa 1 khung / TimelapseSampleMs).</summary>
    private void AddTimelapseFrame(ReadOnlySpan<byte> jpeg, long nowTicks)
    {
        var recorder = Volatile.Read(ref _timelapse);
        if (recorder == null || jpeg.IsEmpty) return;
        if (nowTicks - Interlocked.Read(ref _lastTimelapseFrameTicks) < TimelapseSampleMs) return;
        Interlocked.Exchange(ref _lastTimelapseFrameTicks, nowTicks);
        byte[] frame = jpeg.ToArray();
        recorder.AddFrame(frame, nowTicks);
        Volatile.Read(ref _timelapseFast)?.AddFrame(frame, nowTicks);
    }

    /// <summary>
    /// Dừng ghi video phiên (đầy đủ + timelapse tua nhanh); mã hoá xong thì gắn vào hàng đợi gửi
    /// và chép vào thư mục gallery của phiên (chạy nền).
    /// </summary>
    private void StopTimelapse()
    {
        var full = Interlocked.Exchange(ref _timelapse, null);
        var fast = Interlocked.Exchange(ref _timelapseFast, null);
        if (full == null && fast == null) return;
        string galleryDir = _localGalleryDir;
        Guid cloudSessionId = _cloudSessionId;
        string sessionDirName = Path.GetFileName(_currentSessionDir);
        Volatile.Write(ref _videoEncodingSession, sessionDirName);
        _ = Task.Run(async () =>
        {
            string? video = null, fastVideo = null;
            try
            {
                var fullTask = full?.FinishAsync() ?? Task.FromResult<string?>(null);
                var fastTask = fast?.FinishAsync() ?? Task.FromResult<string?>(null);
                video = await fullTask;
                fastVideo = await fastTask;
                // Gắn video vào hàng đợi trước khi bỏ cờ, để màn hình cuối không thấy phiên "xong" mà thiếu video
                if (video != null) AttachTimelapseToCloud(cloudSessionId, video, 0);
                if (fastVideo != null) AttachTimelapseToCloud(cloudSessionId, fastVideo, 1);
            }
            catch (Exception ex)
            {
                App.Log($"[WARN] Finish session videos: {ex.Message}");
            }
            finally
            {
                Interlocked.CompareExchange(ref _videoEncodingSession, null, sessionDirName);
            }
            foreach (var path in new[] { video, fastVideo })
            {
                if (path == null) continue;
                try
                {
                    if (!string.IsNullOrEmpty(galleryDir) && Directory.Exists(galleryDir))
                    {
                        File.Copy(path, Path.Combine(galleryDir, Path.GetFileName(path)), true);
                    }
                }
                catch (Exception ex)
                {
                    App.Log($"[WARN] Copy {Path.GetFileName(path)} to gallery: {ex.Message}");
                }
            }
            if (video != null || fastVideo != null) PostEventToWeb("timelapse_ready", new { path = video ?? fastVideo });
        });
    }

    private static object ToCameraStatusDto(CameraInfo info) => new
    {
        vendor = info.Vendor,
        sdkAvailable = info.SdkAvailable,
        connected = info.Connected,
        liveView = info.LiveView,
        model = info.Model,
        port = info.Port,
        status = info.Status,
        message = info.Message,
        batteryLevel = info.BatteryLevel,
    };

    private object CurrentCameraStatusDto() => _camera != null
        ? ToCameraStatusDto(_camera.GetInfo())
        : new { sdkAvailable = false, connected = false, liveView = false, model = "", port = "", status = "error", message = "Chưa khởi tạo máy ảnh", batteryLevel = (int?)null };

    private async Task<object> BuildPrinterStatusDtoAsync()
    {
        var overview = await _printerService.GetOverviewAsync();
        var paperSizes = overview.Selected != null
            ? await Task.Run(() => WindowsPrinterService.ListPaperSizes(overview.Selected.Name))
            : Array.Empty<string>();
        return new
        {
            ready = overview.Ready,
            message = overview.Message,
            selected = overview.Selected?.Name ?? "",
            configured = _printerService.ConfiguredPrinter,
            usbWithoutDriver = overview.UsbWithoutDriver,
            paperCount = _paperCounter.RemainingSheets,
            paperSizes,
            printers = overview.Printers.Select(q => new
            {
                name = q.Name,
                driver = q.Driver,
                port = q.Port,
                isDefault = q.IsDefault,
                isVirtual = q.IsVirtual,
                isPhotoPrinter = q.IsPhotoPrinter,
                status = q.Status.ToString(),
                statusText = q.StatusText,
                jobCount = q.JobCount,
            }),
        };
    }

    /// <summary>Trang in thử 4x6 (1200x1800): viền sát mép, dải màu, thang xám để kiểm tra căn lề và màu.</summary>
    private static void WriteTestPage(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        const int W = 1200, H = 1800;
        using var surface = SkiaSharp.SKSurface.Create(new SkiaSharp.SKImageInfo(W, H));
        var canvas = surface.Canvas;
        canvas.Clear(SkiaSharp.SKColors.White);

        using var paint = new SkiaSharp.SKPaint { IsAntialias = true };
        var bars = new[] { "#ff0000", "#00a651", "#0054a6", "#00aeef", "#ec008c", "#fff200", "#000000" };
        float barW = (W - 200f) / bars.Length;
        for (int i = 0; i < bars.Length; i++)
        {
            paint.Color = SkiaSharp.SKColor.Parse(bars[i]);
            canvas.DrawRect(100 + i * barW, 420, barW, 360, paint);
        }
        for (int i = 0; i < 11; i++)
        {
            byte v = (byte)(255 - i * 25.5);
            paint.Color = new SkiaSharp.SKColor(v, v, v);
            canvas.DrawRect(100 + i * (W - 200f) / 11, 840, (W - 200f) / 11, 200, paint);
        }

        paint.Style = SkiaSharp.SKPaintStyle.Stroke;
        paint.StrokeWidth = 6;
        paint.Color = SkiaSharp.SKColor.Parse("#7a0000");
        canvas.DrawRect(3, 3, W - 6, H - 6, paint);      // viền sát mép: bị cắt đều 4 cạnh là căn đúng
        canvas.DrawRect(60, 60, W - 120, H - 120, paint);
        canvas.DrawLine(W / 2f, 60, W / 2f, H - 60, paint); // đường giữa = vị trí cắt đôi dải 2x6

        paint.Style = SkiaSharp.SKPaintStyle.Fill;
        using var titleFont = new SkiaSharp.SKFont(SkiaSharp.SKTypeface.FromFamilyName("Segoe UI", SkiaSharp.SKFontStyle.Bold), 84);
        using var textFont = new SkiaSharp.SKFont(SkiaSharp.SKTypeface.FromFamilyName("Segoe UI"), 44);
        canvas.DrawText("HihiBooth", W / 2f, 220, SkiaSharp.SKTextAlign.Center, titleFont, paint);
        canvas.DrawText("Trang in thu 4x6", W / 2f, 310, SkiaSharp.SKTextAlign.Center, textFont, paint);
        paint.Color = SkiaSharp.SKColors.Black;
        canvas.DrawText(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), W / 2f, 1180, SkiaSharp.SKTextAlign.Center, textFont, paint);
        canvas.DrawText("Vien do sat mep phai deu 4 canh", W / 2f, 1260, SkiaSharp.SKTextAlign.Center, textFont, paint);

        using var image = surface.Snapshot();
        using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Jpeg, 95);
        File.WriteAllBytes(path, data.ToArray());
    }

    /// <summary>
    /// Chờ ảnh của một tấm đã nhả màn trập tải về xong, lưu thành shot_{n}.jpg, tạo bản xem trước
    /// rồi báo UI bằng sự kiện shot_ready (hoặc shot_failed). Chạy nền, không chặn tấm kế tiếp.
    /// </summary>
    private async Task FinishDslrShotAsync(int shotIndex, string sessionDir, string galleryDir, Task<CapturePhotoResult> download)
    {
        try
        {
            var result = await download;
            if (!result.IsSuccess || string.IsNullOrEmpty(result.RawImagePath))
            {
                string error = string.IsNullOrWhiteSpace(result.UserMessage) ? "Không tải được ảnh từ máy ảnh" : result.UserMessage;
                App.Log($"[WARN] DSLR shot #{shotIndex} download failed: {error}");
                PostEventToWeb("shot_failed", new { shotIndex, message = error });
                return;
            }

            string target = Path.Combine(sessionDir, $"shot_{shotIndex}.jpg");
            byte[] preview = await Task.Run(() =>
            {
                File.Move(result.RawImagePath, target, overwrite: true);
                if (!string.IsNullOrEmpty(galleryDir) && Directory.Exists(galleryDir))
                {
                    try { File.Copy(target, Path.Combine(galleryDir, $"shot_{shotIndex}.jpg"), true); }
                    catch (Exception gEx) { App.Log($"[WARN] Copy DSLR shot to gallery: {gEx.Message}"); }
                }
                return PhotoBooth.Imaging.ImagePreviewHelper.CreateJpegPreview(target, 1024);
            });

            if (sessionDir == _currentSessionDir && !_capturedRawPhotos.Contains(target)) _capturedRawPhotos.Add(target);
            App.Log($"[INFO] Captured shot #{shotIndex} from DSLR: {target}");
            PostEventToWeb("shot_ready", new { shotIndex, filePath = target, base64 = Convert.ToBase64String(preview) });
        }
        catch (Exception ex)
        {
            App.Log($"[ERROR] DSLR shot #{shotIndex}: {ex}");
            PostEventToWeb("shot_failed", new { shotIndex, message = "Không lưu được ảnh vừa chụp" });
        }
    }

    /// <summary>Chụp bằng máy ảnh rồi đổi tên thành <paramref name="fileName"/> trong <paramref name="dir"/>.</summary>
    private async Task<(string? Path, string Error)> CaptureDslrAsync(string dir, string fileName)
    {
        if (_camera == null) return (null, "Chưa khởi tạo máy ảnh");
        var result = await _camera.CapturePhotoAsync(dir);
        if (!result.IsSuccess || string.IsNullOrEmpty(result.RawImagePath))
        {
            return (null, string.IsNullOrWhiteSpace(result.UserMessage) ? "Chụp ảnh thất bại" : result.UserMessage);
        }
        string target = Path.Combine(dir, fileName);
        File.Move(result.RawImagePath, target, overwrite: true);
        return (target, "");
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            string json = e.WebMessageAsJson;
            // Không ghi nguyên payload (ảnh base64, khung timelapse...) vào log
            var head = json.AsSpan(0, Math.Min(json.Length, 160));
            if (head.Contains("\"cloud_configure\"", StringComparison.Ordinal))
            {
                // Có khoá API: không bao giờ ghi payload vào log
                App.Log("[IPC IN] cloud_configure (payload ẩn)");
            }
            else if (!head.Contains("\"timelapse_frame\"", StringComparison.Ordinal))
            {
                App.Log($"[IPC IN] {(json.Length > 400 ? json[..400] + $"... ({json.Length} chars)" : json)}");
            }
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.String)
            {
                string inner = root.GetString() ?? "";
                using var innerDoc = JsonDocument.Parse(inner);
                var innerRoot = innerDoc.RootElement.Clone();
                string id = innerRoot.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? "" : "";
                string command = innerRoot.TryGetProperty("command", out var cmdProp) ? cmdProp.GetString() ?? "" : "";
                JsonElement payload = innerRoot.TryGetProperty("payload", out var p) ? p.Clone() : default;
                _ = HandleCommandAsync(id, command, payload);
                return;
            }

            if (root.ValueKind == JsonValueKind.Object)
            {
                string id = root.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? "" : "";
                string command = root.TryGetProperty("command", out var cmdProp) ? cmdProp.GetString() ?? "" : "";
                JsonElement payload = root.TryGetProperty("payload", out var p) ? p.Clone() : default;
                _ = HandleCommandAsync(id, command, payload);
            }
        }
        catch (Exception ex)
        {
            App.Log($"[ERROR] OnWebMessageReceived: {ex}");
        }
    }

    private async Task HandleCommandAsync(string id, string command, JsonElement payload)
    {
        try
        {
            switch (command)
            {
                case "get_kiosk_status":
                    SendSuccessResponse(id, new
                    {
                        mode = _kioskMode,
                        paperCount = _paperCounter.RemainingSheets,
                        isPrinterReady = (await _printerService.GetOverviewAsync()).Ready,
                        isCameraReady = true,
                        cameraModel = "Mock High-Res Portrait Camera",
                        version = "1.0.0"
                    });
                    break;

                case "get_frames":
                    try
                    {
                        string framesJson = await GetOrFetchFramesJsonAsync();
                        SendRawSuccessResponse(id, framesJson);
                    }
                    catch (Exception ex)
                    {
                        App.Log($"[ERROR] get_frames IPC error: {ex.Message}");
                        SendErrorResponse(id, ex.Message);
                    }
                    break;

                case "get_presets":
                    try
                    {
                        string presetsJson = await GetOrFetchPresetsJsonAsync();
                        SendRawSuccessResponse(id, presetsJson);
                    }
                    catch (Exception ex)
                    {
                        App.Log($"[ERROR] get_presets IPC error: {ex.Message}");
                        SendErrorResponse(id, ex.Message);
                    }
                    break;

                case "get_frame_overlay":
                    try
                    {
                        // Frame đã khoét các ô tô màu, để bước chọn ảnh xem trước giống hệt bản in
                        string overlayUrl = payload.TryGetProperty("frameUrl", out var ouEl) ? ouEl.GetString() ?? "" : "";
                        string? localFrame = await EnsureLocalFrameAsync(overlayUrl);
                        if (localFrame == null)
                        {
                            SendErrorResponse(id, "Frame not available");
                            break;
                        }
                        var overlayHoles = ParseHoles(payload);
                        string openPath = await Task.Run(() => FrameOverlayHelper.EnsureOpenOverlayFile(localFrame, overlayHoles));
                        byte[] overlayBytes = await File.ReadAllBytesAsync(openPath);
                        SendSuccessResponse(id, new { dataUrl = "data:image/png;base64," + Convert.ToBase64String(overlayBytes) });
                    }
                    catch (Exception ex)
                    {
                        App.Log($"[WARN] get_frame_overlay failed: {ex.Message}");
                        SendErrorResponse(id, ex.Message);
                    }
                    break;

                case "get_layouts":
                    try
                    {
                        string layoutsJson = await GetOrFetchLayoutsJsonAsync();
                        SendRawSuccessResponse(id, layoutsJson);
                    }
                    catch (Exception ex)
                    {
                        App.Log($"[ERROR] get_layouts IPC error: {ex.Message}");
                        SendErrorResponse(id, ex.Message);
                    }
                    break;

                case "get_cameras":
                    SendSuccessResponse(id, new[]
                    {
                        new { id = "hd_webcam", name = "HD Webcam", isDefault = true }
                    });
                    break;

                case "capture_step":
                    // UI báo khách vào / rời bước chụp: hàng đợi gửi ảnh tạm dừng trong lúc chụp
                    _captureStepActive = payload.ValueKind == JsonValueKind.Object
                        && payload.TryGetProperty("active", out var csEl) && csEl.ValueKind == JsonValueKind.True;
                    Interlocked.Exchange(ref _captureStepSince, Environment.TickCount64);
                    if (!_captureStepActive) _uploadQueue?.Signal();
                    SendSuccessResponse(id, null);
                    break;

                case "cloud_status":
                    SendSuccessResponse(id, BuildCloudStatusDto());
                    break;

                case "cloud_configure":
                    SendSuccessResponse(id, ConfigureCloud(payload));
                    break;

                case "session_progress":
                    SendSuccessResponse(id, BuildSessionProgressDto());
                    break;

                case "cloud_retry":
                    _uploadQueue?.RetryNow();
                    SendSuccessResponse(id, BuildCloudStatusDto());
                    break;

                case "timelapse_start":
                    StopTimelapse();
                    if (string.IsNullOrEmpty(_currentSessionDir))
                    {
                        SendSuccessResponse(id, new { recording = false });
                        break;
                    }
                    try
                    {
                        Volatile.Write(ref _timelapse, new TimelapseRecorder(Path.Combine(_currentSessionDir, SessionVideoFile), TimelapseFps, 1280, App.Log));
                        try
                        {
                            Volatile.Write(ref _timelapseFast, new TimelapseRecorder(Path.Combine(_currentSessionDir, FastTimelapseFile), TimelapseFps, 1280, App.Log, TimelapseFastSpeed));
                        }
                        catch (Exception fastEx)
                        {
                            // Thiếu timelapse tua nhanh không chặn video đầy đủ
                            App.Log($"[WARN] timelapse_start (tua nhanh): {fastEx.Message}");
                        }
                        Interlocked.Exchange(ref _lastTimelapseFrameTicks, 0);
                        SendSuccessResponse(id, new { recording = true });
                    }
                    catch (Exception ex)
                    {
                        App.Log($"[WARN] timelapse_start: {ex.Message}");
                        SendSuccessResponse(id, new { recording = false });
                    }
                    break;

                case "timelapse_frame":
                    // Khung từ webcam của trình duyệt (khi không có máy ảnh SDK)
                    if (payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("base64", out var tlEl) && tlEl.ValueKind == JsonValueKind.String)
                    {
                        string tl = tlEl.GetString() ?? "";
                        int comma = tl.IndexOf(',');
                        if (comma >= 0) tl = tl[(comma + 1)..];
                        try { AddTimelapseFrame(Convert.FromBase64String(tl), Environment.TickCount64); } catch (FormatException) { }
                    }
                    SendSuccessResponse(id, null);
                    break;

                case "timelapse_stop":
                    StopTimelapse();
                    SendSuccessResponse(id, null);
                    break;

                case "camera_status":
                    SendSuccessResponse(id, CurrentCameraStatusDto());
                    break;

                case "camera_reconnect":
                    if (_camera != null) await _camera.ReconnectAsync();
                    SendSuccessResponse(id, CurrentCameraStatusDto());
                    break;

                case "camera_start_liveview":
                    if (_camera != null)
                    {
                        var lvStart = await _camera.StartLiveViewAsync();
                        if (!lvStart.IsSuccess) App.Log($"[WARN] camera_start_liveview: {lvStart.UserMessage}");
                    }
                    SendSuccessResponse(id, CurrentCameraStatusDto());
                    break;

                case "camera_stop_liveview":
                    if (_camera != null) await _camera.StopLiveViewAsync();
                    SendSuccessResponse(id, CurrentCameraStatusDto());
                    break;

                case "camera_prefocus":
                    if (_camera != null && _camera.IsConnected) _ = _camera.PrefocusAsync();
                    SendSuccessResponse(id, new { success = true });
                    break;

                case "camera_test_shot":
                    try
                    {
                        string testDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "HihiBooth", "Data", "CameraTest");
                        Directory.CreateDirectory(testDir);
                        var (testPath, testError) = await CaptureDslrAsync(testDir, $"test_{DateTime.Now:yyyyMMdd_HHmmss}.jpg");
                        if (testPath == null)
                        {
                            SendErrorResponse(id, testError);
                            break;
                        }
                        byte[] testPreview = await Task.Run(() => PhotoBooth.Imaging.ImagePreviewHelper.CreateJpegPreview(testPath, 1200));
                        string testSize = "";
                        using (var testCodec = SkiaSharp.SKCodec.Create(testPath))
                        {
                            if (testCodec != null) testSize = $"{testCodec.Info.Width}x{testCodec.Info.Height}";
                        }
                        SendSuccessResponse(id, new
                        {
                            filePath = testPath,
                            resolution = testSize,
                            fileSize = new FileInfo(testPath).Length,
                            previewBase64 = Convert.ToBase64String(testPreview),
                        });
                    }
                    catch (Exception ex)
                    {
                        App.Log($"[ERROR] camera_test_shot: {ex.Message}");
                        SendErrorResponse(id, ex.Message);
                    }
                    break;

                case "verify_admin_pin":
                    string enteredPin = payload.TryGetProperty("pin", out var pinEl) ? pinEl.GetString() ?? "" : "";
                    bool isValid = (enteredPin == "8888" || enteredPin == "1234");
                    SendSuccessResponse(id, isValid);
                    break;

                case "open_gallery_folder":
                    try
                    {
                        string targetFolder = !string.IsNullOrEmpty(_localGalleryDir) && Directory.Exists(_localGalleryDir)
                            ? _localGalleryDir
                            : GetLocalGalleryRootDir();

                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = "explorer.exe",
                            Arguments = $"\"{targetFolder}\"",
                            UseShellExecute = true
                        });
                        SendSuccessResponse(id, new { folder = targetFolder });
                    }
                    catch (Exception ex)
                    {
                        App.Log($"[ERROR] open_gallery_folder: {ex.Message}");
                        SendErrorResponse(id, ex.Message);
                    }
                    break;

                case "start_capture_session":
                    _currentSessionId = $"SESSION_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid().ToString("N")[..6]}";
                    string commonData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "HihiBooth", "Data", "Sessions", _currentSessionId);
                    _currentSessionDir = commonData;
                    if (!Directory.Exists(_currentSessionDir)) Directory.CreateDirectory(_currentSessionDir);

                    // Setup local gallery folder: <RootDir>/YYYY-MM-DD/<SessionId>
                    string dateFolder = DateTime.Now.ToString("yyyy-MM-dd");
                    _localGalleryDir = Path.Combine(GetLocalGalleryRootDir(), dateFolder, _currentSessionId);
                    if (!Directory.Exists(_localGalleryDir)) Directory.CreateDirectory(_localGalleryDir);

                    _capturedRawPhotos.Clear();
                    StopTimelapse();
                    BeginCloudSession();
                    App.Log($"[INFO] Session started: {_currentSessionId} (cloud {_cloudSessionId}), local gallery: {_localGalleryDir}");

                    SendSuccessResponse(id, new { sessionId = _currentSessionId, galleryDir = _localGalleryDir });
                    break;

                case "capture_shot":
                    int shotIndex = payload.TryGetProperty("shotIndex", out var shotEl) ? shotEl.GetInt32() : _capturedRawPhotos.Count + 1;
                    if (string.IsNullOrEmpty(_currentSessionDir))
                    {
                        _currentSessionDir = Path.Combine(Path.GetTempPath(), "HihiBooth_Temp");
                        if (!Directory.Exists(_currentSessionDir)) Directory.CreateDirectory(_currentSessionDir);
                    }

                    // Máy ảnh Canon đang kết nối: chụp bằng máy ảnh, ảnh gốc giữ ở session, UI nhận bản xem trước
                    if (_camera != null && _camera.IsConnected)
                    {
                        var ticket = await _camera.BeginShotAsync(_currentSessionDir);
                        if (!ticket.Shutter.IsSuccess || ticket.Completion == null)
                        {
                            string shutterError = string.IsNullOrWhiteSpace(ticket.Shutter.UserMessage) ? "Chụp ảnh thất bại" : ticket.Shutter.UserMessage;
                            App.Log($"[WARN] DSLR capture #{shotIndex} failed: {shutterError}");
                            SendErrorResponse(id, shutterError);
                            break;
                        }

                        // Màn trập đã nhả: báo UI ngay để khách chụp tiếp; ảnh tải về và báo qua sự kiện shot_ready / shot_failed
                        SendSuccessResponse(id, new { pending = true, shotIndex, source = "dslr" });
                        _ = FinishDslrShotAsync(shotIndex, _currentSessionDir, _localGalleryDir, ticket.Completion);
                        break;
                    }

                    // Check if frontend provided real webcam capture Base64
                    string rawB64 = "";
                    if (payload.TryGetProperty("base64", out var b64Prop))
                    {
                        rawB64 = b64Prop.GetString() ?? "";
                        if (rawB64.Contains(","))
                        {
                            rawB64 = rawB64.Substring(rawB64.IndexOf(",") + 1);
                        }
                    }

                    string realShotPath = Path.Combine(_currentSessionDir, $"shot_{shotIndex}.jpg");
                    if (!string.IsNullOrWhiteSpace(rawB64))
                    {
                        try
                        {
                            byte[] photoBytes = Convert.FromBase64String(rawB64);
                            await File.WriteAllBytesAsync(realShotPath, photoBytes);
                            if (!_capturedRawPhotos.Contains(realShotPath))
                            {
                                _capturedRawPhotos.Add(realShotPath);
                            }

                            // Also save copy to permanent local gallery
                            if (!string.IsNullOrEmpty(_localGalleryDir) && Directory.Exists(_localGalleryDir))
                            {
                                string galleryShotPath = Path.Combine(_localGalleryDir, $"shot_{shotIndex}.jpg");
                                await File.WriteAllBytesAsync(galleryShotPath, photoBytes);
                            }

                            App.Log($"[INFO] Captured shot #{shotIndex} from Webcam saved to {realShotPath} ({photoBytes.Length} bytes)");
                            SendSuccessResponse(id, new { filePath = realShotPath, base64 = rawB64 });
                            return;
                        }
                        catch (Exception ex)
                        {
                            App.Log($"[WARN] Failed to write webcam base64 shot: {ex.Message}");
                        }
                    }

                    // Fallback mock shot
                    string fallbackShot = Path.Combine(_currentSessionDir, $"shot_{shotIndex}.jpg");
                    if (!File.Exists(fallbackShot))
                    {
                        using var surface = SkiaSharp.SKSurface.Create(new SkiaSharp.SKImageInfo(1200, 800));
                        var c = surface.Canvas;
                        c.Clear(SkiaSharp.SKColors.Ivory);
                        using var paint = new SkiaSharp.SKPaint { Color = SkiaSharp.SKColors.DarkRed, IsAntialias = true };
                        using var font = new SkiaSharp.SKFont(SkiaSharp.SKTypeface.Default, 44);
                        c.DrawText($"HihiBooth Photo #{shotIndex}", 250, 420, SkiaSharp.SKTextAlign.Left, font, paint);
                        using var img = surface.Snapshot();
                        using var data = img.Encode(SkiaSharp.SKEncodedImageFormat.Jpeg, 90);
                        using var fs = File.OpenWrite(fallbackShot);
                        data.SaveTo(fs);
                    }
                    if (!_capturedRawPhotos.Contains(fallbackShot))
                    {
                        _capturedRawPhotos.Add(fallbackShot);
                    }
                    byte[] mockBytes = await File.ReadAllBytesAsync(fallbackShot);
                    string mockB64 = Convert.ToBase64String(mockBytes);
                    SendSuccessResponse(id, new { filePath = fallbackShot, base64 = mockB64 });
                    break;

                case "compose_strip":
                    string layoutType = payload.TryGetProperty("layoutType", out var lt) ? lt.GetString() ?? "strip2x6" : "strip2x6";
                    string frameUrl = payload.TryGetProperty("frameUrl", out var fu) ? fu.GetString() ?? "" : "";
                    string filterCode = payload.TryGetProperty("filterCode", out var fcCode) ? fcCode.GetString() ?? "" : "";
                    bool beautify = payload.TryGetProperty("beautify", out var btf) && btf.GetBoolean();

                    // Setup FrameTemplate
                    var template = new FrameTemplate
                    {
                        TemplateId = layoutType,
                        Name = layoutType == "strip2x6" ? "Dual 2x6 Strip" : "Postcard 4x6",
                        CanvasWidth = 2400,
                        CanvasHeight = 3600,
                        SupportedPaperType = layoutType == "strip2x6" ? PaperType.DoubleStrip2x6 : PaperType.Card4x6,
                        FilterCode = filterCode,
                        Holes = new List<HoleLocation>()
                    };

                    // Dynamic holes from payload
                    var payloadHoles = ParseHoles(payload);
                    if (payloadHoles.Count > 0)
                    {
                        template.Holes.AddRange(payloadHoles);
                    }
                    else if (template.SupportedPaperType == PaperType.DoubleStrip2x6)
                    {
                        // 4 vertical holes on left strip (0 to 1200 on 2400 width)
                        template.Holes.Add(new HoleLocation { ShotIndex = 1, X = 545, Y = 173, Width = 1310, Height = 833, CornerRadius = 12 });
                        template.Holes.Add(new HoleLocation { ShotIndex = 2, X = 545, Y = 1006, Width = 1310, Height = 794, CornerRadius = 12 });
                        template.Holes.Add(new HoleLocation { ShotIndex = 3, X = 545, Y = 1800, Width = 1310, Height = 797, CornerRadius = 12 });
                        template.Holes.Add(new HoleLocation { ShotIndex = 4, X = 545, Y = 2597, Width = 1310, Height = 830, CornerRadius = 12 });
                    }
                    else
                    {
                        // 4 holes 2x2 grid for 4x6 postcard (2400x3600 canvas)
                        template.Holes.Add(new HoleLocation { ShotIndex = 1, X = 142, Y = 792, Width = 1016, Height = 1186, CornerRadius = 12 });
                        template.Holes.Add(new HoleLocation { ShotIndex = 2, X = 1242, Y = 792, Width = 1016, Height = 1186, CornerRadius = 12 });
                        template.Holes.Add(new HoleLocation { ShotIndex = 3, X = 142, Y = 2057, Width = 1016, Height = 1186, CornerRadius = 12 });
                        template.Holes.Add(new HoleLocation { ShotIndex = 4, X = 1242, Y = 2057, Width = 1016, Height = 1186, CornerRadius = 12 });
                    }

                    // Check overlay frame image (download or local cache)
                    template.OverlayImagePath = await EnsureLocalFrameAsync(frameUrl) ?? string.Empty;

                    // Phóng to / độ lệch của từng ô do người dùng chỉnh ở bước chọn ảnh
                    if (payload.TryGetProperty("transforms", out var transformsEl) && transformsEl.ValueKind == JsonValueKind.Array)
                    {
                        int tIdx = 0;
                        foreach (var t in transformsEl.EnumerateArray())
                        {
                            if (tIdx >= template.Holes.Count) break;
                            if (t.ValueKind == JsonValueKind.Object)
                            {
                                var hole = template.Holes[tIdx];
                                if (t.TryGetProperty("scale", out var sEl) && sEl.ValueKind == JsonValueKind.Number) hole.PhotoScale = sEl.GetDouble();
                                if (t.TryGetProperty("x", out var oxEl) && oxEl.ValueKind == JsonValueKind.Number) hole.PhotoOffsetX = oxEl.GetDouble();
                                if (t.TryGetProperty("y", out var oyEl) && oyEl.ValueKind == JsonValueKind.Number) hole.PhotoOffsetY = oyEl.GetDouble();
                            }
                            tIdx++;
                        }
                    }

                    // Tự động giải mã kích thước Canvas thực tế của Frame Overlay PNG
                    if (!string.IsNullOrEmpty(template.OverlayImagePath) && File.Exists(template.OverlayImagePath))
                    {
                        try
                        {
                            using var codec = SkiaSharp.SKCodec.Create(template.OverlayImagePath);
                            if (codec != null && codec.Info.Width > 0 && codec.Info.Height > 0)
                            {
                                template.CanvasWidth = codec.Info.Width;
                                template.CanvasHeight = codec.Info.Height;
                                App.Log($"[INFO] Detected Frame dimensions from PNG: {template.CanvasWidth} x {template.CanvasHeight}");
                            }
                        }
                        catch (Exception cEx)
                        {
                            App.Log($"[WARN] Failed to read Frame dimensions: {cEx.Message}");
                        }
                    }

                    // Ảnh ghép lấy theo đúng thứ tự ô mà frontend gửi (base64 data URL hoặc đường dẫn file).
                    // Ghi ra selected_{n}.jpg để không đè lên ảnh gốc shot_{n}.jpg của phiên.
                    var composePhotos = new List<string>();
                    if (payload.TryGetProperty("photos", out var photosEl) && photosEl.ValueKind == JsonValueKind.Array)
                    {
                        int pIdx = 1;
                        foreach (var pItem in photosEl.EnumerateArray())
                        {
                            string pStr = pItem.ValueKind == JsonValueKind.String ? pItem.GetString() ?? "" : "";
                            if (pStr.Contains(";base64,"))
                            {
                                string base64Data = pStr.Substring(pStr.IndexOf(";base64,") + 8);
                                string pPath = Path.Combine(_currentSessionDir, $"selected_{pIdx}.jpg");
                                try
                                {
                                    await File.WriteAllBytesAsync(pPath, Convert.FromBase64String(base64Data));
                                    composePhotos.Add(pPath);
                                }
                                catch (Exception pEx)
                                {
                                    App.Log($"[WARN] Error decoding photo #{pIdx} in compose_strip: {pEx.Message}");
                                }
                            }
                            else if (!string.IsNullOrWhiteSpace(pStr) && File.Exists(pStr))
                            {
                                composePhotos.Add(pStr);
                            }
                            pIdx++;
                        }
                    }

                    // Frontend không gửi ảnh hợp lệ: dùng ảnh gốc của phiên, cuối cùng mới tới ảnh giả lập
                    if (composePhotos.Count == 0)
                    {
                        composePhotos.AddRange(_capturedRawPhotos);
                    }
                    if (composePhotos.Count == 0)
                    {
                        for (int i = 1; i <= 4; i++)
                        {
                            string mockPath = Path.Combine(_currentSessionDir, $"mock_shot_{i}.jpg");
                            if (!File.Exists(mockPath))
                            {
                                using var surf = SkiaSharp.SKSurface.Create(new SkiaSharp.SKImageInfo(1200, 800));
                                var c = surf.Canvas;
                                c.Clear(i % 2 == 0 ? SkiaSharp.SKColors.MistyRose : SkiaSharp.SKColors.LightCyan);
                                using var p = new SkiaSharp.SKPaint { Color = SkiaSharp.SKColors.DarkSlateGray, IsAntialias = true };
                                using var font = new SkiaSharp.SKFont(SkiaSharp.SKTypeface.Default, 42);
                                c.DrawText($"Favori Photo #{i}", 200, 420, SkiaSharp.SKTextAlign.Left, font, p);
                                using var snap = surf.Snapshot();
                                using var d = snap.Encode(SkiaSharp.SKEncodedImageFormat.Jpeg, 90);
                                using var fs = File.OpenWrite(mockPath);
                                d.SaveTo(fs);
                            }
                            composePhotos.Add(mockPath);
                        }
                    }

                    string compositeOut = Path.Combine(_currentSessionDir, "composite_final.jpg");
                    await _skiaCompositor.ComposePrintSheetAsync(template, composePhotos, compositeOut);
                    _lastCompositePath = compositeOut;

                    // Link tải ảnh dựng ngay trên máy (không chờ mạng); frame có vị trí QR thì in QR lên bản in
                    bool cloudActive = CloudActive;
                    string shareUrl = cloudActive ? CurrentShareUrl() : "";
                    if (cloudActive) _lastCompositePath = StampQrForPrint(compositeOut, shareUrl, payload);

                    // Also export animated recap gif using MagickCompositor
                    string recapGif = Path.Combine(_currentSessionDir, "recap.gif");
                    var recapPhotos = _capturedRawPhotos.Count > 0 ? new List<string>(_capturedRawPhotos) : composePhotos;
                    _ = _magickCompositor.CreateAnimatedRecapAsync(recapPhotos, recapGif);

                    // Save copies to permanent local gallery folder
                    if (!string.IsNullOrEmpty(_localGalleryDir) && Directory.Exists(_localGalleryDir))
                    {
                        try
                        {
                            string galComp = Path.Combine(_localGalleryDir, "composite_final.jpg");
                            // Lưu đúng bản đã in (có QR nếu frame có vị trí QR)
                            File.Copy(_lastCompositePath, galComp, true);

                            if (File.Exists(recapGif))
                            {
                                File.Copy(recapGif, Path.Combine(_localGalleryDir, "recap.gif"), true);
                            }

                            // Write session manifest JSON
                            var sessionInfo = new
                            {
                                sessionId = _currentSessionId,
                                createdAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                                layoutType = layoutType,
                                frameUrl = frameUrl,
                                filterCode = filterCode,
                                shotCount = _capturedRawPhotos.Count
                            };
                            string infoJson = JsonSerializer.Serialize(sessionInfo, new JsonSerializerOptions { WriteIndented = true });
                            File.WriteAllText(Path.Combine(_localGalleryDir, "session_info.json"), infoJson);
                            App.Log($"[INFO] Saved complete session artifacts to local gallery: {_localGalleryDir}");
                        }
                        catch (Exception gEx)
                        {
                            App.Log($"[WARN] Could not copy composite to gallery: {gEx.Message}");
                        }
                    }

                    // Gửi phiên lên server chạy nền: ảnh ghép giống bản in (có QR), ảnh lẻ, ảnh nhỏ, video
                    if (cloudActive) EnqueueCloudSession(_lastCompositePath, shareUrl, payload);

                    // Ảnh xem trước = đúng bản sẽ in (có QR nếu frame có vị trí QR)
                    byte[] compBytes = await File.ReadAllBytesAsync(_lastCompositePath);
                    string compB64 = Convert.ToBase64String(compBytes);

                    // Record to store.db
                    _ = _dbService.RecordTransactionAsync(_currentSessionId, _currentSessionId, layoutType, 1, _kioskMode, 0);
                    _ = _dbService.AddFileItemAsync(Guid.NewGuid().ToString("N"), _currentSessionId, compositeOut, "composite");

                    SendSuccessResponse(id, new
                    {
                        compositePath = compositeOut,
                        previewBase64 = compB64,
                        qrUrl = shareUrl,
                        qrDataUrl = cloudActive ? QrDataUrl(shareUrl) : "",
                        retentionHours = CloudSettingsSnapshot().RetentionHours,
                    });
                    break;

                case "print_session":
                    if (File.Exists(_lastCompositePath))
                    {
                        int printCopies = payload.ValueKind == JsonValueKind.Object
                            && payload.TryGetProperty("copies", out var copiesEl) && copiesEl.ValueKind == JsonValueKind.Number
                            ? Math.Clamp(copiesEl.GetInt32(), 1, 20)
                            : 1;
                        var pRes = await _printerService.PrintLayoutAsync(new PrintJobRequest { ImagePath = _lastCompositePath, Copies = printCopies });
                        SendSuccessResponse(id, new
                        {
                            success = pRes.IsSuccess,
                            message = pRes.UserMessage,
                            sheets = pRes.SheetsPrinted,
                            remainingPaper = _paperCounter.RemainingSheets,
                        });
                    }
                    else
                    {
                        SendSuccessResponse(id, new { success = false, message = "Chưa có ảnh ghép để in.", sheets = 0, remainingPaper = _paperCounter.RemainingSheets });
                    }
                    break;

                case "printer_status":
                    SendSuccessResponse(id, await BuildPrinterStatusDtoAsync());
                    break;

                case "printer_select":
                    {
                        string? selectName = payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("name", out var nameEl)
                            ? nameEl.GetString()
                            : null;
                        _printerService.SelectPrinter(selectName);
                        App.Log($"[INFO] Printer selected: \"{selectName}\"");
                        SendSuccessResponse(id, await BuildPrinterStatusDtoAsync());
                    }
                    break;

                case "print_test_page":
                    {
                        string testPagePath = Path.Combine(Path.GetTempPath(), "HihiBooth_Temp", "print_test_page.jpg");
                        await Task.Run(() => WriteTestPage(testPagePath));
                        var testRes = await _printerService.PrintLayoutAsync(new PrintJobRequest { ImagePath = testPagePath, Copies = 1 });
                        if (testRes.IsSuccess)
                        {
                            SendSuccessResponse(id, new { success = true, message = testRes.UserMessage, remainingPaper = _paperCounter.RemainingSheets });
                        }
                        else
                        {
                            SendErrorResponse(id, testRes.UserMessage);
                        }
                    }
                    break;

                default:
                    SendSuccessResponse(id, new { success = true });
                    break;
            }
        }
        catch (Exception ex)
        {
            SendErrorResponse(id, ex.Message);
        }
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _cmsJsonCache = new();
    private const string CmsBaseUrl = "http://129.221.221.113:8080";

    /// <summary>
    /// Lấy JSON dạng mảng từ CMS (frames / presets / layouts), có cache trong RAM và trên đĩa
    /// (cached_{key}.json) để kiosk vẫn chạy khi mất mạng.
    /// </summary>
    /// <summary>Đọc mảng "holes" ({x, y, w, h}) trong payload; toạ độ có thể là số thực.</summary>
    private static List<HoleLocation> ParseHoles(JsonElement payload)
    {
        var holes = new List<HoleLocation>();
        if (payload.ValueKind != JsonValueKind.Object
            || !payload.TryGetProperty("holes", out var holesEl)
            || holesEl.ValueKind != JsonValueKind.Array)
        {
            return holes;
        }

        static int Num(JsonElement el, string name, int fallback) =>
            el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number ? (int)Math.Round(p.GetDouble()) : fallback;

        int shotIdx = 1;
        foreach (var h in holesEl.EnumerateArray())
        {
            if (h.ValueKind != JsonValueKind.Object) continue;
            holes.Add(new HoleLocation
            {
                ShotIndex = shotIdx++,
                X = Num(h, "x", 0),
                Y = Num(h, "y", 0),
                Width = Num(h, "w", 1016),
                Height = Num(h, "h", 1186),
                CornerRadius = 12
            });
        }
        return holes;
    }

    /// <summary>Trả về đường dẫn frame PNG trên máy: dùng file sẵn có hoặc tải về FramesCache. Null nếu không lấy được.</summary>
    private static async Task<string?> EnsureLocalFrameAsync(string frameUrl)
    {
        if (string.IsNullOrWhiteSpace(frameUrl)) return null;
        if (File.Exists(frameUrl)) return frameUrl;
        if (!Uri.TryCreate(frameUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return null;
        }

        try
        {
            string baseDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "HihiBooth", "Data");
            string cacheDir = Path.Combine(baseDataDir, "FramesCache");
            Directory.CreateDirectory(cacheDir);
            string localFile = Path.Combine(cacheDir, Path.GetFileName(uri.AbsolutePath));
            if (!File.Exists(localFile))
            {
                using var hc = new HttpClient();
                var fBytes = await hc.GetByteArrayAsync(uri);
                string tmp = localFile + ".tmp";
                await File.WriteAllBytesAsync(tmp, fBytes);
                File.Move(tmp, localFile, overwrite: true);
            }
            return localFile;
        }
        catch (Exception ex)
        {
            App.Log($"[WARN] Could not fetch overlay frame from {frameUrl}: {ex.Message}");
            return null;
        }
    }

    private async Task<string> GetOrFetchCmsJsonAsync(string key, string relativeUrl)
    {
        if (_cmsJsonCache.TryGetValue(key, out var cached) && !string.IsNullOrEmpty(cached)) return cached;

        string cacheFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "HihiBooth", "Data", $"cached_{key}.json");
        string? fromDisk = null;
        if (File.Exists(cacheFile))
        {
            try { fromDisk = await File.ReadAllTextAsync(cacheFile); } catch { }
        }

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            string fetched = await http.GetStringAsync(CmsBaseUrl + relativeUrl);
            if (!string.IsNullOrWhiteSpace(fetched) && fetched.TrimStart().StartsWith("["))
            {
                _cmsJsonCache[key] = fetched;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        string dir = Path.GetDirectoryName(cacheFile)!;
                        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                        await File.WriteAllTextAsync(cacheFile, fetched);
                    }
                    catch { }
                });
                return fetched;
            }
        }
        catch (Exception ex)
        {
            App.Log($"[WARN] Fetch {key} from CMS failed: {ex.Message}");
        }

        if (!string.IsNullOrEmpty(fromDisk))
        {
            _cmsJsonCache[key] = fromDisk;
            return fromDisk;
        }
        throw new InvalidOperationException($"Could not fetch {key} and no cache available.");
    }

    private Task<string> GetOrFetchFramesJsonAsync() => GetOrFetchCmsJsonAsync("frames", "/api/frames?onlyActive=true");
    private Task<string> GetOrFetchPresetsJsonAsync() => GetOrFetchCmsJsonAsync("presets", "/api/presets?onlyActive=true");
    private Task<string> GetOrFetchLayoutsJsonAsync() => GetOrFetchCmsJsonAsync("layouts", "/api/layouts?onlyActive=false");

    private void SendRawSuccessResponse(string id, string rawJsonData)
    {
        Dispatcher.Invoke(() =>
        {
            try
            {
                if (MainWebView?.CoreWebView2 != null)
                {
                    string res = $"{{\"id\":\"{id}\",\"success\":true,\"data\":{rawJsonData}}}";
                    MainWebView.CoreWebView2.PostWebMessageAsJson(res);
                }
            }
            catch (Exception ex)
            {
                App.Log($"[WARN] SendRawSuccessResponse failed: {ex.Message}");
            }
        });
    }

    private void SendSuccessResponse(string id, object? data)
    {
        Dispatcher.Invoke(() =>
        {
            try
            {
                if (MainWebView?.CoreWebView2 != null)
                {
                    var res = new { id, success = true, data };
                    MainWebView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(res));
                }
            }
            catch (Exception ex)
            {
                App.Log($"[WARN] SendSuccessResponse failed: {ex.Message}");
            }
        });
    }

    private void SendErrorResponse(string id, string error)
    {
        Dispatcher.Invoke(() =>
        {
            try
            {
                if (MainWebView?.CoreWebView2 != null)
                {
                    var res = new { id, success = false, error };
                    MainWebView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(res));
                }
            }
            catch (Exception ex)
            {
                App.Log($"[WARN] SendErrorResponse failed: {ex.Message}");
            }
        });
    }

    /// <summary>Như SendEventToWeb nhưng không chờ luồng UI (gọi được từ luồng máy ảnh).</summary>
    private void PostEventToWeb(string eventName, object? data)
    {
        Dispatcher.BeginInvoke(() => SendEventToWeb(eventName, data));
    }

    private void SendEventToWeb(string eventName, object? data)
    {
        Dispatcher.Invoke(() =>
        {
            try
            {
                if (MainWebView?.CoreWebView2 != null)
                {
                    var evt = new { @event = eventName, data };
                    MainWebView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(evt));
                }
            }
            catch (Exception ex)
            {
                // Can happen during initial navigation
            }
        });
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _heartbeatTimer?.Dispose();
        _keyboardHook?.Dispose();
        StopTimelapse();
        _uploadQueue?.Dispose();
        _camera?.Dispose();
        _dbService?.Dispose();
    }
}
