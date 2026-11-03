namespace PhotoBooth.Hardware.Printer
{
    using System;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using PhotoBooth.Core.Interfaces;
    using PhotoBooth.Core.Models;

    public class MockPrinterService : IPrinterService
    {
        private readonly string _virtualPrintDirectory;
        private PrinterDeviceStatus _status = PrinterDeviceStatus.Ready;

        public bool SimulatePaperOut { get; set; } = false;
        public bool SimulatePaperJam { get; set; } = false;
        public bool SimulateOffline { get; set; } = false;
        public int SimulationDelayMs { get; set; } = 800;

        public event EventHandler<PrinterStatusChangedEventArgs>? StatusChanged;
        public event EventHandler<PrintJobProgressEventArgs>? JobProgress;

        public MockPrinterService(string virtualPrintDirectory)
        {
            _virtualPrintDirectory = virtualPrintDirectory;
            Directory.CreateDirectory(_virtualPrintDirectory);
        }

        public Task<PrinterDeviceStatus> CheckStatusAsync(string printerName, CancellationToken cancellationToken = default)
        {
            if (SimulateOffline) return Task.FromResult(PrinterDeviceStatus.Offline);
            if (SimulatePaperJam) return Task.FromResult(PrinterDeviceStatus.PaperJammed);
            if (SimulatePaperOut) return Task.FromResult(PrinterDeviceStatus.OutOfPaper);

            return Task.FromResult(_status);
        }

        public async Task<PrintJobResult> PrintLayoutAsync(PrintJobRequest request, CancellationToken cancellationToken = default)
        {
            string jobId = $"MOCK_JOB_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid().ToString("N")[..6]}";

            if (SimulateOffline)
            {
                SetStatus(PrinterDeviceStatus.Offline, "Máy in đang ngoại tuyến");
                return new PrintJobResult
                {
                    IsSuccess = false,
                    Code = ErrorCode.PrinterOffline,
                    UserMessage = "Máy in không phản hồi",
                    JobId = jobId
                };
            }

            if (SimulatePaperOut)
            {
                SetStatus(PrinterDeviceStatus.OutOfPaper, "Máy in đã hết giấy");
                return new PrintJobResult
                {
                    IsSuccess = false,
                    Code = ErrorCode.PrinterOutOfPaper,
                    UserMessage = "Máy in đã hết giấy, vui lòng liên hệ nhân viên",
                    JobId = jobId
                };
            }

            if (SimulatePaperJam)
            {
                SetStatus(PrinterDeviceStatus.PaperJammed, "Máy in bị kẹt giấy");
                return new PrintJobResult
                {
                    IsSuccess = false,
                    Code = ErrorCode.PrinterPaperJammed,
                    UserMessage = "Máy in đang bị kẹt giấy, vui lòng gọi nhân viên",
                    JobId = jobId
                };
            }

            SetStatus(PrinterDeviceStatus.Printing, "Đang xử lý bản in...");
            ReportProgress(jobId, 10, "Bắt đầu nhận lệnh in...");

            await Task.Delay(SimulationDelayMs / 2, cancellationToken);
            ReportProgress(jobId, 50, "Đang nạp giấy và in nhiệt...");

            // Xuất bản in ảo vào thư mục Data/VirtualPrints của app mới
            string outFileName = $"print_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{request.PaperType}.jpg";
            string outFilePath = Path.Combine(_virtualPrintDirectory, outFileName);

            if (File.Exists(request.ImagePath))
            {
                File.Copy(request.ImagePath, outFilePath, overwrite: true);
            }
            else
            {
                // Nếu chưa có file gốc thật, tạo một file giả lập có nội dung
                await File.WriteAllTextAsync(outFilePath, $"VIRTUAL PRINT JOB {jobId} - Copies: {request.Copies} - Type: {request.PaperType}", cancellationToken);
            }

            await Task.Delay(SimulationDelayMs / 2, cancellationToken);
            ReportProgress(jobId, 100, "Đã in hoàn tất!");
            SetStatus(PrinterDeviceStatus.Ready, "Máy in sẵn sàng");

            return new PrintJobResult
            {
                IsSuccess = true,
                Code = ErrorCode.None,
                JobId = jobId,
                SheetsPrinted = request.Copies
            };
        }

        private void SetStatus(PrinterDeviceStatus newStatus, string msg)
        {
            _status = newStatus;
            StatusChanged?.Invoke(this, new PrinterStatusChangedEventArgs
            {
                NewStatus = newStatus,
                UserMessage = msg,
                DiagnosticMessage = $"MockPrinter status: {newStatus}"
            });
        }

        private void ReportProgress(string jobId, int percent, string text)
        {
            JobProgress?.Invoke(this, new PrintJobProgressEventArgs
            {
                JobId = jobId,
                ProgressPercent = percent,
                StatusText = text
            });
        }
    }
}
