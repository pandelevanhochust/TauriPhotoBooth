using System;
using System.Runtime.InteropServices;

namespace PhotoBooth.Imaging.Video;

/// <summary>
/// Ghi video MP4 (H.264) bằng Windows Media Foundation Sink Writer: dùng bộ mã hoá có sẵn của Windows,
/// không cần ffmpeg. Đầu vào là khung RGB32 (BGRA, xếp từ dưới lên như DIB) kích thước cố định.
/// Phải tạo và dùng trên cùng một luồng đã CoInitializeEx.
/// </summary>
internal sealed class MediaFoundationH264Writer : IDisposable
{
    private const int MF_VERSION = 0x00020070;
    private const int MFSTARTUP_FULL = 0;
    private const int MFVideoInterlace_Progressive = 2;

    private static readonly Guid MF_MT_MAJOR_TYPE = new("48EBA18E-F8C9-4687-BF11-0A74C9F96A8F");
    private static readonly Guid MF_MT_SUBTYPE = new("F7E34C9A-42E8-4714-B74B-CB29D72C35E5");
    private static readonly Guid MF_MT_FRAME_SIZE = new("1652C33D-D6B2-4012-B834-72030849A37D");
    private static readonly Guid MF_MT_FRAME_RATE = new("C459A2E8-3D2C-4E44-B132-FEE5156C7BB0");
    private static readonly Guid MF_MT_PIXEL_ASPECT_RATIO = new("C6376A1E-8D0A-4027-BE45-6D9A0AD39BB6");
    private static readonly Guid MF_MT_INTERLACE_MODE = new("E2724BB8-E676-4806-B4B2-A8D6EFB44CCD");
    private static readonly Guid MF_MT_AVG_BITRATE = new("20332624-FB0D-4D9E-BD0D-CBF6786C102E");
    private static readonly Guid MFMediaType_Video = new("73646976-0000-0010-8000-00AA00389B71");
    private static readonly Guid MFVideoFormat_H264 = new("34363248-0000-0010-8000-00AA00389B71");
    private static readonly Guid MFVideoFormat_RGB32 = new("00000016-0000-0010-8000-00AA00389B71");

    private readonly IMFSinkWriter _writer;
    private readonly int _stream;
    private readonly long _frameDuration;
    private long _frameIndex;
    private bool _finalized;
    private bool _disposed;

    public int Width { get; }
    public int Height { get; }
    public long FramesWritten => _frameIndex;

    public MediaFoundationH264Writer(string outputPath, int width, int height, int fps)
    {
        if (width <= 0 || height <= 0 || width % 2 != 0 || height % 2 != 0)
        {
            throw new ArgumentException("Kích thước video phải là số chẵn dương");
        }
        Width = width;
        Height = height;
        fps = Math.Max(1, fps);
        _frameDuration = 10_000_000L / fps;
        int bitrate = (int)Math.Clamp(width * (double)height * fps * 0.2, 2_000_000, 8_000_000);

        Check(MFStartup(MF_VERSION, MFSTARTUP_FULL));
        IMFMediaType? output = null, input = null;
        try
        {
            Check(MFCreateSinkWriterFromURL(outputPath, IntPtr.Zero, IntPtr.Zero, out _writer));

            Check(MFCreateMediaType(out output));
            SetGuid(output, MF_MT_MAJOR_TYPE, MFMediaType_Video);
            SetGuid(output, MF_MT_SUBTYPE, MFVideoFormat_H264);
            Check(output.SetUINT32(MF_MT_AVG_BITRATE, bitrate));
            Check(output.SetUINT32(MF_MT_INTERLACE_MODE, MFVideoInterlace_Progressive));
            Check(output.SetUINT64(MF_MT_FRAME_SIZE, Pack(width, height)));
            Check(output.SetUINT64(MF_MT_FRAME_RATE, Pack(fps, 1)));
            Check(output.SetUINT64(MF_MT_PIXEL_ASPECT_RATIO, Pack(1, 1)));
            Check(_writer.AddStream(output, out _stream));

            Check(MFCreateMediaType(out input));
            SetGuid(input, MF_MT_MAJOR_TYPE, MFMediaType_Video);
            SetGuid(input, MF_MT_SUBTYPE, MFVideoFormat_RGB32);
            Check(input.SetUINT32(MF_MT_INTERLACE_MODE, MFVideoInterlace_Progressive));
            Check(input.SetUINT64(MF_MT_FRAME_SIZE, Pack(width, height)));
            Check(input.SetUINT64(MF_MT_FRAME_RATE, Pack(fps, 1)));
            Check(input.SetUINT64(MF_MT_PIXEL_ASPECT_RATIO, Pack(1, 1)));
            Check(_writer.SetInputMediaType(_stream, input, IntPtr.Zero));

            Check(_writer.BeginWriting());
        }
        catch
        {
            if (_writer != null) Marshal.ReleaseComObject(_writer);
            MFShutdown();
            throw;
        }
        finally
        {
            if (input != null) Marshal.ReleaseComObject(input);
            if (output != null) Marshal.ReleaseComObject(output);
        }
    }

