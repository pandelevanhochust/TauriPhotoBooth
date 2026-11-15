using System;
using System.Threading.Tasks;
using PhotoBooth.Core.Interfaces;
using PhotoBooth.Core.Models;

namespace PhotoBooth.Hardware.Camera;

/// <summary>Trạng thái máy ảnh nối dây (Canon / Fujifilm), dùng cho UI và admin panel.</summary>
public sealed class CameraInfo
{
    /// <summary>"Canon" | "Fujifilm" | "" (chưa có máy).</summary>
    public string Vendor { get; set; } = string.Empty;
    public bool SdkAvailable { get; set; }
    public bool Connected { get; set; }
    public bool LiveView { get; set; }
    public string Model { get; set; } = string.Empty;
    public string Port { get; set; } = string.Empty;
    /// <summary>"ready" | "liveview" | "capturing" | "disconnected" | "error".</summary>
    public string Status { get; set; } = "disconnected";
    public string Message { get; set; } = string.Empty;
    /// <summary>0–100, -1 khi cắm nguồn ngoài, null khi không đọc được.</summary>
    public int? BatteryLevel { get; set; }

    public CameraInfo Clone() => (CameraInfo)MemberwiseClone();

    public bool SameAs(CameraInfo other) =>
        Vendor == other.Vendor && SdkAvailable == other.SdkAvailable && Connected == other.Connected
        && LiveView == other.LiveView && Model == other.Model && Port == other.Port
        && Status == other.Status && Message == other.Message && BatteryLevel == other.BatteryLevel;
}

/// <summary>Máy ảnh điều khiển qua SDK của hãng: tự dò, tự kết nối lại, live view, chụp ảnh gốc về máy tính.</summary>
public interface ITetheredCamera : ICameraDevice
{
    string Vendor { get; }
    bool IsConnected { get; }

    /// <summary>Tắt thì driver không tự dò máy nữa (CameraManager tắt khi đã có máy hãng khác).</summary>
    bool AutoReconnect { get; set; }

    CameraInfo GetInfo();
    Task<CameraInfo> ReconnectAsync();

    /// <summary>Báo mỗi khi thông tin máy ảnh đổi (kết nối, rút dây, bật/tắt live view...).</summary>
    event EventHandler<CameraInfo>? InfoChanged;

    /// <summary>
    /// Nhả màn trập rồi trả về ngay; ảnh tải về ở chế độ nền qua <see cref="ShotTicket.Completion"/>.
    /// Mặc định (driver chưa tách được hai bước): trả vé ngay, toàn bộ việc chụp nằm trong Completion.
    /// </summary>
    Task<ShotTicket> BeginShotAsync(string destinationDirectory, CancellationToken cancellationToken = default)
        => Task.FromResult(new ShotTicket(OperationResult.Ok(), CapturePhotoAsync(destinationDirectory, cancellationToken)));

    /// <summary>Lấy nét trước (nhấn nửa cò) để tấm kế tiếp nhả màn trập ngay. Mặc định không làm gì.</summary>
    Task PrefocusAsync() => Task.CompletedTask;
}

/// <summary>Kết quả nhả màn trập và tác vụ tải ảnh về của một tấm chụp.</summary>
public sealed record ShotTicket(OperationResult Shutter, Task<CapturePhotoResult>? Completion);
