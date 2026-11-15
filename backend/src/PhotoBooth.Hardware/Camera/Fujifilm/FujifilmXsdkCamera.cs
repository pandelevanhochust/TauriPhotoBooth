using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PhotoBooth.Core.Models;
using static PhotoBooth.Hardware.Camera.Fujifilm.XsdkNative;

namespace PhotoBooth.Hardware.Camera.Fujifilm;

/// <summary>
/// Máy ảnh FUJIFILM X series qua FUJIFILM X SDK (XAPI.dll).
/// Máy phải đặt chế độ USB là "USB TETHER SHOOTING AUTO" (menu Kết nối / Connection Setting).
/// Mọi lời gọi XAPI chạy tuần tự trên một luồng riêng. Trình tự chụp: dừng live view, dọn bộ đệm,
/// nhấn nửa (S1) rồi nhấn hết (S2), đọc ảnh JPEG từ bộ đệm của máy, bật lại live view.
/// </summary>
public sealed class FujifilmXsdkCamera : ITetheredCamera
{
    private static readonly TimeSpan ReconnectInterval = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(35);
    /// <summary>
    /// Nhịp đọc khung khi UI không cần hình. Live view vẫn chạy ngầm suốt lúc kết nối: X-S10 để yên
    /// (không live view) khoảng 20–30 giây là rớt liên lạc USB, còn đang live view thì giữ được ổn định.
    /// </summary>
    private static readonly TimeSpan StandbyFrameInterval = TimeSpan.FromMilliseconds(250);
    /// <summary>Chờ máy lấy nét sau khi nhấn nửa cò, trước khi nhấn hết.</summary>
    private const int AutofocusSettleMs = 450;
    private const int ReleaseAttempts = 6;
    private static readonly TimeSpan PingInterval = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan LiveViewStallTimeout = TimeSpan.FromMilliseconds(2500);

    private readonly Action<string> _log;
    private readonly string _sdkDirectory;
    private readonly BlockingCollection<Action> _queue = new();
    private readonly object _infoLock = new();
    private CameraInfo _info = new() { Vendor = "Fujifilm", Message = "Chưa khởi động" };

    private Thread? _thread;
    private int _workerThreadId;
    private volatile bool _stopping;

    // Chỉ dùng trên luồng XAPI
    private XsdkNative? _sdk;
    private bool _sdkFailed;
    private IntPtr _cam = IntPtr.Zero;
    private bool _useHalfPress = true;
    private int _fullPressMode = XSDK_RELEASE_S2_S1OFF;
    private bool _liveViewWanted;
    private bool _liveViewActive;
    private int _errorStreak;
    private DateTime _nextReconnect = DateTime.MinValue;
    private DateTime _nextFrame = DateTime.MinValue;
    private DateTime _nextPing = DateTime.MinValue;
    private DateTime _lastFrameAt = DateTime.MinValue;

    public event EventHandler<FrameLeaseEventArgs>? FrameArrived;
    public event EventHandler<CameraStatusChangedEventArgs>? StatusChanged;
    public event EventHandler<CameraInfo>? InfoChanged;

    public string Vendor => "Fujifilm";
    public bool AutoReconnect { get; set; } = true;

    /// <param name="sdkDirectory">Thư mục chứa XAPI.dll; mặc định là thư mục của app.</param>
    public FujifilmXsdkCamera(Action<string>? log = null, string? sdkDirectory = null)
    {
        _log = log ?? (_ => { });
        _sdkDirectory = sdkDirectory ?? AppContext.BaseDirectory;
    }

    // ------------------------------------------------------------------ API công khai

    public Task<OperationResult> InitializeAsync(CancellationToken cancellationToken = default)
    {
        EnsureThread();
        return Invoke(() =>
        {
            if (!EnsureSdk()) return OperationResult.Fail(ErrorCode.CameraSdkNotInstalled, GetInfo().Message);
            TryConnect();
            return _cam != IntPtr.Zero
                ? OperationResult.Ok()
                : OperationResult.Fail(ErrorCode.CameraNotConnected, GetInfo().Message);
        });
    }

