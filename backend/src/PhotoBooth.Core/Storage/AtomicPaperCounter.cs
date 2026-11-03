namespace PhotoBooth.Infrastructure.Storage
{
    using System;
    using System.IO;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using PhotoBooth.Core.Interfaces;
    using PhotoBooth.Core.Models;

    public class AtomicPaperCounter : IPaperCounter
    {
        private readonly string _filePath;
        private readonly SemaphoreSlim _lock = new(1, 1);
        private int _remainingSheets = 400;
        private int _loadedCapacity = 400;
        private int _warningThreshold = 30;

        public int RemainingSheets => _remainingSheets;
        public int LoadedCapacity => _loadedCapacity;
        public int WarningThreshold => _warningThreshold;

        public event EventHandler<PaperCountChangedEventArgs>? PaperCountChanged;

        public AtomicPaperCounter(string filePath)
        {
            _filePath = filePath;
        }

        public async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            await _lock.WaitAsync(cancellationToken);
            try
            {
                if (File.Exists(_filePath))
                {
                    string json = await File.ReadAllTextAsync(_filePath, cancellationToken);
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("RemainingSheets", out var r)) _remainingSheets = r.GetInt32();
                    if (root.TryGetProperty("LoadedCapacity", out var c)) _loadedCapacity = c.GetInt32();
                    if (root.TryGetProperty("WarningThreshold", out var w)) _warningThreshold = w.GetInt32();
                }
                else
                {
                    await SaveInternalAsync(cancellationToken);
                }
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task RecordSheetsPrintedAsync(int sheetCount, CancellationToken cancellationToken = default)
        {
            await _lock.WaitAsync(cancellationToken);
            try
            {
                _remainingSheets = Math.Max(0, _remainingSheets - sheetCount);
                await SaveInternalAsync(cancellationToken);
            }
            finally
            {
                _lock.Release();
            }

            PaperCountChanged?.Invoke(this, new PaperCountChangedEventArgs
            {
                RemainingSheets = _remainingSheets,
                IsLowPaper = _remainingSheets <= _warningThreshold
            });
        }

        public async Task ResetCountAsync(int totalLoadedSheets, CancellationToken cancellationToken = default)
        {
            await _lock.WaitAsync(cancellationToken);
            try
            {
                _loadedCapacity = totalLoadedSheets;
                _remainingSheets = totalLoadedSheets;
                await SaveInternalAsync(cancellationToken);
            }
            finally
            {
                _lock.Release();
            }

            PaperCountChanged?.Invoke(this, new PaperCountChangedEventArgs
            {
                RemainingSheets = _remainingSheets,
                IsLowPaper = false
            });
        }

        private async Task SaveInternalAsync(CancellationToken ct)
        {
            string dir = Path.GetDirectoryName(_filePath) ?? "";
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string tmpPath = _filePath + ".tmp";
            var data = new
            {
                SchemaVersion = "1.0.0",
                RemainingSheets = _remainingSheets,
                LoadedCapacity = _loadedCapacity,
                WarningThreshold = _warningThreshold,
                LastUpdatedAtUtc = DateTime.UtcNow
            };

            string json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            
            await using (var fs = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true))
            await using (var writer = new StreamWriter(fs))
            {
                await writer.WriteAsync(json.AsMemory(), ct);
                await writer.FlushAsync(ct);
                await fs.FlushAsync(ct);
            }

            File.Move(tmpPath, _filePath, overwrite: true);
        }
    }
}
