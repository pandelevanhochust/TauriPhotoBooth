using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PhotoBooth.Core.Interfaces;
using PhotoBooth.Core.Models;
using PhotoBooth.Infrastructure.Storage;

namespace PhotoBooth.Hardware.Printer;

public class ThermalPrinterService : IPrinterService
{
    private readonly IPaperCounter _paperCounter;
    private readonly string _printerName;
    private readonly string _scriptPath;
    private PrinterDeviceStatus _status = PrinterDeviceStatus.Ready;

    public event EventHandler<PrinterStatusChangedEventArgs>? StatusChanged;
    public event EventHandler<PrintJobProgressEventArgs>? JobProgress;

    public ThermalPrinterService(IPaperCounter paperCounter, string printerName = "", string? scriptPath = null)
    {
        _paperCounter = paperCounter;
        _printerName = printerName;

        if (string.IsNullOrEmpty(scriptPath))
        {
            _scriptPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "resources", "scripts", "print_dnp.ps1");
        }
        else
        {
            _scriptPath = scriptPath;
        }
    }

    public Task<PrinterDeviceStatus> CheckStatusAsync(string printerName, CancellationToken cancellationToken = default)
    {
        if (_paperCounter.RemainingSheets <= 0)
        {
            return Task.FromResult(PrinterDeviceStatus.OutOfPaper);
        }
        return Task.FromResult(_status);
    }

    public async Task<PrintJobResult> PrintLayoutAsync(PrintJobRequest request, CancellationToken cancellationToken = default)
    {
        string jobId = $"JOB_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid().ToString("N")[..6]}";

        if (_paperCounter.RemainingSheets < request.Copies)
        {
            return new PrintJobResult
            {
                IsSuccess = false,
                Code = ErrorCode.PrinterOutOfPaper,
                UserMessage = "Máy in đã hết giấy. Vui lòng liên hệ nhân viên kỹ thuật.",
                JobId = jobId
            };
        }

        // Emit initial progress
        JobProgress?.Invoke(this, new PrintJobProgressEventArgs
        {
            JobId = jobId,
            ProgressPercent = 10,
            StatusText = "Chuẩn bị gửi tài liệu in"
        });

        // Run print_dnp.ps1 if available
        if (File.Exists(_scriptPath) && !string.IsNullOrEmpty(_printerName))
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{_scriptPath}\" -ImagePath \"{request.ImagePath}\" -PrinterName \"{_printerName}\" -Copies {request.Copies}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process != null)
            {
                await process.WaitForExitAsync(cancellationToken);
            }
        }
        else
        {
            // Simulation delay for mock / test environments
            await Task.Delay(1200, cancellationToken);
        }

        // Decrement paper counter atomically
        await _paperCounter.RecordSheetsPrintedAsync(request.Copies, cancellationToken);

        JobProgress?.Invoke(this, new PrintJobProgressEventArgs
        {
            JobId = jobId,
            ProgressPercent = 100,
            StatusText = "In hoàn tất"
        });

        return new PrintJobResult
        {
            IsSuccess = true,
            Code = ErrorCode.None,
            JobId = jobId,
            UserMessage = "In ảnh thành công",
            SheetsPrinted = request.Copies
        };
    }
}