    public Task<CameraInfo> ReconnectAsync()
    {
        EnsureThread();
        return Invoke(() =>
        {
            if (EnsureSdk())
            {
                Disconnect("Đang kết nối lại...");
                TryConnect();
            }
            return GetInfo();
        });
    }

    public CameraInfo GetInfo()
    {
        lock (_infoLock) return _info.Clone();
    }

    public bool IsConnected
    {
        get { lock (_infoLock) return _info.Connected; }
    }

    public CameraDeviceStatus GetStatus()
    {
        var info = GetInfo();
        if (!info.SdkAvailable) return CameraDeviceStatus.Error;
        if (!info.Connected) return CameraDeviceStatus.Disconnected;
        if (info.Status == "capturing") return CameraDeviceStatus.Capturing;
        return info.LiveView ? CameraDeviceStatus.LiveViewActive : CameraDeviceStatus.Ready;
    }

    public Task<OperationResult> StartLiveViewAsync(CancellationToken cancellationToken = default)
    {
        EnsureThread();
        return Invoke(() =>
        {
            _liveViewWanted = true;
            if (_cam == IntPtr.Zero) return OperationResult.Fail(ErrorCode.CameraNotConnected, "Chưa kết nối máy ảnh");
            if (_liveViewActive)
            {
                _nextFrame = DateTime.UtcNow;
                UpdateInfo(i => { i.LiveView = true; i.Status = "liveview"; });
                return OperationResult.Ok();
            }
            return StartLiveView()
                ? OperationResult.Ok()
                : OperationResult.Fail(ErrorCode.CameraLiveViewTimeout, "Không bật được live view");
        });
    }

    public Task<OperationResult> StopLiveViewAsync(CancellationToken cancellationToken = default)
    {
        EnsureThread();
        return Invoke(() =>
        {
            // Chỉ ngừng gửi khung cho UI; live view ngầm vẫn chạy để giữ kết nối
            _liveViewWanted = false;
            UpdateInfo(i => { i.LiveView = false; if (i.Connected) i.Status = "ready"; });
            return OperationResult.Ok();
        });
    }

    public Task<OperationResult> KeepAliveAsync(CancellationToken cancellationToken = default)
    {
        EnsureThread();
        return Invoke(() => _cam != IntPtr.Zero && Ping()
            ? OperationResult.Ok()
            : OperationResult.Fail(ErrorCode.CameraNotConnected, "Chưa kết nối máy ảnh"));
    }

    public async Task<CapturePhotoResult> CapturePhotoAsync(string destinationDirectory, CancellationToken cancellationToken = default)
    {
        EnsureThread();
        Directory.CreateDirectory(destinationDirectory);
        string path = Path.Combine(destinationDirectory, $"fuji_{DateTime.Now:yyyyMMdd_HHmmss_fff}.jpg");
        return await Invoke(() => CaptureCore(path));
    }

    public void Dispose()
    {
        if (_thread == null || _stopping) return;
        _stopping = true;
        _queue.Add(() => { });
        if (!_thread.Join(TimeSpan.FromSeconds(5))) _log("[WARN] Fujifilm XSDK thread did not stop in time");
    }

    // ------------------------------------------------------------------ Luồng XAPI

    private void EnsureThread()
    {
        if (_thread != null) return;
        lock (_queue)
        {
            if (_thread != null) return;
            _thread = new Thread(Run) { IsBackground = true, Name = "FujifilmXsdk" };
            _thread.Start();
        }
    }

    private Task<T> Invoke<T>(Func<T> func)
    {
        if (Environment.CurrentManagedThreadId == _workerThreadId) return Task.FromResult(func());
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (_stopping)
        {
            tcs.SetException(new ObjectDisposedException(nameof(FujifilmXsdkCamera)));
            return tcs.Task;
        }
        _queue.Add(() =>
        {
            try { tcs.SetResult(func()); }
            catch (Exception ex) { tcs.SetException(ex); }
        });
        return tcs.Task;
    }

