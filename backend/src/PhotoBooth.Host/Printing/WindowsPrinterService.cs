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

/// <summary>
/// Căn chỉnh bản in, cùng cách tính với app FavoriCurrent đang chạy thật để dùng lại được số của nó:
/// ảnh có kích thước danh nghĩa 4 x 6 inch (không phải cả trang tràn lề 4.13 x 6.15 của DNP), nhân tỉ lệ,
/// đặt giữa trang rồi dịch theo Offset. Offset tính bằng 1/100 inch; X dương = sang phải, Y dương = xuống dưới.
/// Mặc định là số FavoriCurrent đang dùng cho khổ dọc trên DS-RX1.
/// </summary>
/// <param name="ScalePercent">Tỉ lệ ảnh 4x6, %.</param>
/// <param name="StripScalePercent">Tỉ lệ tờ dải 2x6 (cả hai dải), %.</param>
/// <param name="StripGapMm">Khoảng hở thêm giữa hai dải ở đường cắt, mm (FavoriCurrent không có: 0).</param>
public sealed record PrintCalibration(
    double ScalePercent = 101, double OffsetX = -3, double OffsetY = -4,
    double StripScalePercent = 101, double StripOffsetX = 0, double StripOffsetY = -4,
    double StripGapMm = 0)
{
    public PrintCalibration Clamped() => new(
        Math.Clamp(ScalePercent, 85, 115), Math.Clamp(OffsetX, -50, 50), Math.Clamp(OffsetY, -50, 50),
        Math.Clamp(StripScalePercent, 85, 115), Math.Clamp(StripOffsetX, -50, 50), Math.Clamp(StripOffsetY, -50, 50),
        Math.Clamp(StripGapMm, 0, 8));
}

/// <summary>Toàn cảnh máy in: các hàng đợi, máy đang chọn, và máy USB đã cắm nhưng chưa cài driver.</summary>
public sealed record PrinterOverview(
    IReadOnlyList<PrinterQueueInfo> Printers,
    PrinterQueueInfo? Selected,
    IReadOnlyList<string> UsbWithoutDriver,
    bool Ready,
    string Message);

