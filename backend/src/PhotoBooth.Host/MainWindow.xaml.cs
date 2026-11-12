using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using PhotoBooth.Core.Data;
using PhotoBooth.Core.Interfaces;
using PhotoBooth.Core.Models;
using PhotoBooth.Infrastructure.Storage;
using PhotoBooth.Hardware.Camera;
using PhotoBooth.Hardware.Printer;
using PhotoBooth.Host.Services;
using PhotoBooth.Imaging.Compositor;

namespace PhotoBooth.Host;

public partial class MainWindow : Window
{
    private KeyboardHook? _keyboardHook;
    private MockCameraProvider? _camera;
    private IPaperCounter _paperCounter = null!;
    private IPrinterService _printerService = null!;
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

            _printerService = new ThermalPrinterService(_paperCounter);
            _skiaCompositor = new NativeSkiaCompositor();
            _magickCompositor = new MagickCompositor();
            _dbService = new DatabaseService(Path.Combine(baseDataDir, "store.db"));
            await _dbService.InitializeAsync();
            App.Log("[INFO] Step 2.2: DB initialized");

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
            _camera = new MockCameraProvider();
            await _camera.InitializeAsync();
            _camera.FrameArrived += OnCameraFrameArrived;
            App.Log("[INFO] Step 3.1: Camera provider ready");

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
            if (args.IsSuccess && _camera != null && _useMockLiveView)
            {
                _ = _camera.StartLiveViewAsync();
            }
        };

        // Check if Vite dev server is running with non-blocking socket probe
        string devUrl = "http://localhost:5173";
        bool isDevRunning = false;
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

    private bool _useMockLiveView = false;
    private int _frameThrottle = 0;
    private void OnCameraFrameArrived(object? sender, FrameLeaseEventArgs e)
    {
        // Do not broadcast mock frames if using real webcam
        if (!_useMockLiveView) return;

        // Broadcast camera frame to React UI every 3 frames (~10 fps to prevent IPC saturating)
        if (++_frameThrottle % 3 == 0)
        {
            byte[] bytes = e.JpegData.ToArray();
            string base64 = Convert.ToBase64String(bytes);
            SendEventToWeb("camera_frame", new { base64 });
        }
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            string json = e.WebMessageAsJson;
            App.Log($"[IPC IN] {json}");
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
                        isPrinterReady = true,
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

                case "get_cameras":
                    SendSuccessResponse(id, new[]
                    {
                        new { id = "hd_webcam", name = "HD Webcam", isDefault = true }
                    });
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
                    App.Log($"[INFO] Session started: {_currentSessionId}, local gallery: {_localGalleryDir}");

                    SendSuccessResponse(id, new { sessionId = _currentSessionId, galleryDir = _localGalleryDir });
                    break;

                case "capture_shot":
                    int shotIndex = payload.TryGetProperty("shotIndex", out var shotEl) ? shotEl.GetInt32() : _capturedRawPhotos.Count + 1;
                    if (string.IsNullOrEmpty(_currentSessionDir))
                    {
                        _currentSessionDir = Path.Combine(Path.GetTempPath(), "HihiBooth_Temp");
                        if (!Directory.Exists(_currentSessionDir)) Directory.CreateDirectory(_currentSessionDir);
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

                    if (_camera != null && _useMockLiveView)
                    {
                        var capResult = await _camera.CapturePhotoAsync(_currentSessionDir);
                        if (capResult.IsSuccess && !string.IsNullOrEmpty(capResult.RawImagePath))
                        {
                            _capturedRawPhotos.Add(capResult.RawImagePath);
                            byte[] bytes = await File.ReadAllBytesAsync(capResult.RawImagePath);
                            string b64 = Convert.ToBase64String(bytes);
                            SendSuccessResponse(id, new { filePath = capResult.RawImagePath, base64 = b64 });
                            return;
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
                    if (payload.TryGetProperty("holes", out var holesEl) && holesEl.ValueKind == JsonValueKind.Array && holesEl.GetArrayLength() > 0)
                    {
                        int shotIdx = 1;
                        foreach (var h in holesEl.EnumerateArray())
                        {
                            int x = h.TryGetProperty("x", out var xProp) ? xProp.GetInt32() : 0;
                            int y = h.TryGetProperty("y", out var yProp) ? yProp.GetInt32() : 0;
                            int w = h.TryGetProperty("w", out var wProp) ? wProp.GetInt32() : 1016;
                            int hVal = h.TryGetProperty("h", out var hProp) ? hProp.GetInt32() : 1186;
                            template.Holes.Add(new HoleLocation
                            {
                                ShotIndex = shotIdx++,
                                X = x,
                                Y = y,
                                Width = w,
                                Height = hVal,
                                CornerRadius = 12
                            });
                        }
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
                    if (!string.IsNullOrWhiteSpace(frameUrl))
                    {
                        if (File.Exists(frameUrl))
                        {
                            template.OverlayImagePath = frameUrl;
                        }
                        else if (Uri.TryCreate(frameUrl, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                        {
                            try
                            {
                                string baseDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "HihiBooth", "Data");
                                string cacheDir = Path.Combine(baseDataDir, "FramesCache");
                                if (!Directory.Exists(cacheDir)) Directory.CreateDirectory(cacheDir);
                                string localFile = Path.Combine(cacheDir, Path.GetFileName(uri.AbsolutePath));
                                if (!File.Exists(localFile))
                                {
                                    using var hc = new HttpClient();
                                    var fBytes = await hc.GetByteArrayAsync(uri);
                                    await File.WriteAllBytesAsync(localFile, fBytes);
                                }
                                template.OverlayImagePath = localFile;
                            }
                            catch (Exception ex)
                            {
                                App.Log($"[WARN] Could not fetch overlay frame from {frameUrl}: {ex.Message}");
                            }
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

                    // Check if frontend passed photos list (base64 data URLs or file paths)
                    if (payload.TryGetProperty("photos", out var photosEl) && photosEl.ValueKind == JsonValueKind.Array)
                    {
                        int pIdx = 1;
                        foreach (var pItem in photosEl.EnumerateArray())
                        {
                            string pStr = pItem.GetString() ?? "";
                            if (!string.IsNullOrWhiteSpace(pStr) && pStr.Contains(";base64,"))
                            {
                                string base64Data = pStr.Substring(pStr.IndexOf(";base64,") + 8);
                                string pPath = Path.Combine(_currentSessionDir, $"shot_{pIdx}.jpg");
                                try
                                {
                                    byte[] pBytes = Convert.FromBase64String(base64Data);
                                    File.WriteAllBytes(pPath, pBytes);
                                    if (!_capturedRawPhotos.Contains(pPath))
                                    {
                                        _capturedRawPhotos.Add(pPath);
                                    }
                                }
                                catch (Exception pEx)
                                {
                                    App.Log($"[WARN] Error decoding photo #{pIdx} in compose_strip: {pEx.Message}");
                                }
                            }
                            else if (!string.IsNullOrWhiteSpace(pStr) && File.Exists(pStr) && !_capturedRawPhotos.Contains(pStr))
                            {
                                _capturedRawPhotos.Add(pStr);
                            }
                            pIdx++;
                        }
                    }

                    // Ensure at least 4 photos exist for composition
                    if (_capturedRawPhotos.Count == 0)
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
                            _capturedRawPhotos.Add(mockPath);
                        }
                    }

                    string compositeOut = Path.Combine(_currentSessionDir, "composite_final.jpg");
                    await _skiaCompositor.ComposePrintSheetAsync(template, _capturedRawPhotos, compositeOut);
                    _lastCompositePath = compositeOut;

                    // Also export animated recap gif using MagickCompositor
                    string recapGif = Path.Combine(_currentSessionDir, "recap.gif");
                    _ = _magickCompositor.CreateAnimatedRecapAsync(_capturedRawPhotos, recapGif);

                    // Save copies to permanent local gallery folder
                    if (!string.IsNullOrEmpty(_localGalleryDir) && Directory.Exists(_localGalleryDir))
                    {
                        try
                        {
                            string galComp = Path.Combine(_localGalleryDir, "composite_final.jpg");
                            File.Copy(compositeOut, galComp, true);

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

                    // Read preview bytes
                    byte[] compBytes = await File.ReadAllBytesAsync(compositeOut);
                    string compB64 = Convert.ToBase64String(compBytes);

                    string qrUrl = $"https://hihibooth.vn/gallery/{_currentSessionId}";

                    // Record to store.db
                    _ = _dbService.RecordTransactionAsync(_currentSessionId, _currentSessionId, layoutType, 1, _kioskMode, 0);
                    _ = _dbService.AddFileItemAsync(Guid.NewGuid().ToString("N"), _currentSessionId, compositeOut, "composite");

                    SendSuccessResponse(id, new
                    {
                        compositePath = compositeOut,
                        previewBase64 = compB64,
                        qrUrl = qrUrl
                    });
                    break;

                case "print_session":
                    if (File.Exists(_lastCompositePath))
                    {
                        var printReq = new PrintJobRequest
                        {
                            ImagePath = _lastCompositePath,
                            Copies = 1
                        };
                        var pRes = await _printerService.PrintLayoutAsync(printReq);
                        SendSuccessResponse(id, new { success = pRes.IsSuccess, remainingPaper = _paperCounter.RemainingSheets });
                    }
                    else
                    {
                        SendSuccessResponse(id, new { success = true, remainingPaper = _paperCounter.RemainingSheets });
                    }
                    break;

                case "print_test_page":
                    SendSuccessResponse(id, new { success = true });
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

    private static string? _cachedFramesJson;
    private static string? _cachedPresetsJson;
    private static readonly string FramesCacheFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "HihiBooth", "Data", "cached_frames.json");
    private static readonly string PresetsCacheFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "HihiBooth", "Data", "cached_presets.json");

    private async Task<string> GetOrFetchFramesJsonAsync()
    {
        if (!string.IsNullOrEmpty(_cachedFramesJson)) return _cachedFramesJson;

        if (File.Exists(FramesCacheFilePath))
        {
            try { _cachedFramesJson = await File.ReadAllTextAsync(FramesCacheFilePath); } catch { }
        }

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            string fetched = await http.GetStringAsync("http://129.221.221.113:8080/api/frames?onlyActive=true");
            if (!string.IsNullOrWhiteSpace(fetched) && fetched.TrimStart().StartsWith("["))
            {
                _cachedFramesJson = fetched;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        string dir = Path.GetDirectoryName(FramesCacheFilePath)!;
                        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                        await File.WriteAllTextAsync(FramesCacheFilePath, fetched);
                    }
                    catch { }
                });
                return fetched;
            }
        }
        catch (Exception ex)
        {
            App.Log($"[WARN] Fetch frames from CMS failed: {ex.Message}");
        }

        if (!string.IsNullOrEmpty(_cachedFramesJson)) return _cachedFramesJson;
        throw new InvalidOperationException("Could not fetch frames and no cache available.");
    }

    private async Task<string> GetOrFetchPresetsJsonAsync()
    {
        if (!string.IsNullOrEmpty(_cachedPresetsJson)) return _cachedPresetsJson;

        if (File.Exists(PresetsCacheFilePath))
        {
            try { _cachedPresetsJson = await File.ReadAllTextAsync(PresetsCacheFilePath); } catch { }
        }

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            string fetched = await http.GetStringAsync("http://129.221.221.113:8080/api/presets?onlyActive=true");
            if (!string.IsNullOrWhiteSpace(fetched) && fetched.TrimStart().StartsWith("["))
            {
                _cachedPresetsJson = fetched;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        string dir = Path.GetDirectoryName(PresetsCacheFilePath)!;
                        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                        await File.WriteAllTextAsync(PresetsCacheFilePath, fetched);
                    }
                    catch { }
                });
                return fetched;
            }
        }
        catch (Exception ex)
        {
            App.Log($"[WARN] Fetch presets from CMS failed: {ex.Message}");
        }

        if (!string.IsNullOrEmpty(_cachedPresetsJson)) return _cachedPresetsJson;
        throw new InvalidOperationException("Could not fetch presets and no cache available.");
    }

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
        _camera?.Dispose();
        _dbService?.Dispose();
    }
}