    private void Run()
    {
        _workerThreadId = Environment.CurrentManagedThreadId;
        while (!_stopping)
        {
            if (_queue.TryTake(out var action, 10))
            {
                RunSafe(action);
                while (!_stopping && _queue.TryTake(out action)) RunSafe(action);
            }
            RunSafe(Tick);
        }

        RunSafe(() =>
        {
            Disconnect("Đã dừng");
            if (_sdk != null)
            {
                _sdk.Exit();
                _sdk.Dispose();
                _sdk = null;
            }
        });
        while (_queue.TryTake(out var pending)) RunSafe(pending);
    }

    private void RunSafe(Action action)
    {
        try { action(); }
        catch (Exception ex) { _log($"[ERROR] Fujifilm XSDK: {ex}"); }
    }

    private void Tick()
    {
        if (_sdk == null) return;
        var now = DateTime.UtcNow;

        if (_cam == IntPtr.Zero)
        {
            if (AutoReconnect && now >= _nextReconnect)
            {
                _nextReconnect = now + ReconnectInterval;
                TryConnect();
            }
            return;
        }

        if (!_liveViewActive && now >= _nextFrame)
        {
            _nextFrame = now + TimeSpan.FromSeconds(1);
            StartLiveView();
        }
        else if (_liveViewActive && now >= _nextFrame)
        {
            _nextFrame = now + (_liveViewWanted ? FrameInterval : StandbyFrameInterval);
            PollLiveViewFrame(now);
        }

        if (!_liveViewActive && now >= _nextPing)
        {
            _nextPing = now + PingInterval;
            if (!Ping()) Disconnect("Máy Fujifilm đã tắt hoặc bị rút dây. Đang chờ kết nối lại...");
        }
    }

    // ------------------------------------------------------------------ Kết nối