/// <summary>
/// In ảnh ghép qua hàng đợi in của Windows (driver của hãng lo màu và cắt giấy).
/// Layout dải (hai dải 2x6 trên một tờ 4x6) phải in kèm lệnh cắt đôi. Cách chắc nhất, giống FavoriCurrent:
/// tạo thêm một hàng đợi thứ hai cho cùng máy in, tên có "2x6" (vd. "DS-RX1-2x6"), đặt sẵn preset cắt 2 inch
/// trong Printing Preferences của hàng đợi đó; app in dải ra hàng đợi này và giữ nguyên preset của nó.
/// Với driver DNP DS-RX1, lệnh cắt là mục "Cutter control = 2inch cut" ở tab Option, đi với khổ 4x6 thường;
/// các khổ tên "... x 2" là in hai tấm 4x6 trên giấy 6x8, không phải cắt dải, nên app không coi đó là khổ cắt.
/// Không có hàng đợi riêng thì dải được in trên khổ 4x6 thường, không cắt, và app ghi cảnh báo vào log.
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
    private PrintCalibration _calibration = new();

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

    public PrintCalibration Calibration => _calibration;

    public void SetCalibration(PrintCalibration calibration)
    {
        _calibration = calibration.Clamped();
        SaveConfig();
    }

    public void SelectPrinter(string? printerName)
    {
        _configuredPrinter = printerName?.Trim() ?? string.Empty;
        SaveConfig();
    }

    private void SaveConfig()
    {
        try
        {
            File.WriteAllText(_configPath, JsonSerializer.Serialize(new
            {
                printerName = _configuredPrinter,
                scalePercent = _calibration.ScalePercent,
                offsetX = _calibration.OffsetX,
                offsetY = _calibration.OffsetY,
                stripScalePercent = _calibration.StripScalePercent,
                stripOffsetX = _calibration.StripOffsetX,
                stripOffsetY = _calibration.StripOffsetY,
                stripGapMm = _calibration.StripGapMm,
            }));
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
            var root = doc.RootElement;
            if (root.TryGetProperty("printerName", out var name)) _configuredPrinter = name.GetString() ?? string.Empty;
            double Number(string key, double fallback) =>
                root.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : fallback;
            var defaults = new PrintCalibration();
            _calibration = new PrintCalibration(
                Number("scalePercent", defaults.ScalePercent),
                Number("offsetX", defaults.OffsetX),
                Number("offsetY", defaults.OffsetY),
                Number("stripScalePercent", defaults.StripScalePercent),
                Number("stripOffsetX", defaults.StripOffsetX),
                Number("stripOffsetY", defaults.StripOffsetY),
                Number("stripGapMm", defaults.StripGapMm)).Clamped();
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
        // Không tự in ra máy in ảo (PDF, XPS...): chỉ tự chọn máy in ảnh; hàng đợi cắt dải để dành cho layout dải
        return queues.FirstOrDefault(p => p.IsPhotoPrinter && !p.IsVirtual && !IsStripQueueName(p.Name))
            ?? queues.FirstOrDefault(p => p.IsPhotoPrinter && !p.IsVirtual);
    }

    private static bool IsStripQueueName(string name) =>
        Regex.IsMatch(name, @"2\s*x\s*6|6\s*x\s*2|strip|cut", RegexOptions.IgnoreCase);

    /// <summary>Hàng đợi riêng cho dải ảnh (preset cắt đặt sẵn), phải cùng cổng USB với máy in chính.</summary>
    private static PrinterQueueInfo? FindStripQueue(IReadOnlyList<PrinterQueueInfo> queues, PrinterQueueInfo main)
    {
        if (IsStripQueueName(main.Name)) return main;
        var candidates = queues
            .Where(q => !q.IsVirtual && !string.Equals(q.Name, main.Name, StringComparison.OrdinalIgnoreCase) && IsStripQueueName(q.Name))
            .ToList();
        // Chỉ nhận hàng đợi cùng cổng với máy in chính: hàng đợi khác cổng là một máy in khác (vd. máy laser),
        // in dải sang đó sẽ ra nhầm máy
        return candidates.FirstOrDefault(q => string.Equals(q.Port, main.Port, StringComparison.OrdinalIgnoreCase));
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

        // Dải ảnh: dùng hàng đợi có preset cắt nếu đã tạo (cùng một máy in nên trạng thái như máy in chính)
        bool usePreset = false;
        if (request.PaperType == PaperType.DoubleStrip2x6 && FindStripQueue(overview.Printers, printer) is { } stripQueue)
        {
            printer = stripQueue;
            usePreset = true;
        }

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
            var (sheets, paperName) = await Task.Run(() => SpoolImage(printer.Name, request, jobId, usePreset), cancellationToken);
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

    /// <summary>
    /// Nhận biết khổ in theo kích thước ảnh: ảnh một dải (tỉ lệ khoảng 1:3, vd. 1200x3600) là dải 2x6, in hai dải
    /// trên tờ 4x6 rồi cắt đôi; ảnh khác (vd. 2400x3600, tỉ lệ 2:3) là ảnh 4x6.
    /// </summary>
    public static PaperType DetectPaperType(int width, int height) =>
        width * 2.5 < height || height * 2.5 < width ? PaperType.DoubleStrip2x6 : PaperType.Card4x6;

    private void Report(string jobId, int percent, string text) =>
        JobProgress?.Invoke(this, new PrintJobProgressEventArgs { JobId = jobId, ProgressPercent = percent, StatusText = text });

    /// <summary>
    /// Đưa ảnh vào hàng đợi in. Trả về số tờ giấy dùng và tên khổ giấy đã chọn.
    /// <paramref name="usePreset"/>: hàng đợi có preset cắt đặt sẵn, không ghi đè khổ giấy để giữ lệnh cắt của driver.
    /// </summary>
    private (int Sheets, string PaperName) SpoolImage(string printerName, PrintJobRequest request, string jobId, bool usePreset)
    {
        using var image = Image.FromFile(request.ImagePath);

        // Ảnh một dải (tỉ lệ ~1:3): app tự xếp hai dải cạnh nhau. Ảnh ghép layout dải (2400x3600) đã có sẵn hai dải.
        bool singleStripImage = DetectPaperType(image.Width, image.Height) == PaperType.DoubleStrip2x6;
        bool needsCut = singleStripImage || request.PaperType == PaperType.DoubleStrip2x6;
        int copies = Math.Max(1, request.Copies);
        // Mỗi tờ 4x6 cắt ra hai dải
        int sheets = needsCut ? (copies + 1) / 2 : copies;

        using var doc = new PrintDocument { DocumentName = $"FavoriPhoto {jobId}", PrintController = new StandardPrintController() };
        doc.PrinterSettings.PrinterName = printerName;
        if (!doc.PrinterSettings.IsValid) throw new InvalidPrinterException(doc.PrinterSettings);

        var paper = usePreset ? null : ChoosePaper(doc.PrinterSettings, needsCut);
        if (paper != null) doc.DefaultPageSettings.PaperSize = paper;
        if (needsCut && !usePreset && (paper == null || !IsCutPaperName(paper.PaperName)))
        {
            _log($"[WARN] Print {jobId}: không có khổ giấy cắt đôi trên \"{printerName}\" và chưa có hàng đợi \"...2x6\"; dải ảnh sẽ không được cắt");
        }
        doc.DefaultPageSettings.Margins = new Margins(0, 0, 0, 0);
        doc.OriginAtMargins = false;
        doc.DefaultPageSettings.Landscape = false;

        var calibration = _calibration;
        double extraZoom = request.ZoomCorrection > 0 ? request.ZoomCorrection : 1.0;
        double scale = (needsCut ? calibration.StripScalePercent : calibration.ScalePercent) / 100.0 * extraZoom;
        float offsetX = (float)(needsCut ? calibration.StripOffsetX : calibration.OffsetX) + request.OffsetX;
        float offsetY = (float)(needsCut ? calibration.StripOffsetY : calibration.OffsetY) + request.OffsetY;
        // Đơn vị trang in là 1/100 inch; mỗi dải dịch ra ngoài một nửa khoảng hở
        float stripShift = (float)(calibration.StripGapMm / 2 / 25.4 * 100);
        int page = 0;
        doc.PrintPage += (_, e) =>
        {
            var g = e.Graphics!;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.CompositingQuality = CompositingQuality.HighQuality;

            RectangleF bounds = e.PageBounds;
            bool portrait = bounds.Width <= bounds.Height;
            float shortSide = Math.Min(bounds.Width, bounds.Height), longSide = Math.Max(bounds.Width, bounds.Height);
            // Trang 4x6 (kể cả trang tràn lề 4.13 x 6.15 của DNP): ảnh danh nghĩa đúng 4 x 6 inch. Khổ khác: theo trang.
            bool is4x6 = Math.Abs(shortSide - 400) <= 30 && Math.Abs(longSide - 600) <= 30;
            float nominalW = is4x6 ? (portrait ? 400 : 600) : bounds.Width;
            float nominalH = is4x6 ? (portrait ? 600 : 400) : bounds.Height;
            float w = (float)(nominalW * scale), h = (float)(nominalH * scale);
            var target = new RectangleF(
                bounds.X + (bounds.Width - w) / 2 + offsetX,
                bounds.Y + (bounds.Height - h) / 2 + offsetY,
                w, h);

            if (singleStripImage)
            {
                DrawTwoStrips(g, image, bounds, target, stripShift);
            }
            else
            {
                // Lót bản phủ kín trang bên dưới để phần tràn lề không bị trắng nếu máy xén ít hơn dự tính
                DrawFitted(g, image, bounds);
                DrawFitted(g, image, target);
            }

            page++;
            e.HasMorePages = page < sheets;
        };
        doc.Print();
        return (sheets, (usePreset ? "preset: " : "") + (paper?.PaperName ?? doc.DefaultPageSettings.PaperSize.PaperName));
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

    /// <summary>
    /// Hai dải giống nhau đặt cạnh nhau trong <paramref name="sheet"/> (vùng ảnh đã căn chỉnh), chia theo cạnh dài;
    /// đường cắt của máy in nằm giữa trang. Mỗi dải dịch ra xa đường giữa <paramref name="shift"/> (1/100 inch).
    /// Bên dưới lót một bản phủ kín nửa trang để phần tràn lề không bị trắng.
    /// </summary>
    private static void DrawTwoStrips(Graphics g, Image strip, RectangleF page, RectangleF sheet, float shift)
    {
        bool sideBySide = page.Width <= page.Height; // giấy dọc: hai dải trái / phải; giấy ngang: trên / dưới
        for (int i = 0; i < 2; i++)
        {
            DrawFitted(g, strip, sideBySide
                ? new RectangleF(page.X + i * page.Width / 2, page.Y, page.Width / 2, page.Height)
                : new RectangleF(page.X, page.Y + i * page.Height / 2, page.Width, page.Height / 2));
        }
        for (int i = 0; i < 2; i++)
        {
            float away = i == 0 ? -shift : shift;
            DrawFitted(g, strip, sideBySide
                ? new RectangleF(sheet.X + i * sheet.Width / 2 + away, sheet.Y, sheet.Width / 2, sheet.Height)
                : new RectangleF(sheet.X, sheet.Y + i * sheet.Height / 2 + away, sheet.Width, sheet.Height / 2));
        }
    }

    /// <summary>
    /// Chọn khổ giấy 4x6 của driver. Dải 2x6: ưu tiên khổ có tên cắt dải ("2x6", "cut", "strip") nếu driver có.
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
        static bool IsCutName(string name) => IsCutPaperName(name);

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

    private static bool IsCutPaperName(string name) =>
        Regex.IsMatch(name, @"2\s*x\s*6|6\s*x\s*2|cut|strip", RegexOptions.IgnoreCase);

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
