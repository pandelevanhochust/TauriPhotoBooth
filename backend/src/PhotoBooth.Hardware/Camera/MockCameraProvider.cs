namespace PhotoBooth.Hardware.Camera
{
    using System;
    using System.Buffers;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using PhotoBooth.Core.Interfaces;
    using PhotoBooth.Core.Models;
    using SkiaSharp;

    public class MockCameraProvider : ICameraDevice
    {
        private readonly int _fps;
        private readonly int _width;
        private readonly int _height;
        private CancellationTokenSource? _liveViewCts;
        private Task? _liveViewTask;
        private CameraDeviceStatus _status = CameraDeviceStatus.Disconnected;
        private long _frameCounter = 0;
        private bool _disposed = false;

        public bool SimulateDisconnect { get; set; } = false;
        public bool SimulateTimeout { get; set; } = false;

        public event EventHandler<FrameLeaseEventArgs>? FrameArrived;
        public event EventHandler<CameraStatusChangedEventArgs>? StatusChanged;

        public MockCameraProvider(int width = 960, int height = 640, int fps = 25)
        {
            _width = width;
            _height = height;
            _fps = fps;
        }

        public Task<OperationResult> InitializeAsync(CancellationToken cancellationToken = default)
        {
            if (SimulateDisconnect)
            {
                SetStatus(CameraDeviceStatus.Error, "Mô phỏng: Không tìm thấy camera giả lập");
                return Task.FromResult(OperationResult.Fail(ErrorCode.CameraNotConnected, "Không tìm thấy camera"));
            }

            SetStatus(CameraDeviceStatus.Ready, "Camera giả lập đã sẵn sàng");
            return Task.FromResult(OperationResult.Ok());
        }

        public Task<OperationResult> StartLiveViewAsync(CancellationToken cancellationToken = default)
        {
            if (_status == CameraDeviceStatus.LiveViewActive)
            {
                return Task.FromResult(OperationResult.Ok());
            }

            _liveViewCts = new CancellationTokenSource();
            SetStatus(CameraDeviceStatus.LiveViewActive, "Live view đang chạy");
            _liveViewTask = Task.Run(() => LiveViewLoop(_liveViewCts.Token), _liveViewCts.Token);
            return Task.FromResult(OperationResult.Ok());
        }

        public async Task<OperationResult> StopLiveViewAsync(CancellationToken cancellationToken = default)
        {
            if (_liveViewCts != null)
            {
                _liveViewCts.Cancel();
                if (_liveViewTask != null)
                {
                    try { await _liveViewTask; } catch (OperationCanceledException) { }
                }
                _liveViewCts.Dispose();
                _liveViewCts = null;
            }

            SetStatus(CameraDeviceStatus.Ready, "Live view đã dừng");
            return OperationResult.Ok();
        }

        public async Task<CapturePhotoResult> CapturePhotoAsync(string destinationDirectory, CancellationToken cancellationToken = default)
        {
            if (SimulateTimeout)
            {
                await Task.Delay(500, cancellationToken);
                return new CapturePhotoResult
                {
                    IsSuccess = false,
                    Code = ErrorCode.CameraCaptureTimeout,
                    UserMessage = "Mô phỏng lỗi: Hết thời gian chờ chụp ảnh",
                    DiagnosticMessage = "MockCamera timeout triggered"
                };
            }

            if (SimulateDisconnect)
            {
                SetStatus(CameraDeviceStatus.Error, "Mô phỏng mất kết nối khi chụp");
                return new CapturePhotoResult
                {
                    IsSuccess = false,
                    Code = ErrorCode.CameraNotConnected,
                    UserMessage = "Không có kết nối tới máy ảnh"
                };
            }

            Directory.CreateDirectory(destinationDirectory);
            string photoPath = Path.Combine(destinationDirectory, $"raw_{Interlocked.Increment(ref _frameCounter):D4}.jpg");

            // Sinh 1 ảnh thô có độ nét cao (1920x1280) bằng code SkiaSharp
            using var bitmap = new SKBitmap(1920, 1280);
            using var canvas = new SKCanvas(bitmap);
            canvas.Clear(SKColors.DarkSlateBlue);

            using var fontLarge = new SKFont { Size = 64 };
            using var fontSmall = new SKFont { Size = 36 };
            using var paint = new SKPaint
            {
                Color = SKColors.White,
                IsAntialias = true
            };

            canvas.DrawText("PHOTOBOOTH MOCK HIGH-RES CAPTURE", 1920 / 2f, 1280 / 2f - 40, SKTextAlign.Center, fontLarge, paint);
            paint.Color = SKColors.LightGoldenrodYellow;
            canvas.DrawText($"Thời gian: {DateTime.Now:HH:mm:ss.fff} - Frame #{_frameCounter}", 1920 / 2f, 1280 / 2f + 40, SKTextAlign.Center, fontSmall, paint);

            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Jpeg, 95);
            using (var stream = File.OpenWrite(photoPath))
            {
                data.SaveTo(stream);
            }

            return new CapturePhotoResult
            {
                IsSuccess = true,
                Code = ErrorCode.None,
                RawImagePath = photoPath,
                ShotIndex = (int)(_frameCounter % 4) + 1
            };
        }

        public Task<OperationResult> KeepAliveAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(OperationResult.Ok());
        }

        public CameraDeviceStatus GetStatus() => _status;

        private void LiveViewLoop(CancellationToken ct)
        {
            int delayMs = 1000 / _fps;

            // Khởi tạo các đối tượng vẽ SkiaSharp dùng lại
            using var bitmap = new SKBitmap(_width, _height);
            using var canvas = new SKCanvas(bitmap);
            using var fontLive = new SKFont { Size = 32 };
            using var paintBg = new SKPaint { Color = SKColors.MidnightBlue };
            using var paintText = new SKPaint { Color = SKColors.LightCyan, IsAntialias = true };
            using var paintCircle = new SKPaint { Color = SKColors.Coral, IsAntialias = true };

            while (!ct.IsCancellationRequested)
            {
                if (SimulateDisconnect)
                {
                    SetStatus(CameraDeviceStatus.Disconnected, "Mô phỏng mất kết nối");
                    break;
                }

                long frameNum = Interlocked.Increment(ref _frameCounter);

                // Vẽ frame giả lập có đối tượng chuyển động để chứng minh tính thời gian thực
                canvas.DrawRect(0, 0, _width, _height, paintBg);

                float circleX = (_frameNumToPosition(frameNum) % _width);
                float circleY = (_height / 2f) + (float)(Math.Sin(frameNum * 0.1) * 80);
                canvas.DrawCircle(circleX, circleY, 40, paintCircle);

                canvas.DrawText($"MOCK CAMERA LIVE VIEW - FRAME #{frameNum:D6}", 30, 50, fontLive, paintText);
                canvas.DrawText($"{DateTime.Now:HH:mm:ss.fff} @ {_fps} FPS", 30, 95, fontLive, paintText);

                using var image = SKImage.FromBitmap(bitmap);
                using var data = image.Encode(SKEncodedImageFormat.Jpeg, 75);

                int byteCount = (int)data.Size;
                byte[] rentedArray = ArrayPool<byte>.Shared.Rent(byteCount);
                using (var stream = data.AsStream())
                {
                    stream.ReadExactly(rentedArray, 0, byteCount);
                }

                var memory = new ReadOnlyMemory<byte>(rentedArray, 0, byteCount);

                var leaseArgs = new FrameLeaseEventArgs(memory, _width, _height, () =>
                {
                    ArrayPool<byte>.Shared.Return(rentedArray);
                });

                var handler = FrameArrived;
                if (handler != null)
                {
                    handler.Invoke(this, leaseArgs);
                }
                else
                {
                    // Nếu không có ai đăng ký nhận, producer tự Dispose để hoàn trả buffer
                    leaseArgs.Dispose();
                }

                try
                {
                    Task.Delay(delayMs, ct).Wait(ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private float _frameNumToPosition(long f) => (f * 15f);

        private void SetStatus(CameraDeviceStatus newStatus, string message)
        {
            _status = newStatus;
            StatusChanged?.Invoke(this, new CameraStatusChangedEventArgs
            {
                NewStatus = newStatus,
                UserMessage = message,
                DiagnosticMessage = $"MockCamera status changed to {newStatus}: {message}"
            });
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                _liveViewCts?.Cancel();
                _liveViewCts?.Dispose();
            }
        }
    }
}