    private bool EnsureSdk()
    {
        if (_sdk != null) return true;
        if (_sdkFailed) return false;
        try
        {
            var sdk = XsdkNative.Load(_sdkDirectory);
            int err = sdk.Init();
            if (err != XSDK_COMPLETE)
            {
                sdk.Dispose();
                _sdkFailed = true;
                UpdateInfo(i => { i.SdkAvailable = false; i.Status = "error"; i.Message = $"Khởi tạo FUJIFILM X SDK thất bại (mã {err})"; });
                return false;
            }
            _sdk = sdk;
            UpdateInfo(i => { i.SdkAvailable = true; i.Message = "Đang tìm máy Fujifilm..."; });
            _log($"[INFO] FUJIFILM X SDK initialized ({sdk.LibraryPath})");
            return true;
        }
        catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException)
        {
            _sdkFailed = true;
            UpdateInfo(i =>
            {
                i.SdkAvailable = false;
                i.Status = "error";
                i.Message = "Không tìm thấy FUJIFILM X SDK (XAPI.dll + FF*.dll) cạnh file chạy của app.";
            });
            _log($"[WARN] FUJIFILM X SDK not available: {ex.Message}");
            return false;
        }
    }

    private void TryConnect()
    {
        if (_sdk == null || _cam != IntPtr.Zero) return;

        int count = 0;
        int err = _sdk.Detect(ref count);
        if (err != XSDK_COMPLETE || count <= 0)
        {
            SetDisconnected("Không tìm thấy máy Fujifilm. Kiểm tra dây USB, bật máy và đặt chế độ USB là USB TETHER SHOOTING AUTO.");
            return;
        }

        IntPtr cam = IntPtr.Zero;
        int mode = 0;
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            cam = IntPtr.Zero;
            mode = 0;
            err = _sdk.OpenEx("ENUM:0", ref cam, ref mode);
            if (err == XSDK_COMPLETE && cam != IntPtr.Zero) break;
            if (cam != IntPtr.Zero) _sdk.Close(cam);
            cam = IntPtr.Zero;
            Thread.Sleep(180);
        }
        if (cam == IntPtr.Zero)
        {
            var (openApi, openErr) = _sdk.OpenError();
            _log($"[WARN] Fujifilm OpenEx failed: result {err}, mode {mode}, API 0x{openApi:X}, error 0x{openErr:X}");
            SetDisconnected(openErr switch
            {
                XSDK_ERRCODE_INVALID_USBMODE =>
                    "Máy Fujifilm đang ở chế độ USB không hỗ trợ điều khiển. Trên máy: MENU > Cài đặt (cờ lê) > Cài đặt kết nối > Chế độ kết nối PC > chọn USB TETHER SHOOTING AUTO, rồi rút cắm lại dây.",
                XSDK_ERRCODE_NO_MODEL_MODULE =>
                    "Bộ FUJIFILM X SDK không có module cho đời máy này. Cần bản X SDK mới hơn.",
                _ => $"Thấy máy Fujifilm nhưng không mở được kết nối (mã 0x{openErr:X}). Tắt X Acquire / FUJIFILM X Webcam rồi thử lại.",
            });
            return;
        }

        if (mode != 0 && (mode & XSDK_DSC_MODE_TETHER) == 0)
        {
            _sdk.Close(cam);
            SetDisconnected("Máy Fujifilm chưa ở chế độ chụp tethered. Vào menu Kết nối > Chế độ kết nối PC (USB) và chọn USB TETHER SHOOTING AUTO.");
            return;
        }

        _cam = cam;
        try
        {
            var device = new XSDK_DeviceInformation();
            string model = _sdk.GetDeviceInfo(_cam, ref device) == XSDK_COMPLETE && !string.IsNullOrWhiteSpace(device.strProduct)
                ? device.strProduct.Trim()
                : "FUJIFILM";

            // Không cho máy tự tắt khi đang nối với booth
            _sdk.SetProp(_cam, API_CODE_SetAutoPowerOffSetting, SDK_AUTOPOWEROFF_OFF);
            _sdk.SetProp(_cam, API_CODE_SetCustomAutoPowerOff, SDK_AUTOPOWEROFF_OFF);

            if (!EnsurePcPriority(out string priorityError))
            {
                CloseHandle();
                SetDisconnected(priorityError);
                return;
            }
            ConfigureRelease();
            ConfigureMediaRecord();
            _sdk.SetProp(_cam, API_CODE_SetLiveViewImageQuality, 1);
            _sdk.SetProp(_cam, API_CODE_SetLiveViewImageSize, 1);
            _sdk.SetProp(_cam, API_CODE_SetLiveViewMode, 1);

            _liveViewActive = false;
            _errorStreak = 0;
            _nextFrame = DateTime.UtcNow + TimeSpan.FromMilliseconds(300);
            _nextPing = DateTime.UtcNow + PingInterval;

            UpdateInfo(i =>
            {
                i.Connected = true;
                i.Model = model;
                i.Port = "USB";
                i.Status = "ready";
                i.LiveView = false;
                i.BatteryLevel = null;
                i.Message = "Đã kết nối";
            });
            _log($"[INFO] Fujifilm camera connected: {model} (mode 0x{mode:X})");
        }
        catch
        {
            CloseHandle();
            throw;
        }
    }

    private bool EnsurePcPriority(out string error)
    {
        error = string.Empty;
        var modes = _sdk!.CapPriorityMode(_cam);
        if (modes != null && Array.IndexOf(modes, XSDK_PRIORITY_PC) < 0)
        {
            error = "Máy Fujifilm này không hỗ trợ điều khiển từ máy tính (PC Priority).";
            return false;
        }

        string last = "";
        for (int attempt = 1; attempt <= 5; attempt++)
        {
            if (_sdk.SetPriorityMode(_cam, XSDK_PRIORITY_PC) == XSDK_COMPLETE)
            {
                int current = 0;
                if (_sdk.GetPriorityMode(_cam, ref current) != XSDK_COMPLETE || current == XSDK_PRIORITY_PC) return true;
                last = $"priority 0x{current:X}";
            }
            else
            {
                last = _sdk.LastError(_cam).Text;
            }
            Thread.Sleep(180);
        }
        error = $"Không chuyển được máy Fujifilm sang chế độ điều khiển từ máy tính ({last}).";
        return false;
    }

    private void ConfigureRelease()
    {
        var modes = _sdk!.CapRelease(_cam);
        if (modes == null)
        {
            _useHalfPress = true;
            _fullPressMode = XSDK_RELEASE_S2_S1OFF;
            return;
        }
        bool hasS1 = Array.IndexOf(modes, XSDK_RELEASE_S1ON) >= 0;
        if (hasS1 && Array.IndexOf(modes, XSDK_RELEASE_S2_S1OFF) >= 0)
        {
            _useHalfPress = true;
            _fullPressMode = XSDK_RELEASE_S2_S1OFF;
        }
        else if (Array.IndexOf(modes, XSDK_RELEASE_SHOOT_S1OFF) >= 0)
        {
            _useHalfPress = hasS1;
            _fullPressMode = XSDK_RELEASE_SHOOT_S1OFF;
        }
        else
        {
            _log("[WARN] Fujifilm body does not advertise a known release mode; using S1 + S2/S1-off");
            _useHalfPress = true;
            _fullPressMode = XSDK_RELEASE_S2_S1OFF;
        }
    }

    private void ConfigureMediaRecord()
    {
        var modes = _sdk!.CapMediaRecord(_cam);
        if (modes == null) return;
        int mode = Array.IndexOf(modes, XSDK_MEDIAREC_JPEG) >= 0 ? XSDK_MEDIAREC_JPEG
            : Array.IndexOf(modes, XSDK_MEDIAREC_RAWJPEG) >= 0 ? XSDK_MEDIAREC_RAWJPEG
            : 0;
        if (mode != 0 && _sdk.SetMediaRecord(_cam, mode) != XSDK_COMPLETE)
        {
            _log($"[WARN] Fujifilm SetMediaRecord failed: {_sdk.LastError(_cam).Text}");
        }
    }

    private bool Ping()
    {
        if (_sdk == null || _cam == IntPtr.Zero) return false;
        int mode = 0;
        if (_sdk.GetPriorityMode(_cam, ref mode) == XSDK_COMPLETE)
        {
            _errorStreak = 0;
            return true;
        }
        return !IsCommunicationLost();
    }

    /// <summary>Lỗi liên lạc lặp lại nhiều lần thì coi như máy đã rút / tắt.</summary>
    private bool IsCommunicationLost()
    {
        var (_, err, _) = _sdk!.LastError(_cam);
        if (err == XSDK_ERRCODE_COMMUNICATION || err == XSDK_ERRCODE_TIMEOUT || err == 0)
        {
            return ++_errorStreak >= 3;
        }
        return false;
    }

    private void Disconnect(string message)
    {
        if (_cam != IntPtr.Zero)
        {
            try
            {
                if (_liveViewActive) _sdk?.SetProp(_cam, API_CODE_StopLiveView);
            }
            catch { }
            CloseHandle();
            _log($"[INFO] Fujifilm camera disconnected: {message}");
        }
        _liveViewActive = false;
        _nextReconnect = DateTime.UtcNow + ReconnectInterval;
        SetDisconnected(message);
    }

    private void CloseHandle()
    {
        if (_cam == IntPtr.Zero || _sdk == null) return;
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            if (_sdk.Close(_cam) == XSDK_COMPLETE) break;
            Thread.Sleep(150);
        }
        _cam = IntPtr.Zero;
    }

    private void SetDisconnected(string message)
    {
        UpdateInfo(i =>
        {
            i.Connected = false;
            i.LiveView = false;
            i.Status = "disconnected";
            i.Message = message;
            i.Model = string.Empty;
            i.BatteryLevel = null;
        });
    }

    // ------------------------------------------------------------------ Live view

    private bool StartLiveView()
    {
        if (_sdk == null || _cam == IntPtr.Zero) return false;
        if (_liveViewActive) return true;

        DrainBuffer(1500);
        if (_sdk.SetProp(_cam, API_CODE_StartLiveView) != XSDK_COMPLETE)
        {
            var (_, startErr, startText) = _sdk.LastError(_cam);
            _log($"[WARN] Fujifilm StartLiveView failed: {startText}");
            if (startErr is XSDK_ERRCODE_BUSY or XSDK_ERRCODE_FORCEMODE_BUSY or XSDK_ERRCODE_RUNNING_OTHER_FUNCTION)
            {
                // Máy còn bận (vừa chụp / vừa mở kết nối): thử lại sớm, không gửi thêm lệnh dừng
                _nextFrame = DateTime.UtcNow + TimeSpan.FromMilliseconds(400);
                return false;
            }
            _sdk.SetProp(_cam, API_CODE_StopLiveView);
            DrainBuffer(1000);
            if (IsCommunicationLost()) Disconnect("Mất kết nối với máy Fujifilm. Đang chờ kết nối lại...");
            return false;
        }

        _liveViewActive = true;
        _errorStreak = 0;
        _lastFrameAt = DateTime.UtcNow;
        _nextFrame = DateTime.UtcNow + FrameInterval;
        if (_liveViewWanted) UpdateInfo(i => { i.LiveView = true; i.Status = "liveview"; });
        return true;
    }

    private void StopLiveView(bool drain)
    {
        if (!_liveViewActive) return;
        _liveViewActive = false;
        if (_sdk != null && _cam != IntPtr.Zero)
        {
            _sdk.SetProp(_cam, API_CODE_StopLiveView);
            if (drain) DrainBuffer(1500);
        }
        UpdateInfo(i => { i.LiveView = false; if (i.Connected) i.Status = "ready"; });
    }

    private void PollLiveViewFrame(DateTime now)
    {
        var info = new XSDK_ImageInformation();
        if (_sdk!.ReadImageInfo(_cam, ref info) != XSDK_COMPLETE)
        {
            if (IsCommunicationLost()) Disconnect("Mất kết nối với máy Fujifilm. Đang chờ kết nối lại...");
            return;
        }
        _errorStreak = 0;

        int format = info.lFormat & 0xFF;
        if (format == XSDK_IMAGEFORMAT_LIVE && info.lDataSize > 0 && !_liveViewWanted)
        {
            // Live view ngầm (giữ kết nối): bỏ khung, không tải về
            _sdk.DeleteImage(_cam);
            _lastFrameAt = now;
        }
        else if (format == XSDK_IMAGEFORMAT_LIVE && info.lDataSize > 0)
        {
            var frame = _sdk.ReadImage(_cam, info.lDataSize);
            if (frame != null && frame.Length > 0)
            {
                _lastFrameAt = now;
                var handler = FrameArrived;
                if (handler != null)
                {
                    using var lease = new FrameLeaseEventArgs(frame, 0, 0, () => { });
                    handler(this, lease);
                }
            }
        }
        else if (format != XSDK_IMAGEFORMAT_NONE)
        {
            _sdk.DeleteImage(_cam);
        }

        if (now - _lastFrameAt > LiveViewStallTimeout)
        {
            _log("[WARN] Fujifilm live view stalled, restarting");
            StopLiveView(drain: true);
            _nextFrame = now + TimeSpan.FromMilliseconds(300);
        }
    }

    /// <summary>Xoá các ảnh còn trong bộ đệm của máy (khung live view cũ...) cho tới khi rỗng.</summary>
    private bool DrainBuffer(int timeoutMs)
    {
        var until = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < until)
        {
            var info = new XSDK_ImageInformation();
            if (_sdk!.ReadImageInfo(_cam, ref info) == XSDK_COMPLETE)
            {
                if ((info.lFormat & 0xFF) == XSDK_IMAGEFORMAT_NONE) return true;
                if (_sdk.DeleteImage(_cam) == XSDK_COMPLETE) continue;
            }
            Thread.Sleep(60);
        }
        return false;
    }

    // ------------------------------------------------------------------ Chụp ảnh

    private CapturePhotoResult CaptureCore(string path)
    {
        if (_sdk == null || _cam == IntPtr.Zero)
        {
            return Fail(ErrorCode.CameraNotConnected, GetInfo().Message is { Length: > 0 } m ? m : "Chưa kết nối máy ảnh");
        }

        bool halfPressed = false;
        UpdateInfo(i => i.Status = "capturing");
        try
        {
            StopLiveView(drain: false);
            _sdk.SetProp(_cam, API_CODE_StopLiveView);
            DrainBuffer(3000);

            bool accepted = false;
            int lastErr = 0;
            string diagnostic = "";
            for (int attempt = 1; attempt <= ReleaseAttempts && !accepted; attempt++)
            {
                if (_useHalfPress && !halfPressed)
                {
                    if (_sdk.Release(_cam, XSDK_RELEASE_S1ON) == XSDK_COMPLETE)
                    {
                        halfPressed = true;
                        // Nhấn hết ngay khi máy chưa lấy nét xong thì máy báo lỗi chụp (0x1008)
                        Thread.Sleep(AutofocusSettleMs);
                    }
                    else
                    {
                        (_, lastErr, diagnostic) = _sdk.LastError(_cam);
                        _log($"[WARN] Fujifilm half-press attempt {attempt} failed: {diagnostic}");
                        if (!IsRetryable(lastErr)) break;
                        Recover(lastErr);
                        continue;
                    }
                }

                if (_sdk.Release(_cam, _fullPressMode) == XSDK_COMPLETE)
                {
                    halfPressed = false;
                    accepted = true;
                    break;
                }

                (_, lastErr, diagnostic) = _sdk.LastError(_cam);
                _log($"[WARN] Fujifilm release attempt {attempt} failed: {diagnostic}");
                if (!IsRetryable(lastErr)) break;
                Recover(lastErr);

                // Vẫn lỗi sau hai lần: nhả nửa cò để lần sau máy lấy nét lại từ đầu
                if (halfPressed && attempt % 2 == 0 && lastErr is XSDK_ERRCODE_SHOOT_ERROR or XSDK_ERRCODE_AF_TIMEOUT)
                {
                    _sdk.Release(_cam, XSDK_RELEASE_N_S1OFF);
                    halfPressed = false;
                    Thread.Sleep(150);
                }
            }

            bool mayHaveFired = lastErr == XSDK_ERRCODE_COMMUNICATION || lastErr == XSDK_ERRCODE_TIMEOUT;
            if (!accepted && !mayHaveFired)
            {
                return Fail(ErrorCode.CameraShutterFailed, ReleaseErrorMessage(lastErr, diagnostic));
            }

            return WaitForJpeg(path, accepted ? 12000 : 4000);
        }
        finally
        {
            if (halfPressed)
            {
                _sdk.Release(_cam, XSDK_RELEASE_N_S1OFF);
                Thread.Sleep(200); // cho máy nhả cò xong, tránh live view báo bận
            }
            UpdateInfo(i => { if (i.Connected) i.Status = _liveViewWanted ? "liveview" : "ready"; });
            if (_cam != IntPtr.Zero)
            {
                _nextFrame = DateTime.UtcNow + TimeSpan.FromMilliseconds(200);
                StartLiveView();
            }
        }
    }

    private CapturePhotoResult WaitForJpeg(string path, int timeoutMs)
    {
        bool sawNonJpeg = false;
        var until = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < until)
        {
            bool removed = false;
            var info = new XSDK_ImageInformation();
            if (_sdk!.ReadImageInfo(_cam, ref info) == XSDK_COMPLETE)
            {
                int format = info.lFormat & 0xFF;
                if (format == XSDK_IMAGEFORMAT_JPEG && info.lDataSize > 0)
                {
                    var data = _sdk.ReadImage(_cam, info.lDataSize);
                    if (data != null)
                    {
                        _sdk.DeleteImage(_cam);
                        string part = path + ".part";
                        File.WriteAllBytes(part, data);
                        File.Move(part, path, overwrite: true);
                        _log($"[INFO] Fujifilm photo saved: {path} ({data.Length} bytes)");
                        return new CapturePhotoResult { IsSuccess = true, Code = ErrorCode.None, RawImagePath = path };
                    }
                }
                else if (format != XSDK_IMAGEFORMAT_NONE)
                {
                    if (format == XSDK_IMAGEFORMAT_RAW || format == XSDK_IMAGEFORMAT_HEIF) sawNonJpeg = true;
                    removed = _sdk.DeleteImage(_cam) == XSDK_COMPLETE;
                }
            }
            Thread.Sleep(removed ? 5 : 25);
        }

        string msg = sawNonJpeg
            ? "Máy Fujifilm chỉ trả ảnh RAW/HEIF. Hãy đặt chất lượng ảnh là JPEG (hoặc RAW+JPEG)."
            : "Máy Fujifilm không trả ảnh về (hết thời gian chờ).";
        _log($"[WARN] Fujifilm capture: {msg}");
        return Fail(ErrorCode.CameraCaptureTimeout, msg);
    }

    private static bool IsRetryable(int err) => err is XSDK_ERRCODE_BUSY or XSDK_ERRCODE_AF_TIMEOUT
        or XSDK_ERRCODE_SHOOT_ERROR or XSDK_ERRCODE_FRAME_FULL or XSDK_ERRCODE_FORCEMODE_BUSY
        or XSDK_ERRCODE_RUNNING_OTHER_FUNCTION;

    private void Recover(int err)
    {
        if (err == XSDK_ERRCODE_FRAME_FULL || err == XSDK_ERRCODE_RUNNING_OTHER_FUNCTION) DrainBuffer(1500);
        Thread.Sleep(180);
    }

    private static string ReleaseErrorMessage(int err, string diagnostic) => err switch
    {
        XSDK_ERRCODE_AF_TIMEOUT => "Máy Fujifilm không lấy nét được. Kiểm tra ánh sáng / khoảng cách hoặc chuyển lấy nét tay.",
        XSDK_ERRCODE_BUSY or XSDK_ERRCODE_FORCEMODE_BUSY or XSDK_ERRCODE_RUNNING_OTHER_FUNCTION => "Máy Fujifilm đang bận, thử lại sau giây lát.",
        XSDK_ERRCODE_SHOOT_ERROR => "Máy Fujifilm chưa chụp được (thường do chưa lấy nét được). Thử lại, hoặc chuyển cần lấy nét trên máy sang M / bật ƯU TIÊN NHẢ CỬA TRẬP.",
        XSDK_ERRCODE_FRAME_FULL => "Bộ đệm máy Fujifilm đang đầy, thử lại sau giây lát.",
        _ => $"Máy Fujifilm từ chối lệnh chụp ({diagnostic}).",
    };

    // ------------------------------------------------------------------ Tiện ích

    private void UpdateInfo(Action<CameraInfo> mutate)
    {
        CameraInfo before, after;
        lock (_infoLock)
        {
            before = _info.Clone();
            mutate(_info);
            after = _info.Clone();
        }
        if (before.SameAs(after)) return;

        InfoChanged?.Invoke(this, after);
        StatusChanged?.Invoke(this, new CameraStatusChangedEventArgs
        {
            NewStatus = GetStatus(),
            UserMessage = after.Message,
            DiagnosticMessage = after.Model,
        });
    }

    private static CapturePhotoResult Fail(ErrorCode code, string message) => new()
    {
        IsSuccess = false,
        Code = code,
        UserMessage = message,
        DiagnosticMessage = message,
    };
}
