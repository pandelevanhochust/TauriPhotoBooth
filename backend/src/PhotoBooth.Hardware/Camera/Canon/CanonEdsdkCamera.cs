using System;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using PhotoBooth.Core.Interfaces;
using PhotoBooth.Core.Models;
using static PhotoBooth.Hardware.Camera.Canon.EdsdkNative;

namespace PhotoBooth.Hardware.Camera.Canon;

/// <summary>
/// Máy ảnh Canon EOS qua EDSDK (đã thử định hướng cho EOS R50).
/// Mọi lời gọi EDSDK chạy trên một luồng STA riêng có bơm message Win32, vì EDSDK gửi sự kiện
/// (ảnh chụp xong, máy tắt...) qua hàng đợi message của luồng đã khởi tạo SDK.
/// Không có thẻ nhớ: ảnh tải thẳng về máy tính (SaveTo = Host + dung lượng "ảo"), giống app FavoriCurrent
/// đang chạy thật với EOS R10. Có thẻ nhớ: lưu "cả hai", vì R50 có thẻ không gửi ảnh về khi chỉ lưu Host.
/// Máy rút ra / cắm lại sẽ được tự kết nối lại sau khoảng 2 giây.
/// </summary>
public sealed class CanonEdsdkCamera : ITetheredCamera
{
    private static readonly TimeSpan ReconnectInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan EvfInterval = TimeSpan.FromMilliseconds(33);
    /// <summary>Giữ nửa cò tối đa bấy lâu sau khi lấy nét trước mà không có lệnh chụp.</summary>
    private static readonly TimeSpan PrefocusHold = TimeSpan.FromSeconds(4);
    /// <summary>Nhấn nửa cò bấy lâu để lấy nét, nhả, chờ <see cref="FocusSettleMs"/> rồi mới chụp (theo FavoriCurrent).</summary>
    private const int FocusHalfPressMs = 150;
    private const int FocusSettleMs = 200;
    private static readonly TimeSpan KeepAliveInterval = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan CaptureTimeout = TimeSpan.FromSeconds(15);

    private readonly Action<string> _log;
    private readonly BlockingCollection<Action> _queue = new();
    private readonly object _infoLock = new();
    private CameraInfo _info = new() { Vendor = "Canon", Message = "Chưa khởi động" };

    // Giữ tham chiếu delegate để GC không thu hồi khi EDSDK còn giữ con trỏ hàm
    private readonly EdsObjectEventHandler _objectHandler;
    private readonly EdsStateEventHandler _stateHandler;

    private Thread? _thread;
    private int _workerThreadId;
    private volatile bool _stopping;

    // Các trường dưới đây chỉ được đụng tới trên luồng EDSDK
    private bool _sdkInitialized;
    private IntPtr _cam = IntPtr.Zero;
    private bool _liveViewWanted;
    private bool _liveViewActive;
    private bool _capturing;
    /// <summary>Host khi máy không có thẻ (như FavoriCurrent); Both khi có thẻ, vì R50 có thẻ không gửi ảnh về nếu chỉ lưu Host.</summary>
    private uint _saveTo = SaveTo_Host;
    /// <summary>Bật sau một tấm không về: dùng lại kiểu nhấn nút đã chạy ổn thay cho lệnh TakePicture.</summary>
    private bool _legacyShutter;
    private bool _pressingShutter;
    private bool _disconnectRequested;
    private int _evfErrorStreak;
    private DateTime _nextReconnect = DateTime.MinValue;
    private DateTime _nextEvf = DateTime.MinValue;
    private DateTime _nextKeepAlive = DateTime.MinValue;
    private TaskCompletionSource<string>? _pendingShot;
    private string? _pendingShotPath;
    private bool _pendingShotSawNonJpeg;
    private DateTime? _halfPressedAt;
    private Task<CapturePhotoResult>? _lastDownload;

    public event EventHandler<FrameLeaseEventArgs>? FrameArrived;
    public event EventHandler<CameraStatusChangedEventArgs>? StatusChanged;

    public event EventHandler<CameraInfo>? InfoChanged;

    public string Vendor => "Canon";

    public bool AutoReconnect { get; set; } = true;

    public CanonEdsdkCamera(Action<string>? log = null)
    {
        _log = log ?? (_ => { });
        _objectHandler = OnObjectEvent;
        _stateHandler = OnStateEvent;
    }

    // ------------------------------------------------------------------ API công khai

