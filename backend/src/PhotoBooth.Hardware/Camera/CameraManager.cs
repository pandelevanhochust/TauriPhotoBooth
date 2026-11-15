using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PhotoBooth.Core.Models;

namespace PhotoBooth.Hardware.Camera;

/// <summary>
/// Gộp các driver máy ảnh (Canon EDSDK, FUJIFILM X SDK...) thành một máy ảnh duy nhất cho host:
/// máy nào đang kết nối thì dùng máy đó (theo thứ tự ưu tiên truyền vào).
/// Khi đã có một máy, các driver còn lại ngừng dò USB để hai SDK không tranh nhau thiết bị.
/// </summary>
public sealed class CameraManager : ITetheredCamera
{
    private readonly IReadOnlyList<ITetheredCamera> _cameras;
    private readonly Action<string> _log;
    private readonly object _lock = new();
    private ITetheredCamera? _active;
    private bool _liveViewWanted;
    private CameraInfo _published = new() { Message = "Chưa khởi động" };

    public event EventHandler<FrameLeaseEventArgs>? FrameArrived;
    public event EventHandler<CameraStatusChangedEventArgs>? StatusChanged;
    public event EventHandler<CameraInfo>? InfoChanged;

    public CameraManager(Action<string>? log, params ITetheredCamera[] cameras)
    {
        _log = log ?? (_ => { });
        _cameras = cameras;
        foreach (var camera in _cameras)
        {
            camera.FrameArrived += (sender, e) =>
            {
                if (ReferenceEquals(sender, Volatile.Read(ref _active))) FrameArrived?.Invoke(this, e);
            };
            camera.InfoChanged += (_, _) => Recompute();
        }
    }

    public IReadOnlyList<ITetheredCamera> Cameras => _cameras;

    public string Vendor => Volatile.Read(ref _active)?.Vendor ?? string.Empty;

    public bool IsConnected => _cameras.Any(c => c.IsConnected);

    public bool AutoReconnect
    {
        get => _cameras.Any(c => c.AutoReconnect);
        set { foreach (var c in _cameras) c.AutoReconnect = value; }
    }

    public async Task<OperationResult> InitializeAsync(CancellationToken cancellationToken = default)
    {
        await Task.WhenAll(_cameras.Select(c => c.InitializeAsync(cancellationToken)));
        Recompute();
        return IsConnected ? OperationResult.Ok() : OperationResult.Fail(ErrorCode.CameraNotConnected, GetInfo().Message);
    }

    public async Task<CameraInfo> ReconnectAsync()
    {
        foreach (var c in _cameras) c.AutoReconnect = true;
        await Task.WhenAll(_cameras.Select(c => c.ReconnectAsync()));
        Recompute();
        return GetInfo();
    }

    public CameraInfo GetInfo()
    {
        var active = Volatile.Read(ref _active);
        if (active != null) return active.GetInfo();

        var infos = _cameras.Select(c => c.GetInfo()).ToList();
        bool anySdk = infos.Any(i => i.SdkAvailable);
        return new CameraInfo
        {
            Vendor = string.Empty,
            SdkAvailable = anySdk,
            Connected = false,
            Status = anySdk ? "disconnected" : "error",
            Message = string.Join("\n", infos.Select(i => $"{i.Vendor}: {i.Message}")),
        };
    }

    public CameraDeviceStatus GetStatus()
    {
        var active = Volatile.Read(ref _active);
        if (active != null) return active.GetStatus();
        return _cameras.Any(c => c.GetInfo().SdkAvailable) ? CameraDeviceStatus.Disconnected : CameraDeviceStatus.Error;
    }

    public Task<OperationResult> StartLiveViewAsync(CancellationToken cancellationToken = default)
    {
        ITetheredCamera? active;
        lock (_lock)
        {
            _liveViewWanted = true;
            active = _active;
        }
        return active?.StartLiveViewAsync(cancellationToken)
            ?? Task.FromResult(OperationResult.Fail(ErrorCode.CameraNotConnected, "Chưa kết nối máy ảnh"));
    }

    public async Task<OperationResult> StopLiveViewAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock) _liveViewWanted = false;
        await Task.WhenAll(_cameras.Select(c => c.StopLiveViewAsync(cancellationToken)));
        return OperationResult.Ok();
    }

    public Task<CapturePhotoResult> CapturePhotoAsync(string destinationDirectory, CancellationToken cancellationToken = default)
    {
        var active = Volatile.Read(ref _active);
        if (active != null) return active.CapturePhotoAsync(destinationDirectory, cancellationToken);
        return Task.FromResult(new CapturePhotoResult
        {
            IsSuccess = false,
            Code = ErrorCode.CameraNotConnected,
            UserMessage = "Chưa kết nối máy ảnh",
            DiagnosticMessage = "No tethered camera connected",
        });
    }

    public Task<ShotTicket> BeginShotAsync(string destinationDirectory, CancellationToken cancellationToken = default)
    {
        var active = Volatile.Read(ref _active);
        if (active != null) return active.BeginShotAsync(destinationDirectory, cancellationToken);
        return Task.FromResult(new ShotTicket(OperationResult.Fail(ErrorCode.CameraNotConnected, "Chưa kết nối máy ảnh"), null));
    }

    public Task PrefocusAsync() => Volatile.Read(ref _active)?.PrefocusAsync() ?? Task.CompletedTask;

    public Task<OperationResult> KeepAliveAsync(CancellationToken cancellationToken = default)
    {
        var active = Volatile.Read(ref _active);
        return active?.KeepAliveAsync(cancellationToken)
            ?? Task.FromResult(OperationResult.Fail(ErrorCode.CameraNotConnected, "Chưa kết nối máy ảnh"));
    }

    /// <summary>Chọn lại máy đang dùng sau mỗi lần một driver đổi trạng thái.</summary>
    private void Recompute()
    {
        ITetheredCamera? previous, next;
        bool liveViewWanted;
        lock (_lock)
        {
            previous = _active;
            next = _cameras.FirstOrDefault(c => c.IsConnected);
            _active = next;
            liveViewWanted = _liveViewWanted;
            foreach (var c in _cameras) c.AutoReconnect = next == null || ReferenceEquals(c, next);
        }

        if (!ReferenceEquals(previous, next))
        {
            _log($"[INFO] Active camera: {(next == null ? "none" : $"{next.Vendor} {next.GetInfo().Model}")}");
            if (liveViewWanted)
            {
                if (previous != null) _ = previous.StopLiveViewAsync();
                if (next != null) _ = next.StartLiveViewAsync();
            }
        }

        var info = GetInfo();
        CameraInfo before;
        lock (_lock)
        {
            before = _published;
            if (before.SameAs(info)) return;
            _published = info.Clone();
        }

        InfoChanged?.Invoke(this, info);
        StatusChanged?.Invoke(this, new CameraStatusChangedEventArgs
        {
            NewStatus = GetStatus(),
            UserMessage = info.Message,
            DiagnosticMessage = $"{info.Vendor} {info.Model}".Trim(),
        });
    }

    public void Dispose()
    {
        foreach (var c in _cameras)
        {
            try { c.Dispose(); }
            catch (Exception ex) { _log($"[WARN] Dispose {c.Vendor} camera: {ex.Message}"); }
        }
    }
}
