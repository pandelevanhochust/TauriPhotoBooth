using PhotoBooth.Core.Storage;
using Xunit;

namespace PhotoBooth.Tests;

public sealed class StorageCleanerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "HihiBoothCleanerTests_" + Guid.NewGuid().ToString("N"));
    private readonly DateTimeOffset _now = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);
    private readonly List<string> _logs = new();

    private string Sessions => Path.Combine(_root, "Sessions");
    private string Gallery => Path.Combine(_root, "Gallery");
    private string CameraTest => Path.Combine(_root, "CameraTest");

    public StorageCleanerTests()
    {
        Directory.CreateDirectory(Sessions);
        Directory.CreateDirectory(Gallery);
        Directory.CreateDirectory(CameraTest);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private StorageCleaner NewCleaner() =>
        new(Sessions, new[] { Gallery, CameraTest }, _root, "host_startup", () => _now, _logs.Add);

    private string MakeSession(string name, DateTimeOffset when)
    {
        string dir = Path.Combine(Sessions, name);
        Directory.CreateDirectory(dir);
        string file = Path.Combine(dir, "shot_1.jpg");
        File.WriteAllBytes(file, new byte[1000]);
        File.SetLastWriteTimeUtc(file, when.UtcDateTime);
        Directory.SetLastWriteTimeUtc(dir, when.UtcDateTime);
        return dir;
    }

    private static string NameFor(DateTimeOffset when, string suffix = "abc123") => $"SESSION_{when:yyyyMMdd_HHmmss}_{suffix}";

    private string MakeFile(string dir, string name, DateTimeOffset when)
    {
        Directory.CreateDirectory(dir);
        string file = Path.Combine(dir, name);
        File.WriteAllBytes(file, new byte[500]);
        File.SetLastWriteTimeUtc(file, when.UtcDateTime);
        return file;
    }

    [Fact]
    public void Run_DeletesSessionsOlderThanSevenDays_KeepsNewer()
    {
        var old = MakeSession(NameFor(_now.AddDays(-8)), _now.AddDays(-8));
        var recent = MakeSession(NameFor(_now.AddDays(-6), "def456"), _now.AddDays(-6));

        var result = NewCleaner().Run();

        Assert.False(Directory.Exists(old));
        Assert.True(Directory.Exists(recent));
        Assert.Equal(1, result.SessionDirectories);
        Assert.True(result.BytesFreed >= 1000);
    }

    [Fact]
    public void Run_SkipsDirectoriesNotNamedAsSessions()
    {
        string other = Path.Combine(Sessions, "important_stuff");
        Directory.CreateDirectory(other);
        File.WriteAllText(Path.Combine(other, "a.txt"), "x");
        Directory.SetLastWriteTimeUtc(other, _now.AddDays(-30).UtcDateTime);

        NewCleaner().Run();

        Assert.True(Directory.Exists(other));
    }

    [Fact]
    public void Run_SkipsProtectedSessions()
    {
        var protectedDir = MakeSession(NameFor(_now.AddDays(-20)), _now.AddDays(-20));

        NewCleaner().Run(dir => string.Equals(dir, protectedDir, StringComparison.OrdinalIgnoreCase));

        Assert.True(Directory.Exists(protectedDir));
    }

    [Fact]
    public void Run_SessionTouchedRecently_IsKeptEvenIfNameIsOld()
    {
        var dir = MakeSession(NameFor(_now.AddDays(-20)), _now.AddDays(-1));

        NewCleaner().Run();

        Assert.True(Directory.Exists(dir));
    }

    [Fact]
    public void Run_DeletesLeftoverVideoFrames_ButKeepsTheSession()
    {
        var dir = MakeSession(NameFor(_now.AddHours(-10)), _now.AddHours(-10));
        string frames = Path.Combine(dir, "video_frames");
        MakeFile(frames, "frame_000001.jpg", _now.AddHours(-9));
        Directory.SetLastWriteTimeUtc(frames, _now.AddHours(-9).UtcDateTime);

        var result = NewCleaner().Run();

        Assert.False(Directory.Exists(frames));
        Assert.True(File.Exists(Path.Combine(dir, "shot_1.jpg")));
        Assert.Equal(1, result.FrameDirectories);
    }

    [Fact]
    public void Run_KeepsFreshVideoFrames_OfSessionStillRecording()
    {
        var dir = MakeSession(NameFor(_now.AddMinutes(-5)), _now.AddMinutes(-5));
        string frames = Path.Combine(dir, "video_frames");
        MakeFile(frames, "frame_000001.jpg", _now.AddMinutes(-1));
        Directory.SetLastWriteTimeUtc(frames, _now.AddMinutes(-1).UtcDateTime);

        NewCleaner().Run();

        Assert.True(Directory.Exists(frames));
    }

    [Fact]
    public void Run_GalleryAndCameraTest_DeleteOldFilesAndEmptyFolders_KeepRoots()
    {
        string oldDay = Path.Combine(Gallery, "2026-11-01", "SESSION_x");
        string newDay = Path.Combine(Gallery, "2026-11-15", "SESSION_y");
        var oldFile = MakeFile(oldDay, "shot_1.jpg", _now.AddDays(-15));
        var newFile = MakeFile(newDay, "shot_1.jpg", _now.AddDays(-1));
        var testShot = MakeFile(CameraTest, "test.jpg", _now.AddDays(-9));

        var result = NewCleaner().Run();

        Assert.False(File.Exists(oldFile));
        Assert.False(Directory.Exists(Path.Combine(Gallery, "2026-11-01")));
        Assert.True(File.Exists(newFile));
        Assert.False(File.Exists(testShot));
        Assert.True(Directory.Exists(Gallery));
        Assert.True(Directory.Exists(CameraTest));
        Assert.Equal(2, result.LooseFiles);
    }

    [Fact]
    public void Run_GalleryFilesOfProtectedSession_AreKept()
    {
        string sessionDir = Path.Combine(Gallery, "2026-11-01", "SESSION_x");
        var file = MakeFile(sessionDir, "shot_1.jpg", _now.AddDays(-15));

        NewCleaner().Run(dir => string.Equals(dir, sessionDir, StringComparison.OrdinalIgnoreCase));

        Assert.True(File.Exists(file));
        Assert.True(Directory.Exists(sessionDir));
    }

    [Fact]
    public void Run_DeletesOnlyOldRotatedLogs_NotTheActiveLog()
    {
        var active = MakeFile(_root, "host_startup.log", _now.AddDays(-30));
        var oldRotated = MakeFile(_root, "host_startup.1.log", _now.AddDays(-8));
        var freshRotated = MakeFile(_root, "host_startup.2.log", _now.AddDays(-1));

        var result = NewCleaner().Run();

        Assert.True(File.Exists(active));
        Assert.False(File.Exists(oldRotated));
        Assert.True(File.Exists(freshRotated));
        Assert.Equal(1, result.LogFiles);
    }

    [Fact]
    public void Run_MissingDirectories_DoNotThrow()
    {
        var cleaner = new StorageCleaner(Path.Combine(_root, "nope"), new[] { Path.Combine(_root, "nope2") }, Path.Combine(_root, "nope3"), now: () => _now);
        var result = cleaner.Run();
        Assert.True(result.NothingDeleted);
    }
}
