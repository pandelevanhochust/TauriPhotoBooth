namespace PhotoBooth.Core.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using PhotoBooth.Core.Models;

    public interface ICameraDevice : IDisposable
    {
        event EventHandler<FrameLeaseEventArgs> FrameArrived;
        event EventHandler<CameraStatusChangedEventArgs> StatusChanged;

        Task<OperationResult> InitializeAsync(CancellationToken cancellationToken = default);
        Task<OperationResult> StartLiveViewAsync(CancellationToken cancellationToken = default);
        Task<OperationResult> StopLiveViewAsync(CancellationToken cancellationToken = default);
        Task<CapturePhotoResult> CapturePhotoAsync(string destinationDirectory, CancellationToken cancellationToken = default);
        Task<OperationResult> KeepAliveAsync(CancellationToken cancellationToken = default);
        CameraDeviceStatus GetStatus();
    }

    public interface IPaperCounter
    {
        int RemainingSheets { get; }
        int LoadedCapacity { get; }
        int WarningThreshold { get; }

        Task InitializeAsync(CancellationToken cancellationToken = default);
        Task RecordSheetsPrintedAsync(int sheetCount, CancellationToken cancellationToken = default);
        Task ResetCountAsync(int totalLoadedSheets, CancellationToken cancellationToken = default);
        event EventHandler<PaperCountChangedEventArgs> PaperCountChanged;
    }

    public interface IPrinterService
    {
        event EventHandler<PrinterStatusChangedEventArgs> StatusChanged;
        event EventHandler<PrintJobProgressEventArgs> JobProgress;

        Task<PrintJobResult> PrintLayoutAsync(PrintJobRequest request, CancellationToken cancellationToken = default);
        Task<PrinterDeviceStatus> CheckStatusAsync(string printerName, CancellationToken cancellationToken = default);
    }

    public interface ISessionStore
    {
        Task<BoothSession> CreateSessionAsync(string layoutId, CancellationToken cancellationToken = default);
        Task<BoothSession?> GetSessionByIdAsync(string sessionId, CancellationToken cancellationToken = default);
        Task UpdateSessionStatusAsync(string sessionId, SessionState newState, CancellationToken cancellationToken = default);
        Task SaveSessionManifestAsync(BoothSession session, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<BoothSession>> GetIncompleteSessionsAsync(CancellationToken cancellationToken = default);
        Task<IReadOnlyList<BoothSession>> GetFailedSessionsAsync(CancellationToken cancellationToken = default);
        Task DeleteSessionAsync(string sessionId, CancellationToken cancellationToken = default);
        Task CleanupExpiredSessionsAsync(TimeSpan retentionPeriod, CancellationToken cancellationToken = default);
    }

    public interface IImageComposer
    {
        Task<string> ComposePrintSheetAsync(
            FrameTemplate template, 
            IReadOnlyList<string> rawPhotoPaths, 
            string outputPath, 
            CancellationToken cancellationToken = default);
    }

    public interface ITemplateRepository
    {
        Task<IReadOnlyList<FrameTemplate>> GetTemplatesByLayoutAsync(string layoutId, CancellationToken cancellationToken = default);
        Task<FrameTemplate?> GetTemplateByIdAsync(string templateId, CancellationToken cancellationToken = default);
    }

    public interface ISettingsService
    {
        Task<AppSettings> LoadSettingsAsync(CancellationToken cancellationToken = default);
        Task SaveSettingsAsync(AppSettings settings, CancellationToken cancellationToken = default);
    }

    public interface IAuditLog
    {
        Task LogEventAsync(string eventType, string description, string? sessionId = null, CancellationToken cancellationToken = default);
    }

    public interface IAdminAuthService
    {
        bool VerifyPin(string inputPin, string storedHash, string storedSalt);
        string HashPin(string plainPin, out string salt);
        bool IsLockedOut();
        void RecordFailedAttempt();
        void ResetFailedAttempts();
    }

    public interface IClock
    {
        DateTime UtcNow { get; }
        DateTime LocalNow { get; }
    }

    public interface ISoundPlayer
    {
        void PlayCountdownBeep();
        void PlayShutterSnap();
        void PlayPrintingComplete();
        void SetVolume(double volumePercent);
    }

    public interface IKioskGuard
    {
        void EnableKioskHooks();
        void DisableKioskHooks();
    }

    // Module phần cứng ngoại vi (Đèn/Relay/Nút bấm) được quy hoạch sau MVP
    public interface IPeripheralController : IDisposable
    {
        Task<OperationResult> OpenPortAsync(string portName, int baudRate = 9600);
        Task<OperationResult> TriggerFlashLightAsync(int durationMs = 200);
        event EventHandler ArcadeButtonPressed;
    }
}
