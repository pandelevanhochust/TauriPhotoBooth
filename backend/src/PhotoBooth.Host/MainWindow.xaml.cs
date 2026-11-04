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
    private string _kioskMode = "FreeEvent";
    private readonly List<string> _capturedRawPhotos = new();
    private string _lastCompositePath = "";

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
        var env = await CoreWebView2Environment.CreateAsync(null, webViewDataFolder);
        App.Log("[INFO] InitializeWebView2Async: Ensuring CoreWebView2");
        await MainWebView.EnsureCoreWebView2Async(env);
        App.Log("[INFO] InitializeWebView2Async: CoreWebView2 ready");

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
            if (args.IsSuccess && _camera != null)
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

    private int _frameThrottle = 0;
    private void OnCameraFrameArrived(object? sender, FrameLeaseEventArgs e)
    {
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

                case "verify_admin_pin":
                    string enteredPin = payload.TryGetProperty("pin", out var pinEl) ? pinEl.GetString() ?? "" : "";
                    bool isValid = (enteredPin == "8888" || enteredPin == "1234");
                    SendSuccessResponse(id, isValid);
                    break;

                case "start_capture_session":
                    _currentSessionId = $"SESSION_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid().ToString("N")[..6]}";
                    string commonData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "HihiBooth", "Data", "Sessions", _currentSessionId);
                    _currentSessionDir = commonData;
                    if (!Directory.Exists(_currentSessionDir)) Directory.CreateDirectory(_currentSessionDir);
                    _capturedRawPhotos.Clear();

                    SendSuccessResponse(id, new { sessionId = _currentSessionId });
                    break;

                case "capture_shot":
                    int shotIndex = payload.TryGetProperty("shotIndex", out var shotEl) ? shotEl.GetInt32() : _capturedRawPhotos.Count + 1;
                    if (string.IsNullOrEmpty(_currentSessionDir))
                    {
                        _currentSessionDir = Path.Combine(Path.GetTempPath(), "HihiBooth_Temp");
                        if (!Directory.Exists(_currentSessionDir)) Directory.CreateDirectory(_currentSessionDir);
                    }

                    if (_camera != null)
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
                    SendSuccessResponse(id, new { filePath = fallbackShot, base64 = "" });
                    break;

                case "compose_strip":
                    string layoutType = payload.TryGetProperty("layoutType", out var lt) ? lt.GetString() ?? "strip2x6" : "strip2x6";
                    string frameColor = payload.TryGetProperty("frameColor", out var fc) ? fc.GetString() ?? "#ffffff" : "#ffffff";
                    bool beautify = payload.TryGetProperty("beautify", out var btf) && btf.GetBoolean();

                    // Setup FrameTemplate
                    var template = new FrameTemplate
                    {
                        TemplateId = layoutType,
                        Name = layoutType == "strip2x6" ? "Dual 2x6 Strip" : "Postcard 4x6",
                        CanvasWidth = 1200,
                        CanvasHeight = 1800,
                        SupportedPaperType = layoutType == "strip2x6" ? PaperType.DoubleStrip2x6 : PaperType.Card4x6,
                        Holes = new List<HoleLocation>()
                    };

                    if (template.SupportedPaperType == PaperType.DoubleStrip2x6)
                    {
                        // 4 vertical holes on left strip (0 to 600 width)
                        int slotW = 540;
                        int slotH = 370;
                        int startX = 30;
                        int startY = 40;
                        int gapY = 30;
                        for (int i = 0; i < 4; i++)
                        {
                            template.Holes.Add(new HoleLocation
                            {
                                ShotIndex = i + 1,
                                X = startX,
                                Y = startY + i * (slotH + gapY),
                                Width = slotW,
                                Height = slotH,
                                CornerRadius = 12
                            });
                        }
                    }
                    else
                    {
                        // 4 holes 2x2 grid for 4x6 postcard
                        int slotW = 550;
                        int slotH = 750;
                        template.Holes.Add(new HoleLocation { ShotIndex = 1, X = 35, Y = 50, Width = slotW, Height = slotH, CornerRadius = 12 });
                        template.Holes.Add(new HoleLocation { ShotIndex = 2, X = 615, Y = 50, Width = slotW, Height = slotH, CornerRadius = 12 });
                        template.Holes.Add(new HoleLocation { ShotIndex = 3, X = 35, Y = 850, Width = slotW, Height = slotH, CornerRadius = 12 });
                        template.Holes.Add(new HoleLocation { ShotIndex = 4, X = 615, Y = 850, Width = slotW, Height = slotH, CornerRadius = 12 });
                    }

                    string compositeOut = Path.Combine(_currentSessionDir, "composite_final.jpg");
                    await _skiaCompositor.ComposePrintSheetAsync(template, _capturedRawPhotos, compositeOut);
                    _lastCompositePath = compositeOut;

                    // Also export animated recap gif using MagickCompositor
                    string recapGif = Path.Combine(_currentSessionDir, "recap.gif");
                    _ = _magickCompositor.CreateAnimatedRecapAsync(_capturedRawPhotos, recapGif);

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
