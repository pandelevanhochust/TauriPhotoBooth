namespace PhotoBooth.Core.Models
{
    using System;
    using System.Collections.Generic;

    public enum ErrorCode
    {
        None = 0,
        
        // Nhóm Camera
        CameraNotConnected = 1001,
        CameraBusy = 1002,
        CameraShutterFailed = 1003,
        CameraLiveViewTimeout = 1004,
        CameraAutoPowerOff = 1005,
        CameraResourceOccupied = 1006,
        CameraSdkNotInstalled = 1007,
        CameraAutoFocusFailed = 1008,
        CameraTransferFailed = 1009,
        CameraCaptureTimeout = 1010,

        // Nhóm Máy in
        PrinterOffline = 2001,
        PrinterOutOfPaper = 2002,
        PrinterPaperJammed = 2003,
        PrinterCoverOpen = 2004,
        PrinterDriverNotFound = 2005,
        PrintSpoolerTimeout = 2006,
        PrintJobFailed = 2007,
        PrintJobCancelled = 2008,

        // Nhóm Lưu trữ & Dữ liệu
        StorageDiskFull = 3001,
        SessionCorrupted = 3002,
        TemplateFileNotFound = 3003,
        TemplateInvalid = 3004,

        // Nhóm Ngoại vi (Quy hoạch sau MVP)
        SerialPortUnavailable = 4001,
        SerialTimeout = 4002,

        // Nhóm Hệ thống chung
        OperationCancelled = 5001,
        UnknownError = 5999
    }

    public enum CameraDeviceStatus
    {
        Disconnected,
        Connecting,
        Ready,
        LiveViewActive,
        Capturing,
        Error
    }

    public enum PrinterDeviceStatus
    {
        Ready,
        Printing,
        OutOfPaper,
        PaperJammed,
        CoverOpen,
        Offline,
        Unknown
    }

    public enum SessionState
    {
        Created,
        SelectingLayout,
        WaitingForUserReady,
        Capturing,
        RetakePrompt,
        SelectingFrame,
        ReviewingPrint,
        Printing,
        Completed,
        Aborted,
        Failed,
        RecoveryNeeded
    }

    public enum PaperType
    {
        Card4x6,
        DoubleStrip2x6
    }

    public enum OperationMode
    {
        FreeEvent,
        Commercial
    }

    public enum KioskModeLevel
    {
        MaximizedTopmost,
        ShellLauncher,
        AssignedAccess
    }

    public enum SelectedCameraDriver
    {
        Mock,
        Webcam,
        CanonEdsdk,
        SonyCrAdapter
    }

    public class OperationResult
    {
        public bool IsSuccess { get; set; }
        public ErrorCode Code { get; set; }
        public string UserMessage { get; set; } = string.Empty;
        public string DiagnosticMessage { get; set; } = string.Empty;

        public static OperationResult Ok() => new OperationResult 
        { 
            IsSuccess = true, 
            Code = ErrorCode.None 
        };

        public static OperationResult Fail(ErrorCode code, string userMsg, string diagMsg = "") => new OperationResult 
        { 
            IsSuccess = false, 
            Code = code, 
            UserMessage = userMsg, 
            DiagnosticMessage = string.IsNullOrWhiteSpace(diagMsg) ? userMsg : diagMsg 
        };
    }

    public class CapturePhotoResult : OperationResult
    {
        public string RawImagePath { get; set; } = string.Empty;
        public int ShotIndex { get; set; }
    }

    public class PrintJobRequest
    {
        public string ImagePath { get; set; } = string.Empty;
        public string PrinterName { get; set; } = string.Empty;
        public int Copies { get; set; } = 1;
        public PaperType PaperType { get; set; } = PaperType.Card4x6;
        public double ZoomCorrection { get; set; } = 1.0;
        public int OffsetX { get; set; } = 0;
        public int OffsetY { get; set; } = 0;
    }

    public class PrintJobResult : OperationResult
    {
        public string JobId { get; set; } = string.Empty;
        public int SheetsPrinted { get; set; }
    }

    /// <summary>
    /// FrameLeaseEventArgs quản lý vùng nhớ đệm khung hình Live View, giảm thiểu việc cấp phát rác (GC)
    /// </summary>
    public class FrameLeaseEventArgs : EventArgs, IDisposable
    {
        private readonly Action _returnBufferAction;
        private bool _disposed;

        public ReadOnlyMemory<byte> JpegData { get; }
        public int Width { get; }
        public int Height { get; }

        public FrameLeaseEventArgs(ReadOnlyMemory<byte> jpegData, int width, int height, Action returnBufferAction)
        {
            JpegData = jpegData;
            Width = width;
            Height = height;
            _returnBufferAction = returnBufferAction;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                _returnBufferAction?.Invoke();
            }
        }
    }

    public class CameraStatusChangedEventArgs : EventArgs
    {
        public CameraDeviceStatus NewStatus { get; set; }
        public string UserMessage { get; set; } = string.Empty;
        public string DiagnosticMessage { get; set; } = string.Empty;
    }

    public class PrinterStatusChangedEventArgs : EventArgs
    {
        public PrinterDeviceStatus NewStatus { get; set; }
        public string UserMessage { get; set; } = string.Empty;
        public string DiagnosticMessage { get; set; } = string.Empty;
    }

    public class PrintJobProgressEventArgs : EventArgs
    {
        public string JobId { get; set; } = string.Empty;
        public int ProgressPercent { get; set; }
        public string StatusText { get; set; } = string.Empty;
    }

    public class PaperCountChangedEventArgs : EventArgs
    {
        public int RemainingSheets { get; set; }
        public bool IsLowPaper { get; set; }
    }

    public class HoleLocation
    {
        public int ShotIndex { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public int CornerRadius { get; set; }
        public double Rotation { get; set; }
    }

    public class FrameTemplate
    {
        public string SchemaVersion { get; set; } = "1.0.0";
        public string TemplateId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string LayoutId { get; set; } = string.Empty;
        public PaperType SupportedPaperType { get; set; } = PaperType.Card4x6;
        public int CanvasWidth { get; set; } = 1200;
        public int CanvasHeight { get; set; } = 1800;
        public int Dpi { get; set; } = 300;
        public string OverlayImagePath { get; set; } = string.Empty;
        public List<HoleLocation> Holes { get; set; } = new();
    }

    public class BoothSession
    {
        public string SchemaVersion { get; set; } = "1.0.0";
        public string SessionId { get; set; } = string.Empty;
        public DateTime CreatedAtUtc { get; set; }
        public string LayoutId { get; set; } = string.Empty;
        public SessionState State { get; set; } = SessionState.Created;
        public List<string> CapturedPhotoPaths { get; set; } = new();
        public string SelectedTemplateId { get; set; } = string.Empty;
        public string FinalPrintSheetPath { get; set; } = string.Empty;
        public int PrintCopies { get; set; } = 1;
        public string? FailureReason { get; set; }
    }

    public class BoothSettings
    {
        public OperationMode OperationMode { get; set; } = OperationMode.FreeEvent;
        public string AdminPinHash { get; set; } = string.Empty;
        public string AdminPinSalt { get; set; } = string.Empty;
        public int PinMaxFailedAttempts { get; set; } = 5;
        public int PinLockoutDurationSeconds { get; set; } = 60;
        public int CountdownDurationSeconds { get; set; } = 10;
        public int SessionSelectionTimeoutSeconds { get; set; } = 45;
        public int SessionOverallTimeoutSeconds { get; set; } = 300;
        public int ThankYouScreenDurationSeconds { get; set; } = 15;
        public KioskModeLevel KioskModeLevel { get; set; } = KioskModeLevel.MaximizedTopmost;
        public string Language { get; set; } = "vi";
    }

    public class CameraSettings
    {
        public SelectedCameraDriver SelectedDriver { get; set; } = SelectedCameraDriver.Mock;
        public string CameraResolution { get; set; } = "1920x1080";
        public bool IsMirrorPreviewEnabled { get; set; } = true;
        public bool FocusBeforeShutter { get; set; } = true;
    }

    public class PrinterSettings
    {
        public string TargetPrinterName { get; set; } = "MockPrinter";
        public int PaperCapacity { get; set; } = 400;
        public int PaperWarningThreshold { get; set; } = 30;
        public double CalibrationZoom { get; set; } = 1.0;
        public int CalibrationOffsetX { get; set; } = 0;
        public int CalibrationOffsetY { get; set; } = 0;
    }

    public class AudioSettings
    {
        public bool SoundEnabled { get; set; } = true;
        public double SoundVolumePercent { get; set; } = 80.0;
    }

    public class StorageSettings
    {
        public int StorageRetentionDays { get; set; } = 7;
        public string DataDirectoryPath { get; set; } = "%ProgramData%\\OpenPhotoBooth\\Data";
    }

    public class WatchdogSettings
    {
        public int HeartbeatIntervalSeconds { get; set; } = 2;
        public int WatchdogTimeoutSeconds { get; set; } = 15;
    }

    public class AppSettings
    {
        public string SchemaVersion { get; set; } = "1.0.0";
        public BoothSettings Booth { get; set; } = new();
        public CameraSettings Camera { get; set; } = new();
        public PrinterSettings Printer { get; set; } = new();
        public AudioSettings Audio { get; set; } = new();
        public StorageSettings Storage { get; set; } = new();
        public WatchdogSettings Watchdog { get; set; } = new();
    }
}
