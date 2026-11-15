using System;
using System.Threading.Tasks;

namespace PhotoBooth.Hardware.Camera;

/// <summary>
/// Máy ảnh nhận được ảnh do chính máy chụp (nút chụp trên thân máy, điều khiển từ xa của hãng như Canon BR-E1),
/// không qua lệnh chụp của app. Dùng cho chế độ chụp bằng điều khiển từ xa.
/// </summary>
public interface IRemoteShutterSource
{
    /// <summary>
    /// <paramref name="destinationDirectory"/> khác null: bật nhận ảnh do máy tự chụp, tải về thư mục đó; đồng thời
    /// để màn hình sau của máy vẫn hiện live view và mở khoá nút trên máy để điều khiển từ xa dùng được.
    /// null: tắt, ảnh không do app yêu cầu bị bỏ qua như trước.
    /// </summary>
    Task SetRemoteShutterAsync(string? destinationDirectory);

    /// <summary>Một ảnh do máy tự chụp vừa tải về xong (đường dẫn file JPEG).</summary>
    event EventHandler<string>? RemotePhotoDownloaded;
}
