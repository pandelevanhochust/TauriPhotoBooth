using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using PhotoBooth.Core.Data;
using PhotoBooth.Core.Models;
using PhotoBooth.Hardware.Camera;
using PhotoBooth.Hardware.Printer;
using PhotoBooth.Imaging.Compositor;
using PhotoBooth.Infrastructure.Storage;
using Xunit;

namespace PhotoBooth.Tests;

public class CoreServicesTests : IDisposable
{
    private readonly string _testTempDir;

    public CoreServicesTests()
    {
        _testTempDir = Path.Combine(Path.GetTempPath(), "HihiBooth_Tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testTempDir);
    }

    [Fact]
    public async Task PaperCounter_Decrements_And_Persists_Correctly()
    {
        string counterPath = Path.Combine(_testTempDir, "paper_counter.json");
        var counter = new AtomicPaperCounter(counterPath);
        await counter.InitializeAsync();
        await counter.ResetCountAsync(350);

        Assert.Equal(350, counter.RemainingSheets);

        await counter.RecordSheetsPrintedAsync(2);
        Assert.Equal(348, counter.RemainingSheets);

        // Re-read with new instance to verify disk persistence
        var counter2 = new AtomicPaperCounter(counterPath);
        await counter2.InitializeAsync();
        Assert.Equal(348, counter2.RemainingSheets);
    }

    [Fact]
    public async Task DatabaseService_RecordsTransactions_And_FileItems()
    {
        string dbPath = Path.Combine(_testTempDir, "test_store.db");
        using var db = new DatabaseService(dbPath);
        await db.InitializeAsync();

        string txId = "TX_" + Guid.NewGuid().ToString("N")[..8];
        string sessionId = "SESSION_TEST_01";
        await db.RecordTransactionAsync(txId, sessionId, "strip2x6", 2, "FreeEvent", 0);
        await db.AddFileItemAsync("FILE_01", txId, "C:\\test.jpg", "composite");

        var recents = await db.GetRecentTransactionsAsync(5);
        Assert.NotEmpty(recents);
        Assert.Equal(txId, recents[0]["Id"]?.ToString());
    }

    [Fact]
    public async Task MockCameraProvider_Initializes_And_CapturesPhoto()
    {
        using var camera = new MockCameraProvider(640, 480, 15);
        var initRes = await camera.InitializeAsync();
        Assert.True(initRes.IsSuccess);

        var captureRes = await camera.CapturePhotoAsync(_testTempDir);
        Assert.True(captureRes.IsSuccess);
        Assert.True(File.Exists(captureRes.RawImagePath));
    }

    [Fact]
    public async Task SkiaCompositor_Generates_Valid_Composite_JPEG()
    {
        // 1. Generate 4 sample photos using mock camera
        using var camera = new MockCameraProvider();
        await camera.InitializeAsync();
        var p1 = await camera.CapturePhotoAsync(_testTempDir);
        var p2 = await camera.CapturePhotoAsync(_testTempDir);
        var p3 = await camera.CapturePhotoAsync(_testTempDir);
        var p4 = await camera.CapturePhotoAsync(_testTempDir);

        var template = new FrameTemplate
        {
            TemplateId = "strip2x6",
            CanvasWidth = 1200,
            CanvasHeight = 1800,
            SupportedPaperType = PaperType.DoubleStrip2x6,
            Holes = new List<HoleLocation>
            {
                new() { ShotIndex = 1, X = 30, Y = 40, Width = 540, Height = 370 },
                new() { ShotIndex = 2, X = 30, Y = 450, Width = 540, Height = 370 },
                new() { ShotIndex = 3, X = 30, Y = 860, Width = 540, Height = 370 },
                new() { ShotIndex = 4, X = 30, Y = 1270, Width = 540, Height = 370 }
            }
        };

        var compositor = new NativeSkiaCompositor();
        string outPath = Path.Combine(_testTempDir, "skia_test_composite.jpg");
        await compositor.ComposePrintSheetAsync(template, new[] { p1.RawImagePath, p2.RawImagePath, p3.RawImagePath, p4.RawImagePath }, outPath);

        Assert.True(File.Exists(outPath));
        var fileInfo = new FileInfo(outPath);
        Assert.True(fileInfo.Length > 1000); // Has realistic image data
    }

    [Fact]
    public async Task MagickCompositor_Generates_Composite_And_GifRecap()
    {
        using var camera = new MockCameraProvider();
        await camera.InitializeAsync();
        var p1 = await camera.CapturePhotoAsync(_testTempDir);
        var p2 = await camera.CapturePhotoAsync(_testTempDir);

        var template = new FrameTemplate
        {
            TemplateId = "postcard4x6",
            CanvasWidth = 1200,
            CanvasHeight = 1800,
            SupportedPaperType = PaperType.Card4x6,
            Holes = new List<HoleLocation>
            {
                new() { ShotIndex = 1, X = 35, Y = 50, Width = 550, Height = 750 },
                new() { ShotIndex = 2, X = 615, Y = 50, Width = 550, Height = 750 }
            }
        };

        var compositor = new MagickCompositor();
        string outPath = Path.Combine(_testTempDir, "magick_test_composite.jpg");
        await compositor.ComposePrintSheetAsync(template, new[] { p1.RawImagePath, p2.RawImagePath }, outPath);

        Assert.True(File.Exists(outPath));

        string gifPath = Path.Combine(_testTempDir, "magick_recap.gif");
        await compositor.CreateAnimatedRecapAsync(new[] { p1.RawImagePath, p2.RawImagePath }, gifPath);
        Assert.True(File.Exists(gifPath));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testTempDir))
            {
                Directory.Delete(_testTempDir, true);
            }
        }
        catch { }
    }
}