    /// <summary>Ghi một khung BGRA (Width*Height*4 byte, hàng dưới cùng trước).</summary>
    public void WriteFrame(byte[] bgraBottomUp)
    {
        if (_finalized || _disposed) return;
        if (bgraBottomUp.Length != Width * Height * 4) throw new ArgumentException("Sai kích thước khung");

        IMFMediaBuffer? buffer = null;
        IMFSample? sample = null;
        try
        {
            Check(MFCreateMemoryBuffer(bgraBottomUp.Length, out buffer));
            Check(buffer.Lock(out IntPtr data, out _, out _));
            try
            {
                Marshal.Copy(bgraBottomUp, 0, data, bgraBottomUp.Length);
            }
            finally
            {
                buffer.Unlock();
            }
            Check(buffer.SetCurrentLength(bgraBottomUp.Length));

            Check(MFCreateSample(out sample));
            Check(sample.AddBuffer(buffer));
            Check(sample.SetSampleTime(_frameIndex * _frameDuration));
            Check(sample.SetSampleDuration(_frameDuration));
            Check(_writer.WriteSample(_stream, sample));
            _frameIndex++;
        }
        finally
        {
            if (sample != null) Marshal.ReleaseComObject(sample);
            if (buffer != null) Marshal.ReleaseComObject(buffer);
        }
    }

    /// <summary>Kết thúc và ghi chỉ mục MP4. Không gọi thì file không phát được.</summary>
    public void FinalizeWriter()
    {
        if (_finalized || _disposed) return;
        _finalized = true;
        Check(_writer.FinalizeWriter());
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Marshal.ReleaseComObject(_writer);
        MFShutdown();
    }

    private static long Pack(int high, int low) => (long)(((ulong)(uint)high << 32) | (uint)low);

    private static void SetGuid(IMFMediaType type, Guid key, Guid value) => Check(type.SetGUID(key, value));

    private static void Check(int hr)
    {
        if (hr < 0) Marshal.ThrowExceptionForHR(hr);
    }

    // ------------------------------------------------------------------ COM interop (mfplat / mfreadwrite)

    [DllImport("mfplat.dll", ExactSpelling = true)] private static extern int MFStartup(int version, int flags);
    [DllImport("mfplat.dll", ExactSpelling = true)] private static extern int MFShutdown();
    [DllImport("mfplat.dll", ExactSpelling = true)] private static extern int MFCreateMediaType(out IMFMediaType type);
    [DllImport("mfplat.dll", ExactSpelling = true)] private static extern int MFCreateMemoryBuffer(int maxLength, out IMFMediaBuffer buffer);
    [DllImport("mfplat.dll", ExactSpelling = true)] private static extern int MFCreateSample(out IMFSample sample);

