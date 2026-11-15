using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Printing;
using System.IO;
using System.Linq;
using System.Printing;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using PhotoBooth.Core.Interfaces;
using PhotoBooth.Core.Models;
using PrinterSettings = System.Drawing.Printing.PrinterSettings;

namespace PhotoBooth.Host.Printing;

/// <summary>Một hàng đợi in của Windows.</summary>
public sealed record PrinterQueueInfo(
    string Name,
    string Driver,
    string Port,
    bool IsDefault,
    bool IsVirtual,
    bool IsPhotoPrinter,
    PrinterDeviceStatus Status,
    string StatusText,
    int JobCount);

/// <summary>Toàn cảnh máy in: các hàng đợi, máy đang chọn, và máy USB đã cắm nhưng chưa cài driver.</summary>
public sealed record PrinterOverview(
    IReadOnlyList<PrinterQueueInfo> Printers,
    PrinterQueueInfo? Selected,
    IReadOnlyList<string> UsbWithoutDriver,
    bool Ready,
    string Message);

/// <summary>
/// In ảnh ghép qua hàng đợi in của Windows (driver của hãng lo màu và cắt giấy).
/// Ảnh dải 2x6 được xếp hai dải cạnh nhau trên một tờ 4x6 và in bằng khổ có cắt đôi của driver.
/// </summary>
public sealed class WindowsPrinterService : IPrinterService
{
    private static readonly Regex PhotoPrinterPattern = new(
        @"DNP|Dai\s*Nippon|DS-?RX1|DS-?40|DS-?80|DS-?620|DS-?820|QW-?410|Citizen|CX-|CY-|CZ-|Mitsubishi|CP-?[DKW]\d|HiTi|Sinfonia|Shinko|SELPHY|Kodak\s*\d|Fujifilm\s*ASK",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex VirtualPrinterPattern = new(
        @"PDF|XPS|OneNote|Fax|Virtual|Software Printer|Send To",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly IPaperCounter _paperCounter;
    private readonly string _configPath;
    private readonly Action<string> _log;
    private readonly SemaphoreSlim _printGate = new(1, 1);
    private string _configuredPrinter = string.Empty;

    public event EventHandler<PrinterStatusChangedEventArgs>? StatusChanged;
    public event EventHandler<PrintJobProgressEventArgs>? JobProgress;

    public WindowsPrinterService(IPaperCounter paperCounter, string configPath, Action<string>? log = null)
    {
        _paperCounter = paperCounter;
        _configPath = configPath;
        _log = log ?? (_ => { });
        LoadConfig();
    }

    /// <summary>Tên máy in admin đã chọn; rỗng = tự chọn máy in ảnh đầu tiên.</summary>
    public string ConfiguredPrinter => _configuredPrinter;

    public void SelectPrinter(string? printerName)
    {
        _configuredPrinter = printerName?.Trim() ?? string.Empty;
        try
        {
            File.WriteAllText(_configPath, JsonSerializer.Serialize(new { printerName = _configuredPrinter }));
        }
        catch (Exception ex)
        {
            _log($"[WARN] Save printer config: {ex.Message}");
        }
    }

    private void LoadConfig()
    {
        try
        {
            if (!File.Exists(_configPath)) return;
            using var doc = JsonDocument.Parse(File.ReadAllText(_configPath));
            if (doc.RootElement.TryGetProperty("printerName", out var name)) _configuredPrinter = name.GetString() ?? string.Empty;
        }
        catch (Exception ex)
        {
            _log($"[WARN] Load printer config: {ex.Message}");
        }
    }

    // ------------------------------------------------------------------ Trạng thái

    public Task<PrinterOverview> GetOverviewAsync() => Task.Run(GetOverview);

    public PrinterOverview GetOverview()
    {
        var queues = ListQueues();
        var selected = ResolvePrinter(queues);
        var usbWithoutDriver = FindUsbPrintersWithoutQueue(queues);

        string message;
        bool ready = false;
        if (selected != null)
        {
            ready = selected.Status is PrinterDeviceStatus.Ready or PrinterDeviceStatus.Printing;
            message = ready ? $"Máy in sẵn sàng: {selected.Name}" : $"{selected.Name}: {selected.StatusText}";
        }
        else if (usbWithoutDriver.Count > 0)
        {
            message = $"Đã cắm máy in {string.Join(", ", usbWithoutDriver)} nhưng Windows chưa có driver nên chưa in được.\n"
                + "Hãy cài driver của hãng (DNP: tải \"DS-RX1 Printer Driver\" cho Windows 64-bit), rồi bấm \"Kiểm tra lại\".";
        }
        else if (!string.IsNullOrEmpty(_configuredPrinter))
        {
            message = $"Không tìm thấy máy in đã chọn \"{_configuredPrinter}\". Kiểm tra dây USB và nguồn máy in.";
        }
        else
        {
            message = "Chưa có máy in ảnh nào. Cắm máy in, cài driver rồi bấm \"Kiểm tra lại\".";
        }

        return new PrinterOverview(queues, selected, usbWithoutDriver, ready, message);
    }

    public async Task<PrinterDeviceStatus> CheckStatusAsync(string printerName, CancellationToken cancellationToken = default)
    {
        if (_paperCounter.RemainingSheets <= 0) return PrinterDeviceStatus.OutOfPaper;
        var overview = await GetOverviewAsync();
        var queue = string.IsNullOrEmpty(printerName)
            ? overview.Selected
            : overview.Printers.FirstOrDefault(p => string.Equals(p.Name, printerName, StringComparison.OrdinalIgnoreCase));
        return queue?.Status ?? PrinterDeviceStatus.Offline;
    }

    private PrinterQueueInfo? ResolvePrinter(IReadOnlyList<PrinterQueueInfo> queues)
    {
        if (!string.IsNullOrEmpty(_configuredPrinter))
        {
            return queues.FirstOrDefault(p => string.Equals(p.Name, _configuredPrinter, StringComparison.OrdinalIgnoreCase));
        }
        // Không tự in ra máy in ảo (PDF, XPS...): chỉ tự chọn máy in ảnh
        return queues.FirstOrDefault(p => p.IsPhotoPrinter && !p.IsVirtual);
    }

    private List<PrinterQueueInfo> ListQueues()
    {
        var result = new List<PrinterQueueInfo>();
        try
        {
            using var server = new LocalPrintServer();
            string defaultName = string.Empty;
            try { defaultName = server.DefaultPrintQueue?.Name ?? string.Empty; } catch { }

            foreach (var queue in server.GetPrintQueues(new[] { EnumeratedPrintQueueTypes.Local, EnumeratedPrintQueueTypes.Connections }))
            {
                using (queue)
                {
                    try
                    {
                        queue.Refresh();
                        string driver = queue.QueueDriver?.Name ?? string.Empty;
                        string port = queue.QueuePort?.Name ?? string.Empty;
                        bool isVirtual = VirtualPrinterPattern.IsMatch(queue.Name) || VirtualPrinterPattern.IsMatch(driver)
                            || port.StartsWith("PORTPROMPT", StringComparison.OrdinalIgnoreCase)
                            || port.StartsWith("nul", StringComparison.OrdinalIgnoreCase)
                            || port.StartsWith("SHRFAX", StringComparison.OrdinalIgnoreCase);
                        bool isPhoto = PhotoPrinterPattern.IsMatch(queue.Name) || PhotoPrinterPattern.IsMatch(driver);
                        var (status, text) = DescribeStatus(queue);
                        result.Add(new PrinterQueueInfo(queue.Name, driver, port,
                            string.Equals(queue.Name, defaultName, StringComparison.OrdinalIgnoreCase),
                            isVirtual, isPhoto, status, text, queue.NumberOfJobs));
                    }
                    catch (Exception ex)
                    {
                        _log($"[WARN] Read print queue: {ex.Message}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _log($"[WARN] Enumerate printers: {ex.Message}");
        }
        return result;
    }

    private static (PrinterDeviceStatus Status, string Text) DescribeStatus(PrintQueue q)
    {
        if (q.IsOffline || q.IsNotAvailable) return (PrinterDeviceStatus.Offline, "Máy in đang ngoại tuyến (tắt nguồn hoặc rút dây)");
        if (q.IsPaperJammed) return (PrinterDeviceStatus.PaperJammed, "Kẹt giấy");
        if (q.IsOutOfPaper || q.HasPaperProblem) return (PrinterDeviceStatus.OutOfPaper, "Hết giấy hoặc lỗi giấy");
        if (q.IsDoorOpened) return (PrinterDeviceStatus.CoverOpen, "Nắp máy in đang mở");
        if (q.IsTonerLow || q.IsOutOfMemory || q.NeedUserIntervention || q.IsInError) return (PrinterDeviceStatus.Unknown, "Máy in báo lỗi, cần kiểm tra (ruy băng / giấy)");
        if (q.IsPaused) return (PrinterDeviceStatus.Offline, "Hàng đợi in đang tạm dừng");
        if (q.IsPrinting || q.IsBusy || q.IsProcessing) return (PrinterDeviceStatus.Printing, "Đang in");
        return (PrinterDeviceStatus.Ready, "Sẵn sàng");
    }

    /// <summary>Máy in USB đang cắm (lớp USBPRINT) mà không có hàng đợi nào dùng cổng của nó = chưa cài driver.</summary>
    private List<string> FindUsbPrintersWithoutQueue(IReadOnlyList<PrinterQueueInfo> queues)
    {
        var missing = new List<string>();
        try
        {
            using var root = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\USBPRINT");
            if (root == null) return missing;
            foreach (string model in root.GetSubKeyNames())
            {
                using var modelKey = root.OpenSubKey(model);
                if (modelKey == null) continue;
                foreach (string instance in modelKey.GetSubKeyNames())
                {
                    string instanceId = $@"USBPRINT\{model}\{instance}";
                    if (CM_Locate_DevNodeW(out _, instanceId, 0) != 0) continue; // không còn cắm

                    string port = instance[(instance.LastIndexOf('&') + 1)..];
                    if (queues.Any(q => string.Equals(q.Port, port, StringComparison.OrdinalIgnoreCase))) continue;

                    string name = model.Replace('_', ' ').Trim();
                    using var instKey = modelKey.OpenSubKey(instance);
                    if (instKey?.GetValue("FriendlyName") is string friendly && !friendly.StartsWith('@')) name = friendly.Trim();
                    name = Regex.Replace(name, @"\s{2,}", " ");
                    if (!missing.Contains(name)) missing.Add(name);
                }
            }
        }
        catch (Exception ex)
        {
            _log($"[WARN] Scan USB printers: {ex.Message}");
        }
        return missing;
    }

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Locate_DevNodeW(out uint devInst, string deviceId, int flags);

    // ------------------------------------------------------------------ In

    public async Task<PrintJobResult> PrintLayoutAsync(PrintJobRequest request, CancellationToken cancellationToken = default)
    {
        string jobId = $"JOB_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid().ToString("N")[..6]}";
        PrintJobResult Failed(ErrorCode code, string message)
        {
            _log($"[WARN] Print {jobId} failed: {message}");
            StatusChanged?.Invoke(this, new PrinterStatusChangedEventArgs { NewStatus = PrinterDeviceStatus.Unknown, UserMessage = message, DiagnosticMessage = message });
            return new PrintJobResult { IsSuccess = false, Code = code, UserMessage = message, DiagnosticMessage = message, JobId = jobId };
        }

        if (!File.Exists(request.ImagePath)) return Failed(ErrorCode.PrinterDriverNotFound, "Không tìm thấy ảnh để in.");

        var overview = await GetOverviewAsync();
        var printer = string.IsNullOrEmpty(request.PrinterName)
            ? overview.Selected
            : overview.Printers.FirstOrDefault(p => string.Equals(p.Name, request.PrinterName, StringComparison.OrdinalIgnoreCase));
        if (printer == null) return Failed(ErrorCode.PrinterDriverNotFound, overview.Message);

        switch (printer.Status)
        {
            case PrinterDeviceStatus.Offline: return Failed(ErrorCode.PrinterOffline, $"{printer.Name}: {printer.StatusText}");
            case PrinterDeviceStatus.OutOfPaper: return Failed(ErrorCode.PrinterOutOfPaper, $"{printer.Name}: {printer.StatusText}");
            case PrinterDeviceStatus.PaperJammed: return Failed(ErrorCode.PrinterPaperJammed, $"{printer.Name}: {printer.StatusText}");
            case PrinterDeviceStatus.CoverOpen: return Failed(ErrorCode.PrinterCoverOpen, $"{printer.Name}: {printer.StatusText}");
        }

        await _printGate.WaitAsync(cancellationToken);
        try
        {
            Report(jobId, 10, "Đang gửi ảnh tới máy in");
            var (sheets, paperName) = await Task.Run(() => SpoolImage(printer.Name, request, jobId), cancellationToken);
            await _paperCounter.RecordSheetsPrintedAsync(sheets, cancellationToken);
            Report(jobId, 100, "Đã gửi lệnh in");
            _log($"[INFO] Print {jobId}: {sheets} sheet(s) on \"{printer.Name}\", paper \"{paperName}\", image {request.ImagePath}");
            StatusChanged?.Invoke(this, new PrinterStatusChangedEventArgs { NewStatus = PrinterDeviceStatus.Printing, UserMessage = "Đang in", DiagnosticMessage = jobId });
            return new PrintJobResult { IsSuccess = true, Code = ErrorCode.None, JobId = jobId, UserMessage = "Đã gửi lệnh in", SheetsPrinted = sheets };
        }
        catch (InvalidPrinterException ex)
        {
            return Failed(ErrorCode.PrinterDriverNotFound, $"Máy in \"{printer.Name}\" không dùng được: {ex.Message}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Failed(ErrorCode.PrinterOffline, $"Gửi lệnh in thất bại: {ex.Message}");
        }
        finally
        {
            _printGate.Release();
        }
    }

    private void Report(string jobId, int percent, string text) =>
        JobProgress?.Invoke(this, new PrintJobProgressEventArgs { JobId = jobId, ProgressPercent = percent, StatusText = text });

    /// <summary>Đưa ảnh vào hàng đợi in. Trả về số tờ giấy dùng và tên khổ giấy đã chọn.</summary>
    private (int Sheets, string PaperName) SpoolImage(string printerName, PrintJobRequest request, string jobId)
    {
        using var image = Image.FromFile(request.ImagePath);

        // Dải 2x6 (tỉ lệ ~1:3): hai dải cạnh nhau trên một tờ 4x6, máy in cắt đôi
        bool isStrip = image.Width * 2.5 < image.Height || image.Height * 2.5 < image.Width;
        int copies = Math.Max(1, request.Copies);
        int sheets = isStrip ? (copies + 1) / 2 : copies;

        using var doc = new PrintDocument { DocumentName = $"HihiBooth {jobId}", PrintController = new StandardPrintController() };
        doc.PrinterSettings.PrinterName = printerName;
        if (!doc.PrinterSettings.IsValid) throw new InvalidPrinterException(doc.PrinterSettings);

        var paper = ChoosePaper(doc.PrinterSettings, isStrip);
        if (paper != null) doc.DefaultPageSettings.PaperSize = paper;
        doc.DefaultPageSettings.Margins = new Margins(0, 0, 0, 0);
        doc.OriginAtMargins = false;
        doc.DefaultPageSettings.Landscape = false;

        double zoom = request.ZoomCorrection > 0 ? request.ZoomCorrection : 1.0;
        int page = 0;
        doc.PrintPage += (_, e) =>
        {
            var g = e.Graphics!;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.CompositingQuality = CompositingQuality.HighQuality;

            RectangleF bounds = e.PageBounds;
            float w = (float)(bounds.Width * zoom), h = (float)(bounds.Height * zoom);
            var target = new RectangleF(
                bounds.X + (bounds.Width - w) / 2 + request.OffsetX,
                bounds.Y + (bounds.Height - h) / 2 + request.OffsetY,
                w, h);

            if (isStrip) DrawTwoStrips(g, image, target);
            else DrawFitted(g, image, target);

            page++;
            e.HasMorePages = page < sheets;
        };
        doc.Print();
        return (sheets, paper?.PaperName ?? doc.DefaultPageSettings.PaperSize.PaperName);
    }

    /// <summary>Vẽ ảnh phủ kín vùng in; ảnh và giấy lệch chiều (dọc / ngang) thì xoay ảnh 90°.</summary>
    private static void DrawFitted(Graphics g, Image image, RectangleF target)
    {
        bool rotate = (image.Width > image.Height) != (target.Width > target.Height);
        if (!rotate)
        {
            g.DrawImage(image, target);
            return;
        }
        var state = g.Save();
        g.TranslateTransform(target.X + target.Width / 2, target.Y + target.Height / 2);
        g.RotateTransform(90);
        g.DrawImage(image, new RectangleF(-target.Height / 2, -target.Width / 2, target.Height, target.Width));
        g.Restore(state);
    }

    /// <summary>Hai dải giống nhau chia đôi tờ giấy theo cạnh dài (đường cắt nằm giữa hai dải).</summary>
    private static void DrawTwoStrips(Graphics g, Image strip, RectangleF target)
    {
        if (target.Width <= target.Height)
        {
            // Giấy dọc: hai dải dọc trái / phải
            float half = target.Width / 2;
            DrawFitted(g, strip, new RectangleF(target.X, target.Y, half, target.Height));
            DrawFitted(g, strip, new RectangleF(target.X + half, target.Y, half, target.Height));
        }
        else
        {
            // Giấy ngang: hai dải nằm ngang trên / dưới
            float half = target.Height / 2;
            DrawFitted(g, strip, new RectangleF(target.X, target.Y, target.Width, half));
            DrawFitted(g, strip, new RectangleF(target.X, target.Y + half, target.Width, half));
        }
    }

    /// <summary>
    /// Chọn khổ giấy 4x6 của driver. Dải 2x6: ưu tiên khổ có cắt đôi ("2x6", "(6x4) x 2"...).
    /// Không khớp tên thì lấy khổ có kích thước gần 4x6 inch nhất.
    /// </summary>
    private static PaperSize? ChoosePaper(PrinterSettings settings, bool isStrip)
    {
        var sizes = settings.PaperSizes.Cast<PaperSize>().ToList();
        if (sizes.Count == 0) return null;

        static bool Is4x6(PaperSize p)
        {
            int shortSide = Math.Min(p.Width, p.Height), longSide = Math.Max(p.Width, p.Height);
            return Math.Abs(shortSide - 400) <= 25 && Math.Abs(longSide - 600) <= 30; // đơn vị 1/100 inch
        }
        static bool IsCutName(string name) => Regex.IsMatch(name, @"2\s*x\s*6|6\s*x\s*2|x\s*2\b|cut|strip", RegexOptions.IgnoreCase);

        var fourBySix = sizes.Where(Is4x6).ToList();
        if (isStrip)
        {
            var cut = fourBySix.FirstOrDefault(p => IsCutName(p.PaperName))
                ?? sizes.FirstOrDefault(p => Regex.IsMatch(p.PaperName, @"2\s*x\s*6|6\s*x\s*2", RegexOptions.IgnoreCase));
            if (cut != null) return cut;
        }
        var plain = fourBySix.FirstOrDefault(p => !IsCutName(p.PaperName)) ?? fourBySix.FirstOrDefault();
        if (plain != null) return plain;

        return sizes
            .OrderBy(p => Math.Abs(Math.Min(p.Width, p.Height) - 400) + Math.Abs(Math.Max(p.Width, p.Height) - 600))
            .First();
    }

    /// <summary>Các khổ giấy driver của máy in hỗ trợ (để admin kiểm tra đã có khổ 4x6 / cắt 2x6 chưa).</summary>
    public static IReadOnlyList<string> ListPaperSizes(string printerName)
    {
        try
        {
            var settings = new PrinterSettings { PrinterName = printerName };
            if (!settings.IsValid) return Array.Empty<string>();
            return settings.PaperSizes.Cast<PaperSize>().Select(p => $"{p.PaperName} ({p.Width / 100.0:0.##}x{p.Height / 100.0:0.##} in)").ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }
}