    public Task<OperationResult> InitializeAsync(CancellationToken cancellationToken = default)
    {
        EnsureThread();
        return Invoke(() =>
        {
            if (!EnsureSdk())
            {
                return OperationResult.Fail(ErrorCode.CameraSdkNotInstalled, _info.Message);
            }
            TryConnect();
            return _cam != IntPtr.Zero
                ? OperationResult.Ok()
                : OperationResult.Fail(ErrorCode.CameraNotConnected, GetInfo().Message);
        });
    }

    /// <summary>Ngắt rồi kết nối lại (nút "Kiểm tra lại" trong admin).</summary>
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
            return EnableLiveView()
                ? OperationResult.Ok()
                : OperationResult.Fail(ErrorCode.CameraLiveViewTimeout, "Không bật được live view");
        });
    }

    public Task<OperationResult> StopLiveViewAsync(CancellationToken cancellationToken = default)
    {
        EnsureThread();
        return Invoke(() =>
        {
            _liveViewWanted = false;
            DisableLiveView();
            return OperationResult.Ok();
        });
    }

    public Task<OperationResult> KeepAliveAsync(CancellationToken cancellationToken = default)
    {
        EnsureThread();
        return Invoke(() =>
        {
            if (_cam == IntPtr.Zero) return OperationResult.Fail(ErrorCode.CameraNotConnected, "Chưa kết nối máy ảnh");
            uint err = EdsSendCommand(_cam, CameraCommand_ExtendShutDownTimer, 0);
            return err == EDS_ERR_OK ? OperationResult.Ok() : OperationResult.Fail(ErrorCode.CameraBusy, Hex(err));
        });
    }

    /// <summary>Lấy nét, chụp và tải ảnh JPEG về <paramref name="destinationDirectory"/>.</summary>
    public async Task<CapturePhotoResult> CapturePhotoAsync(string destinationDirectory, CancellationToken cancellationToken = default)
    {
        var ticket = await BeginShotAsync(destinationDirectory, cancellationToken);
        if (ticket.Completion != null) return await ticket.Completion;
        return Fail(ticket.Shutter.Code, ticket.Shutter.UserMessage);
    }

    /// <summary>Nhả màn trập rồi trả về ngay; ảnh tải về ở chế độ nền (Completion).</summary>
    public async Task<ShotTicket> BeginShotAsync(string destinationDirectory, CancellationToken cancellationToken = default)
    {
        EnsureThread();
        Directory.CreateDirectory(destinationDirectory);

        // Tấm trước còn đang tải về: chờ một chút thay vì báo bận ngay
        var previous = _lastDownload;
        if (previous != null && !previous.IsCompleted)
        {
            await Task.WhenAny(previous, Task.Delay(TimeSpan.FromSeconds(4), cancellationToken));
        }

        var start = await Invoke(() => BeginCapture(destinationDirectory));
        if (start.Error != null) return new ShotTicket(start.Error, null);

        var download = AwaitDownloadAsync(start.Task!, cancellationToken);
        _lastDownload = download;
        return new ShotTicket(OperationResult.Ok(), download);
    }

    /// <summary>Nhấn nửa cò để lấy nét trước; tấm chụp trong vài giây tới sẽ nhả màn trập ngay.</summary>
    public Task PrefocusAsync()
    {
        EnsureThread();
        return Invoke(() =>
        {
            if (_cam == IntPtr.Zero || _pendingShot != null || _halfPressedAt != null) return false;
            if (EdsSendCommand(_cam, CameraCommand_PressShutterButton, ShutterButton_Halfway) == EDS_ERR_OK)
            {
                _halfPressedAt = DateTime.UtcNow;
            }
            return true;
        });
    }

    private async Task<CapturePhotoResult> AwaitDownloadAsync(Task<string> shotTask, CancellationToken cancellationToken)
    {
        var finished = await Task.WhenAny(shotTask, Task.Delay(CaptureTimeout, cancellationToken));
        if (finished != shotTask)
        {
            bool sawNonJpeg = await Invoke(() =>
            {
                bool raw = _pendingShotSawNonJpeg;
                ClearPendingShot();
                if (!raw) UseProvenCaptureSettings();
                return raw;
            });
            string msg = sawNonJpeg
                ? "Máy ảnh đang chụp RAW. Hãy chỉnh chất lượng ảnh sang JPEG (hoặc RAW+JPEG)."
                : "Máy ảnh không trả ảnh về (hết thời gian chờ).";
            _log($"[WARN] Canon capture timeout. {msg}");
            return Fail(ErrorCode.CameraCaptureTimeout, msg);
        }

        try
        {
            string path = await shotTask;
            return new CapturePhotoResult { IsSuccess = true, Code = ErrorCode.None, RawImagePath = path };
        }
        catch (Exception ex)
        {
            return Fail(ErrorCode.CameraTransferFailed, ex.Message);
        }
    }

    public void Dispose()
    {
        if (_thread == null || _stopping) return;
        _stopping = true;
        _queue.Add(() => { });
        if (!_thread.Join(TimeSpan.FromSeconds(5)))
        {
            _log("[WARN] Canon EDSDK thread did not stop in time");
        }
    }

    // ------------------------------------------------------------------ Luồng EDSDK

    private void EnsureThread()
    {
        if (_thread != null) return;
        lock (_queue)
        {
            if (_thread != null) return;
            _thread = new Thread(Run) { IsBackground = true, Name = "CanonEdsdk" };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
        }
    }

    private Task<T> Invoke<T>(Func<T> func)
    {
        if (Environment.CurrentManagedThreadId == _workerThreadId)
        {
            return Task.FromResult(func());
        }
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (_stopping)
        {
            tcs.SetException(new ObjectDisposedException(nameof(CanonEdsdkCamera)));
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
            PumpMessages();
            RunSafe(Tick);
        }

        RunSafe(() =>
        {
            DisableLiveView();
            Disconnect("Đã dừng");
            if (_sdkInitialized)
            {
                EdsTerminateSDK();
                _sdkInitialized = false;
            }
        });

        // Trả lỗi cho các lời gọi còn đang chờ
        while (_queue.TryTake(out var pending)) RunSafe(pending);
    }

    private void RunSafe(Action action)
    {
        try { action(); }
        catch (Exception ex) { _log($"[ERROR] Canon EDSDK: {ex}"); }
    }

    private void PumpMessages()
    {
        while (PeekMessage(out var msg, IntPtr.Zero, 0, 0, PM_REMOVE))
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }
        if (_sdkInitialized) EdsGetEvent();
    }

    /// <summary>Chờ trên luồng EDSDK mà vẫn nhận sự kiện của máy ảnh.</summary>
    private void PumpFor(int milliseconds)
    {
        var until = DateTime.UtcNow.AddMilliseconds(milliseconds);
        while (DateTime.UtcNow < until)
        {
            PumpMessages();
            Thread.Sleep(10);
        }
    }

    private void Tick()
    {
        if (!_sdkInitialized) return;
        var now = DateTime.UtcNow;

        if (_disconnectRequested)
        {
            _disconnectRequested = false;
            Disconnect("Máy ảnh đã tắt hoặc bị rút dây. Đang chờ kết nối lại...");
        }

        if (_cam == IntPtr.Zero)
        {
            if (AutoReconnect && now >= _nextReconnect)
            {
                _nextReconnect = now + ReconnectInterval;
                TryConnect();
            }
            return;
        }

        if (_liveViewWanted && !_liveViewActive && !_capturing && now >= _nextEvf)
        {
            _nextEvf = now + TimeSpan.FromSeconds(1);
            EnableLiveView();
        }
        else if (_liveViewActive && !_capturing && now >= _nextEvf)
        {
            _nextEvf = now + EvfInterval;
            DownloadEvfFrame();
        }

        // Đã lấy nét trước mà không có lệnh chụp: nhả nửa cò
        if (_halfPressedAt is { } pressedAt && !_capturing && now - pressedAt > PrefocusHold)
        {
            _halfPressedAt = null;
            EdsSendCommand(_cam, CameraCommand_PressShutterButton, ShutterButton_OFF);
        }

        if (now >= _nextKeepAlive)
        {
            _nextKeepAlive = now + KeepAliveInterval;
            EdsSendCommand(_cam, CameraCommand_ExtendShutDownTimer, 0);
        }
    }

    // ------------------------------------------------------------------ Kết nối

    private bool EnsureSdk()
    {
        if (_sdkInitialized) return true;
        try
        {
            uint err = EdsInitializeSDK();
            if (err != EDS_ERR_OK)
            {
                UpdateInfo(i => { i.SdkAvailable = false; i.Status = "error"; i.Message = $"Khởi tạo Canon EDSDK thất bại ({Hex(err)})"; });
                return false;
            }
            _sdkInitialized = true;
            UpdateInfo(i => { i.SdkAvailable = true; i.Message = "Đang tìm máy ảnh..."; });
            _log("[INFO] Canon EDSDK initialized");
            return true;
        }
        catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException)
        {
            UpdateInfo(i =>
            {
                i.SdkAvailable = false;
                i.Status = "error";
                i.Message = "Không tìm thấy Canon EDSDK (EDSDK.dll 64-bit) cạnh file chạy của app.";
            });
            _log($"[ERROR] Canon EDSDK not available: {ex.Message}");
            return false;
        }
    }

    private void TryConnect()
    {
        if (_cam != IntPtr.Zero || !_sdkInitialized) return;

        uint err = EdsGetCameraList(out IntPtr list);
        if (err != EDS_ERR_OK)
        {
            SetDisconnected($"Không đọc được danh sách máy ảnh ({Hex(err)})");
            return;
        }

        try
        {
            EdsGetChildCount(list, out int count);
            if (count <= 0)
            {
                SetDisconnected("Không tìm thấy máy ảnh Canon. Kiểm tra dây USB, bật nguồn máy và tắt EOS Utility / EOS Webcam Utility.");
                return;
            }

            err = EdsGetChildAtIndex(list, 0, out IntPtr cam);
            if (err != EDS_ERR_OK || cam == IntPtr.Zero)
            {
                SetDisconnected($"Không truy cập được máy ảnh ({Hex(err)})");
                return;
            }

            EdsGetDeviceInfo(cam, out var deviceInfo);

            err = EdsOpenSession(cam);
            if (err != EDS_ERR_OK)
            {
                EdsRelease(cam);
                SetDisconnected($"Không mở được phiên làm việc với máy ảnh ({Hex(err)}). Hãy tắt EOS Utility hoặc ứng dụng khác đang dùng máy rồi thử lại.");
                return;
            }

            _cam = cam;
            EdsSetObjectEventHandler(_cam, ObjectEvent_All, _objectHandler, IntPtr.Zero);
            EdsSetCameraStateEventHandler(_cam, StateEvent_All, _stateHandler, IntPtr.Zero);

            // Không thẻ: gửi thẳng về máy tính. Có thẻ: lưu cả hai. Báo dung lượng "ảo" để máy cho chụp khi không có thẻ
            bool hasCard = HasMemoryCard();
            _saveTo = hasCard ? SaveTo_Both : SaveTo_Host;
            _legacyShutter = false;
            ApplySaveTo(_saveTo);
            _log($"[INFO] Canon memory card={(hasCard ? "yes" : "no")} -> saveTo={_saveTo}");

            string model = ReadStringProperty(PropID_ProductName) ?? deviceInfo.szDeviceDescription ?? "Canon";
            int? battery = ReadBattery();

            _liveViewActive = false;
            _evfErrorStreak = 0;
            _nextEvf = DateTime.UtcNow + TimeSpan.FromMilliseconds(500);
            _nextKeepAlive = DateTime.UtcNow + KeepAliveInterval;

            UpdateInfo(i =>
            {
                i.Connected = true;
                i.Model = model;
                i.Port = deviceInfo.szPortName ?? string.Empty;
                i.BatteryLevel = battery;
                i.Status = "ready";
                i.LiveView = false;
                i.Message = "Đã kết nối";
            });
            _log($"[INFO] Canon camera connected: {model} ({deviceInfo.szPortName})");
        }
        finally
        {
            EdsRelease(list);
        }
    }

    private void Disconnect(string message)
    {
        if (_cam != IntPtr.Zero)
        {
            try
            {
                if (_liveViewActive)
                {
                    uint dev = EvfOutputDevice_TFT;
                    EdsSetPropertyData(_cam, PropID_Evf_OutputDevice, 0, sizeof(uint), ref dev);
                }
                EdsSetObjectEventHandler(_cam, ObjectEvent_All, null, IntPtr.Zero);
                EdsSetCameraStateEventHandler(_cam, StateEvent_All, null, IntPtr.Zero);
                EdsCloseSession(_cam);
            }
            catch (Exception ex)
            {
                _log($"[WARN] Canon close session: {ex.Message}");
            }
            EdsRelease(_cam);
            _cam = IntPtr.Zero;
            _log($"[INFO] Canon camera disconnected: {message}");
        }

        _liveViewActive = false;
        _capturing = false;
        _halfPressedAt = null;
        _pendingShot?.TrySetException(new IOException("Mất kết nối máy ảnh trong lúc chụp"));
        ClearPendingShot();
        _nextReconnect = DateTime.UtcNow + ReconnectInterval;
        SetDisconnected(message);
    }

    private void SetDisconnected(string message)
    {
        UpdateInfo(i =>
        {
            i.Connected = false;
            i.LiveView = false;
            i.Status = "disconnected";
            i.Message = message;
            i.BatteryLevel = null;
        });
    }

    // ------------------------------------------------------------------ Live view

    private bool EnableLiveView()
    {
        if (_cam == IntPtr.Zero) return false;
        if (_liveViewActive) return true;

        uint mode = 1;
        EdsSetPropertyData(_cam, PropID_Evf_Mode, 0, sizeof(uint), ref mode);

        uint dev = EvfOutputDevice_PC;
        uint err = EdsSetPropertyData(_cam, PropID_Evf_OutputDevice, 0, sizeof(uint), ref dev);
        if (err != EDS_ERR_OK)
        {
            _log($"[WARN] Canon enable live view failed {Hex(err)}");
            return false;
        }

        _liveViewActive = true;
        _evfErrorStreak = 0;
        _nextEvf = DateTime.UtcNow + TimeSpan.FromMilliseconds(300);
        UpdateInfo(i => { i.LiveView = true; i.Status = "liveview"; });
        return true;
    }

    private void DisableLiveView()
    {
        if (!_liveViewActive) return;
        _liveViewActive = false;
        if (_cam != IntPtr.Zero)
        {
            uint dev = EvfOutputDevice_TFT;
            EdsSetPropertyData(_cam, PropID_Evf_OutputDevice, 0, sizeof(uint), ref dev);
        }
        UpdateInfo(i => { i.LiveView = false; if (i.Connected) i.Status = "ready"; });
    }

    private void DownloadEvfFrame()
    {
        IntPtr stream = IntPtr.Zero, evf = IntPtr.Zero;
        try
        {
            if (EdsCreateMemoryStream(0, out stream) != EDS_ERR_OK) return;
            if (EdsCreateEvfImageRef(stream, out evf) != EDS_ERR_OK) return;

            uint err = EdsDownloadEvfImage(_cam, evf);
            if (err == EDS_ERR_OBJECT_NOTREADY || err == EDS_ERR_DEVICE_BUSY) return;
            if (err != EDS_ERR_OK)
            {
                if (++_evfErrorStreak == 25)
                {
                    _log($"[WARN] Canon live view keeps failing ({Hex(err)}), re-enabling");
                    _liveViewActive = false; // Tick sẽ bật lại
                }
                if (err == EDS_ERR_COMM_DISCONNECTED || err == EDS_ERR_SESSION_NOT_OPEN) _disconnectRequested = true;
                return;
            }
            _evfErrorStreak = 0;

            EdsGetPointer(stream, out IntPtr ptr);
            EdsGetLength(stream, out ulong len);
            if (ptr == IntPtr.Zero || len == 0 || len > int.MaxValue) return;

            var bytes = new byte[(int)len];
            Marshal.Copy(ptr, bytes, 0, bytes.Length);

            var handler = FrameArrived;
            if (handler != null)
            {
                using var lease = new FrameLeaseEventArgs(bytes, 0, 0, () => { });
                handler(this, lease);
            }
        }
        finally
        {
            if (evf != IntPtr.Zero) EdsRelease(evf);
            if (stream != IntPtr.Zero) EdsRelease(stream);
        }
    }

    // ------------------------------------------------------------------ Chụp ảnh

    private sealed record CaptureStart(Task<string>? Task, CapturePhotoResult? Error);

    private CaptureStart BeginCapture(string destinationDirectory)
    {
        if (_cam == IntPtr.Zero)
        {
            return new CaptureStart(null, Fail(ErrorCode.CameraNotConnected, GetInfo().Message is { Length: > 0 } m ? m : "Chưa kết nối máy ảnh"));
        }
        if (_pendingShot != null)
        {
            return new CaptureStart(null, Fail(ErrorCode.CameraBusy, "Máy ảnh đang chụp tấm trước"));
        }

        _pendingShot = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingShotPath = Path.Combine(destinationDirectory, $"dslr_{DateTime.Now:yyyyMMdd_HHmmss_fff}.jpg");
        _pendingShotSawNonJpeg = false;
        _capturing = true;
        UpdateInfo(i => i.Status = "capturing");

        // Đặt lại trước mỗi tấm: máy có thể tự đổi nơi lưu sau khi bật live view / đổi chế độ
        ApplySaveTo(_saveTo);
        uint err = PressShutter();
        if (err == EDS_ERR_TAKE_PICTURE_NO_CARD && _saveTo == SaveTo_Both)
        {
            _log("[INFO] Canon: không có thẻ nhớ, chuyển sang lưu thẳng về máy tính");
            _saveTo = SaveTo_Host;
            ApplySaveTo(_saveTo);
            PumpFor(300);
            err = PressShutter();
        }

        if (err != EDS_ERR_OK)
        {
            ClearPendingShot();
            string msg = ShutterErrorMessage(err);
            _log($"[WARN] Canon shutter failed {Hex(err)}");
            return new CaptureStart(null, Fail(ErrorCode.CameraShutterFailed, msg));
        }

        // Màn trập đã nhả: cho live view chạy lại ngay, ảnh tải về qua sự kiện của máy
        var pending = _pendingShot.Task;
        _capturing = false;
        _nextEvf = DateTime.UtcNow + TimeSpan.FromMilliseconds(120);
        return new CaptureStart(pending, null);
    }

    /// <summary>Lý do máy ảnh từ chối lệnh chụp (mã EDS_ERR_TAKE_PICTURE_* trong EDSDKErrors.h).</summary>
    private static string ShutterErrorMessage(uint err) => err switch
    {
        EDS_ERR_DEVICE_BUSY => "Máy ảnh đang bận, thử lại sau giây lát.",
        0x00008D01 => "Máy ảnh không lấy nét được.",
        0x00008D03 => "Máy ảnh đang lật gương / bận, thử lại.",
        0x00008D04 => "Máy ảnh đang làm sạch cảm biến, chờ một chút.",
        0x00008D06 => "Máy ảnh báo không có thẻ nhớ.",
        0x00008D07 => "Máy ảnh báo lỗi thẻ nhớ.",
        0x00008D08 => "Thẻ nhớ đang bị khoá ghi.",
        0x00008D0A => "Đèn flash đang sạc, chờ một chút.",
        0x00008D0B => "Máy ảnh chưa gắn ống kính.",
        0x00008D0C or 0x00008D0E => "Máy ảnh đang ở chế độ quay phim. Hãy vặn sang chế độ chụp ảnh (P / Av / M).",
        0x00008D0D => "Chế độ chụp hiện tại không cho chụp khi đang xem trực tiếp. Hãy vặn sang P / Av / M.",
        0x00008D0F => "Ống kính đang thu gọn. Hãy xoay vòng zoom để mở ống kính ra.",
        _ => $"Máy ảnh từ chối lệnh chụp ({Hex(err)}).",
    };

    private void ClearPendingShot()
    {
        _pendingShot = null;
        _pendingShotPath = null;
        _capturing = false;
        _nextEvf = DateTime.UtcNow + TimeSpan.FromMilliseconds(200);
        UpdateInfo(i => { if (i.Connected) i.Status = i.LiveView ? "liveview" : "ready"; });
    }

    /// <summary>
    /// Trình tự chụp của FavoriCurrent: nhấn nửa cò 150 ms để lấy nét, nhả, chờ 200 ms rồi gửi lệnh TakePicture.
    /// Máy báo lấy nét trượt thì chụp lại ở chế độ không chờ AF, để tấm ảnh vẫn có và không bị treo.
    /// </summary>
    private uint PressShutter()
    {
        // Lỗi trong lúc nhấn nút đã có mã trả về của lệnh: không để sự kiện CaptureError huỷ tấm đang chờ
        _pressingShutter = true;
        try
        {
            // Đã lấy nét trước (PrefocusAsync đang giữ nửa cò) thì không cần nhấn nửa lần nữa
            bool prefocused = _halfPressedAt is { } at && DateTime.UtcNow - at <= PrefocusHold;
            if (!prefocused)
            {
                EdsSendCommand(_cam, CameraCommand_PressShutterButton, ShutterButton_Halfway);
                PumpFor(FocusHalfPressMs);
            }
            EdsSendCommand(_cam, CameraCommand_PressShutterButton, ShutterButton_OFF);
            _halfPressedAt = null;
            PumpFor(FocusSettleMs);

            uint err = _legacyShutter ? EDS_ERR_TAKE_PICTURE_AF_NG : EdsSendCommand(_cam, CameraCommand_TakePicture, 0);
            if (_legacyShutter)
            {
                // Kiểu cũ: nhấn nửa cò lâu hơn để kịp lấy nét rồi chụp không chờ AF
                EdsSendCommand(_cam, CameraCommand_PressShutterButton, ShutterButton_Halfway);
                PumpFor(350);
                err = EdsSendCommand(_cam, CameraCommand_PressShutterButton, ShutterButton_Completely_NonAF);
                PumpFor(150);
                EdsSendCommand(_cam, CameraCommand_PressShutterButton, ShutterButton_OFF);
            }
            else if (err == EDS_ERR_TAKE_PICTURE_AF_NG)
            {
                _log("[INFO] Canon: lấy nét trượt, chụp lại không chờ AF");
                err = EdsSendCommand(_cam, CameraCommand_PressShutterButton, ShutterButton_Completely_NonAF);
                PumpFor(150);
                EdsSendCommand(_cam, CameraCommand_PressShutterButton, ShutterButton_OFF);
            }
            _log($"[INFO] Canon shutter saveTo={_saveTo} legacy={_legacyShutter} prefocused={prefocused} -> {Hex(err)}");
            return err;
        }
        finally
        {
            _pressingShutter = false;
        }
    }

    /// <summary>Máy vẫn liệt kê khe thẻ khi khe trống, nên phải xem từng khe có thẻ thật (StorageType khác 0) hay không.</summary>
    private bool HasMemoryCard()
    {
        if (EdsGetChildCount(_cam, out int volumes) != EDS_ERR_OK) return false;
        bool found = false;
        for (int i = 0; i < volumes; i++)
        {
            if (EdsGetChildAtIndex(_cam, i, out IntPtr volume) != EDS_ERR_OK || volume == IntPtr.Zero) continue;
            uint err = EdsGetVolumeInfo(volume, out var info);
            _log($"[INFO] Canon volume {i}: {Hex(err)} storageType={info.StorageType} access={info.Access} max={info.MaxCapacity} label={info.szVolumeLabel}");
            if (err == EDS_ERR_OK && info.StorageType != 0) found = true;
            EdsRelease(volume);
        }
        return found;
    }

    /// <summary>
    /// Một tấm đã chụp mà ảnh không về: quay lại cấu hình đã chạy ổn trên R50 (lưu cả hai + nhấn nút kiểu cũ)
    /// cho tới lần kết nối sau, để các tấm tiếp theo không hỏng theo.
    /// </summary>
    private void UseProvenCaptureSettings()
    {
        if (_saveTo == SaveTo_Both && _legacyShutter) return;
        _log($"[WARN] Canon: ảnh không về với saveTo={_saveTo} legacy={_legacyShutter}; chuyển sang saveTo={SaveTo_Both} legacy=True");
        _saveTo = SaveTo_Both;
        _legacyShutter = true;
        if (_cam != IntPtr.Zero) ApplySaveTo(_saveTo);
    }

    /// <summary>Đặt nơi lưu ảnh và báo dung lượng "ảo" (4096 byte/sector, số cluster tối đa) để chụp được khi không có thẻ.</summary>
    private void ApplySaveTo(uint saveTo)
    {
        if (EdsGetPropertyData(_cam, PropID_SaveTo, 0, sizeof(uint), out uint current) != EDS_ERR_OK || current != saveTo)
        {
            uint err = EdsSetPropertyData(_cam, PropID_SaveTo, 0, sizeof(uint), ref saveTo);
            if (err != EDS_ERR_OK) _log($"[WARN] Canon SaveTo={saveTo} failed {Hex(err)}");
        }
        uint capErr = EdsSetCapacity(_cam, new EdsCapacity { NumberOfFreeClusters = 0x7FFFFFFF, BytesPerSector = 0x1000, Reset = 1 });
        if (capErr != EDS_ERR_OK) _log($"[WARN] Canon SetCapacity failed {Hex(capErr)}");
    }

    private uint OnObjectEvent(uint inEvent, IntPtr inRef, IntPtr inContext)
    {
        try
        {
            // Ghi lại sự kiện trong lúc chờ ảnh để lần sau ảnh không về thì biết máy đã báo gì
            if (_pendingShot != null) _log($"[INFO] Canon object event {Hex(inEvent)} while waiting for photo");
            if (inEvent == ObjectEvent_DirItemRequestTransfer && inRef != IntPtr.Zero)
            {
                HandleTransfer(inRef, fromCard: false);
                return EDS_ERR_OK;
            }
            // R50: ảnh được ghi vào thẻ rồi báo "file mới" thay vì yêu cầu truyền thẳng → tải từ thẻ về
            if (inEvent == ObjectEvent_DirItemCreated && inRef != IntPtr.Zero && _pendingShot != null)
            {
                HandleTransfer(inRef, fromCard: true);
                return EDS_ERR_OK;
            }
        }
        catch (Exception ex)
        {
            _log($"[ERROR] Canon object event: {ex}");
        }

        if (inRef != IntPtr.Zero) EdsRelease(inRef);
        return EDS_ERR_OK;
    }

    private void HandleTransfer(IntPtr item, bool fromCard)
    {
        IntPtr stream = IntPtr.Zero;
        var tcs = _pendingShot;
        string? path = _pendingShotPath;
        try
        {
            uint err = EdsGetDirectoryItemInfo(item, out var info);
            string name = err == EDS_ERR_OK ? info.szFileName ?? string.Empty : string.Empty;
            bool isJpeg = name.EndsWith(".JPG", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith(".JPEG", StringComparison.OrdinalIgnoreCase);

            if (tcs == null || path == null || !isJpeg || (err == EDS_ERR_OK && info.isFolder != 0))
            {
                // Không có lệnh chụp đang chờ (bấm nút trên máy), thư mục mới hoặc file RAW: bỏ qua
                if (tcs != null && !isJpeg && info.isFolder == 0) _pendingShotSawNonJpeg = true;
                if (!fromCard) EdsDownloadCancel(item);
                return;
            }

            string partPath = path + ".part";
            err = EdsCreateFileStreamEx(partPath, FileCreateDisposition_CreateAlways, Access_ReadWrite, out stream);
            if (err != EDS_ERR_OK) throw new IOException($"Không tạo được file ảnh ({Hex(err)})");

            err = EdsDownload(item, info.size, stream);
            if (err != EDS_ERR_OK)
            {
                if (!fromCard) EdsDownloadCancel(item);
                throw new IOException($"Tải ảnh từ máy ảnh thất bại ({Hex(err)})");
            }
            EdsDownloadComplete(item);
            EdsRelease(stream);
            stream = IntPtr.Zero;

            File.Move(partPath, path, overwrite: true);
            _log($"[INFO] Canon photo downloaded{(fromCard ? " from card" : "")}: {name} -> {path} ({info.size} bytes)");
            ClearPendingShot();
            tcs.TrySetResult(path);
        }
        catch (Exception ex)
        {
            ClearPendingShot();
            tcs?.TrySetException(ex);
        }
        finally
        {
            if (stream != IntPtr.Zero) EdsRelease(stream);
            EdsRelease(item);
        }
    }

    private uint OnStateEvent(uint inEvent, uint inParameter, IntPtr inContext)
    {
        if (_pendingShot != null) _log($"[INFO] Canon state event {Hex(inEvent)} param {Hex(inParameter)} while waiting for photo");
        switch (inEvent)
        {
            case StateEvent_Shutdown:
                // Không giải phóng máy ảnh ngay trong callback của chính nó: để Tick xử lý
                _disconnectRequested = true;
                break;
            case StateEvent_WillSoonShutDown:
                if (_cam != IntPtr.Zero) EdsSendCommand(_cam, CameraCommand_ExtendShutDownTimer, 0);
                break;
            case StateEvent_CaptureError:
                _log($"[WARN] Canon capture error event (param {inParameter})");
                if (_pressingShutter) break;
                _pendingShot?.TrySetException(new IOException("Máy ảnh báo lỗi khi chụp"));
                ClearPendingShot();
                break;
            case StateEvent_InternalError:
                _log($"[WARN] Canon internal error event (param {inParameter})");
                break;
        }
        return EDS_ERR_OK;
    }

    // ------------------------------------------------------------------ Tiện ích

    private string? ReadStringProperty(uint propId)
    {
        if (EdsGetPropertySize(_cam, propId, 0, out _, out int size) != EDS_ERR_OK || size <= 0) return null;
        IntPtr buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (EdsGetPropertyData(_cam, propId, 0, size, buffer) != EDS_ERR_OK) return null;
            return Marshal.PtrToStringAnsi(buffer);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private int? ReadBattery()
    {
        if (EdsGetPropertyData(_cam, PropID_BatteryLevel, 0, sizeof(uint), out uint level) != EDS_ERR_OK) return null;
        if (level == 0xFFFFFFFF) return -1;
        return level <= 100 ? (int)level : null;
    }

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
            DiagnosticMessage = $"{after.Model} {after.Port}".Trim(),
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