    [DllImport("mfreadwrite.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int MFCreateSinkWriterFromURL(string url, IntPtr byteStream, IntPtr attributes, out IMFSinkWriter writer);

    // IMFAttributes giữ đủ thứ tự vtable; chỉ những hàm dùng tới mới có kiểu tham số cụ thể.
    [ComImport, Guid("44AE0FA8-EA31-4109-8D2E-4CAE4997C555"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMFMediaType
    {
        [PreserveSig] int GetItem(); [PreserveSig] int GetItemType(); [PreserveSig] int CompareItem(); [PreserveSig] int Compare();
        [PreserveSig] int GetUINT32(); [PreserveSig] int GetUINT64(); [PreserveSig] int GetDouble(); [PreserveSig] int GetGUID();
        [PreserveSig] int GetStringLength(); [PreserveSig] int GetString(); [PreserveSig] int GetAllocatedString();
        [PreserveSig] int GetBlobSize(); [PreserveSig] int GetBlob(); [PreserveSig] int GetAllocatedBlob(); [PreserveSig] int GetUnknown();
        [PreserveSig] int SetItem(); [PreserveSig] int DeleteItem(); [PreserveSig] int DeleteAllItems();
        [PreserveSig] int SetUINT32([In, MarshalAs(UnmanagedType.LPStruct)] Guid key, int value);
        [PreserveSig] int SetUINT64([In, MarshalAs(UnmanagedType.LPStruct)] Guid key, long value);
        [PreserveSig] int SetDouble();
        [PreserveSig] int SetGUID([In, MarshalAs(UnmanagedType.LPStruct)] Guid key, [In, MarshalAs(UnmanagedType.LPStruct)] Guid value);
        // Các hàm còn lại của IMFAttributes / IMFMediaType không dùng tới
    }

    [ComImport, Guid("045FA593-8799-42B8-BC8D-8968C6453507"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMFMediaBuffer
    {
        [PreserveSig] int Lock(out IntPtr buffer, out int maxLength, out int currentLength);
        [PreserveSig] int Unlock();
        [PreserveSig] int GetCurrentLength(out int length);
        [PreserveSig] int SetCurrentLength(int length);
        [PreserveSig] int GetMaxLength(out int length);
    }

    [ComImport, Guid("C40A00F2-B93A-4D80-AE8C-5A1C634F58E4"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMFSample
    {
        // 30 hàm của IMFAttributes
        [PreserveSig] int GetItem(); [PreserveSig] int GetItemType(); [PreserveSig] int CompareItem(); [PreserveSig] int Compare();
        [PreserveSig] int GetUINT32(); [PreserveSig] int GetUINT64(); [PreserveSig] int GetDouble(); [PreserveSig] int GetGUID();
        [PreserveSig] int GetStringLength(); [PreserveSig] int GetString(); [PreserveSig] int GetAllocatedString();
        [PreserveSig] int GetBlobSize(); [PreserveSig] int GetBlob(); [PreserveSig] int GetAllocatedBlob(); [PreserveSig] int GetUnknown();
        [PreserveSig] int SetItem(); [PreserveSig] int DeleteItem(); [PreserveSig] int DeleteAllItems();
        [PreserveSig] int SetUINT32(); [PreserveSig] int SetUINT64(); [PreserveSig] int SetDouble(); [PreserveSig] int SetGUID();
        [PreserveSig] int SetString(); [PreserveSig] int SetBlob(); [PreserveSig] int SetUnknown();
        [PreserveSig] int LockStore(); [PreserveSig] int UnlockStore(); [PreserveSig] int GetCount(); [PreserveSig] int GetItemByIndex();
        [PreserveSig] int CopyAllItems();
        // IMFSample
        [PreserveSig] int GetSampleFlags(out int flags);
        [PreserveSig] int SetSampleFlags(int flags);
        [PreserveSig] int GetSampleTime(out long time);
        [PreserveSig] int SetSampleTime(long time);
        [PreserveSig] int GetSampleDuration(out long duration);
        [PreserveSig] int SetSampleDuration(long duration);
        [PreserveSig] int GetBufferCount(out int count);
        [PreserveSig] int GetBufferByIndex(int index, out IMFMediaBuffer buffer);
        [PreserveSig] int ConvertToContiguousBuffer(out IMFMediaBuffer buffer);
        [PreserveSig] int AddBuffer(IMFMediaBuffer buffer);
    }

    [ComImport, Guid("3137F1CD-FE5E-4805-A5D8-FB477448CB3D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMFSinkWriter
    {
        [PreserveSig] int AddStream(IMFMediaType targetType, out int streamIndex);
        [PreserveSig] int SetInputMediaType(int streamIndex, IMFMediaType inputType, IntPtr encodingParameters);
        [PreserveSig] int BeginWriting();
        [PreserveSig] int WriteSample(int streamIndex, IMFSample sample);
        [PreserveSig] int SendStreamTick(int streamIndex, long timestamp);
        [PreserveSig] int PlaceMarker(int streamIndex, IntPtr context);
        [PreserveSig] int NotifyEndOfSegment(int streamIndex);
        [PreserveSig] int Flush(int streamIndex);
        [PreserveSig] int FinalizeWriter();
    }
}
