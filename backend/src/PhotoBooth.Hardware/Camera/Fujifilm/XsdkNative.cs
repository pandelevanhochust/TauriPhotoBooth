using System;
using System.IO;
using System.Runtime.InteropServices;

namespace PhotoBooth.Hardware.Camera.Fujifilm;

/// <summary>
/// Nạp FUJIFILM X SDK (XAPI.dll 64-bit, bản 1.17) và các hàm cần cho chụp tethered.
/// XAPI.dll tự nạp các module theo dòng máy (FF00xxAPI.dll, FTLPTP.dll, XSDK.DAT) cùng thư mục.
/// Chữ ký hàm theo FUJIFILM X SDK (kiểu long của C trên Windows là 32-bit).
/// </summary>
internal sealed class XsdkNative : IDisposable
{
    // --- Hằng số ---
    public const int XSDK_COMPLETE = 0;
    public const int XSDK_DSC_IF_USB = 1;
    public const int XSDK_DSC_MODE_TETHER = 1;
    public const int XSDK_PRIORITY_PC = 2;

    public const int XSDK_MEDIAREC_RAWJPEG = 1;
    public const int XSDK_MEDIAREC_JPEG = 3;

    public const int XSDK_IMAGEFORMAT_RAW = 1;
    public const int XSDK_IMAGEFORMAT_LIVE = 4;
    public const int XSDK_IMAGEFORMAT_NONE = 5;
    public const int XSDK_IMAGEFORMAT_JPEG = 7;
    public const int XSDK_IMAGEFORMAT_HEIF = 18;

    public const int XSDK_RELEASE_N_S1OFF = 0x0004;
    public const int XSDK_RELEASE_SHOOT_S1OFF = 0x0104;
    public const int XSDK_RELEASE_S1ON = 0x0200;
    public const int XSDK_RELEASE_S2_S1OFF = 0x0304;

    public const int API_CODE_StartLiveView = 0x3301;
    public const int API_CODE_StopLiveView = 0x3302;
    public const int API_CODE_SetLiveViewImageQuality = 0x3323;
    public const int API_CODE_SetLiveViewImageSize = 0x3325;
    public const int API_CODE_SetLiveViewMode = 0x332F;
    public const int API_CODE_SetAutoPowerOffSetting = 0x411B;
    public const int API_CODE_SetCustomAutoPowerOff = 0x4229;
    public const int SDK_AUTOPOWEROFF_OFF = 3;

    // --- Mã lỗi (XSDK_GetErrorNumber) ---
    public const int XSDK_ERRCODE_BUSY = 0x1006;
    public const int XSDK_ERRCODE_AF_TIMEOUT = 0x1007;
    public const int XSDK_ERRCODE_SHOOT_ERROR = 0x1008;
    public const int XSDK_ERRCODE_FRAME_FULL = 0x1009;
    public const int XSDK_ERRCODE_NO_MODEL_MODULE = 0x1012;
    /// <summary>Chế độ USB của máy không cho điều khiển từ PC (đang ở Card Reader / Webcam / RAW Conv.).</summary>
    public const int XSDK_ERRCODE_INVALID_USBMODE = 0x1015;
    public const int XSDK_ERRCODE_FORCEMODE_BUSY = 0x1016;
    public const int XSDK_ERRCODE_RUNNING_OTHER_FUNCTION = 0x1017;
    public const int XSDK_ERRCODE_COMMUNICATION = 0x2001;
    public const int XSDK_ERRCODE_TIMEOUT = 0x2002;

    [StructLayout(LayoutKind.Sequential, Pack = 1, CharSet = CharSet.Ansi)]
    public struct XSDK_DeviceInformation
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string strVendor;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string strManufacturer;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string strProduct;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string strFirmware;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string strDeviceType;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string strSerialNo;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string strFramework;
        public byte bDeviceId;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string strDeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string strYNo;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1, CharSet = CharSet.Ansi)]
    public struct XSDK_ImageInformation
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string strInternalName;
        public int lFormat;
        public int lDataSize;
        public int lImagePixHeight;
        public int lImagePixWidth;
        public int lImageBitDepth;
        public int lPreviewSize;
        public IntPtr hImage;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int InitFn(IntPtr hLib);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ExitFn();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)] private delegate int DetectFn(int lInterface, string pInterface, string pDeviceName, ref int plCount);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)] private delegate int OpenExFn(string pDevice, ref IntPtr phCamera, ref int plCameraMode, IntPtr pOption);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int HandleFn(IntPtr hCamera);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetErrorNumberFn(IntPtr hCamera, ref int plAPICode, ref int plERRCode);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetDeviceInfoFn(IntPtr hCamera, ref XSDK_DeviceInformation pDevInfo);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int CapListFn(IntPtr hCamera, ref int plNum, IntPtr plValues);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SetIntFn(IntPtr hCamera, int lValue);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetIntFn(IntPtr hCamera, ref int plValue);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ReleaseFn(IntPtr hCamera, int lReleaseMode, ref int plShotOpt, ref int plAfStatus);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ReadImageInfoFn(IntPtr hCamera, ref XSDK_ImageInformation pImgInfo);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ReadImageFn(IntPtr hCamera, IntPtr pData, uint lDataSize);
    // XSDK_SetProp là hàm variadic: mỗi số lượng tham số cần một kiểu delegate riêng
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SetProp0Fn(IntPtr hCamera, int lAPICode, int lAPIParam);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SetProp1Fn(IntPtr hCamera, int lAPICode, int lAPIParam, int param1);

    private readonly IntPtr _module;
    private readonly InitFn _init;
    private readonly ExitFn _exit;
    private readonly DetectFn _detect;
    private readonly OpenExFn _openEx;
    private readonly HandleFn _close;
    private readonly GetErrorNumberFn _getErrorNumber;
    private readonly GetIntFn _getErrorDetails;
    private readonly GetDeviceInfoFn _getDeviceInfo;
    private readonly CapListFn _capPriorityMode;
    private readonly SetIntFn _setPriorityMode;
    private readonly GetIntFn _getPriorityMode;
    private readonly CapListFn _capRelease;
    private readonly ReleaseFn _release;
    private readonly ReadImageInfoFn _readImageInfo;
    private readonly ReadImageFn _readImage;
    private readonly HandleFn _deleteImage;
    private readonly CapListFn _capMediaRecord;
    private readonly SetIntFn _setMediaRecord;
    private readonly SetProp0Fn _setProp0;
    private readonly SetProp1Fn _setProp1;

    public string LibraryPath { get; }

    private XsdkNative(string path, IntPtr module)
    {
        LibraryPath = path;
        _module = module;
        _init = Get<InitFn>("XSDK_Init");
        _exit = Get<ExitFn>("XSDK_Exit");
        _detect = Get<DetectFn>("XSDK_Detect");
        _openEx = Get<OpenExFn>("XSDK_OpenEx");
        _close = Get<HandleFn>("XSDK_Close");
        _getErrorNumber = Get<GetErrorNumberFn>("XSDK_GetErrorNumber");
        _getErrorDetails = Get<GetIntFn>("XSDK_GetErrorDetails");
        _getDeviceInfo = Get<GetDeviceInfoFn>("XSDK_GetDeviceInfo");
        _capPriorityMode = Get<CapListFn>("XSDK_CapPriorityMode");
        _setPriorityMode = Get<SetIntFn>("XSDK_SetPriorityMode");
        _getPriorityMode = Get<GetIntFn>("XSDK_GetPriorityMode");
        _capRelease = Get<CapListFn>("XSDK_CapRelease");
        _release = Get<ReleaseFn>("XSDK_Release");
        _readImageInfo = Get<ReadImageInfoFn>("XSDK_ReadImageInfo");
        _readImage = Get<ReadImageFn>("XSDK_ReadImage");
        _deleteImage = Get<HandleFn>("XSDK_DeleteImage");
        _capMediaRecord = Get<CapListFn>("XSDK_CapMediaRecord");
        _setMediaRecord = Get<SetIntFn>("XSDK_SetMediaRecord");
        _setProp0 = Get<SetProp0Fn>("XSDK_SetProp");
        _setProp1 = Get<SetProp1Fn>("XSDK_SetProp");
    }

    /// <summary>Nạp XAPI.dll trong thư mục cho trước. Ném DllNotFoundException / EntryPointNotFoundException nếu thiếu.</summary>
    public static XsdkNative Load(string directory)
    {
        string path = Path.Combine(directory, "XAPI.dll");
        if (!File.Exists(path)) throw new DllNotFoundException($"Không thấy {path}");
        IntPtr module = NativeLibrary.Load(path);
        try
        {
            return new XsdkNative(path, module);
        }
        catch
        {
            NativeLibrary.Free(module);
            throw;
        }
    }

    private T Get<T>(string name) where T : Delegate
    {
        if (!NativeLibrary.TryGetExport(_module, name, out IntPtr fn))
        {
            throw new EntryPointNotFoundException($"XAPI.dll thiếu hàm {name}");
        }
        return Marshal.GetDelegateForFunctionPointer<T>(fn);
    }

    public int Init() => _init(_module);
    public int Exit() => _exit();

    public int Detect(ref int count) => _detect(XSDK_DSC_IF_USB, string.Empty, string.Empty, ref count);
    public int OpenEx(string device, ref IntPtr hCamera, ref int cameraMode) => _openEx(device, ref hCamera, ref cameraMode, IntPtr.Zero);
    public int Close(IntPtr hCamera) => _close(hCamera);

    public int GetDeviceInfo(IntPtr hCamera, ref XSDK_DeviceInformation info) => _getDeviceInfo(hCamera, ref info);

    public int SetPriorityMode(IntPtr hCamera, int mode) => _setPriorityMode(hCamera, mode);
    public int GetPriorityMode(IntPtr hCamera, ref int mode) => _getPriorityMode(hCamera, ref mode);
    public int[]? CapPriorityMode(IntPtr hCamera) => CapList(_capPriorityMode, hCamera);
    public int[]? CapRelease(IntPtr hCamera) => CapList(_capRelease, hCamera);
    public int[]? CapMediaRecord(IntPtr hCamera) => CapList(_capMediaRecord, hCamera);
    public int SetMediaRecord(IntPtr hCamera, int mode) => _setMediaRecord(hCamera, mode);

    public int Release(IntPtr hCamera, int releaseMode)
    {
        int shotOpt = 1, afStatus = 0;
        return _release(hCamera, releaseMode, ref shotOpt, ref afStatus);
    }

    public int ReadImageInfo(IntPtr hCamera, ref XSDK_ImageInformation info) => _readImageInfo(hCamera, ref info);
    public int DeleteImage(IntPtr hCamera) => _deleteImage(hCamera);

    /// <summary>Đọc dữ liệu ảnh đang đứng đầu bộ đệm của máy (kích thước lấy từ ReadImageInfo).</summary>
    public byte[]? ReadImage(IntPtr hCamera, int size)
    {
        if (size <= 0) return null;
        IntPtr buffer = Marshal.AllocCoTaskMem(size);
        try
        {
            if (_readImage(hCamera, buffer, (uint)size) != XSDK_COMPLETE) return null;
            var data = new byte[size];
            Marshal.Copy(buffer, data, 0, size);
            return data;
        }
        finally
        {
            Marshal.FreeCoTaskMem(buffer);
        }
    }

    public int SetProp(IntPtr hCamera, int apiCode) => _setProp0(hCamera, apiCode, 0);
    public int SetProp(IntPtr hCamera, int apiCode, int value) => _setProp1(hCamera, apiCode, 1, value);

    /// <summary>Lỗi gần nhất của lần mở kết nối thất bại (handle = NULL theo tài liệu X SDK).</summary>
    public (int ApiCode, int ErrorCode) OpenError()
    {
        int api = 0, err = 0;
        return _getErrorNumber(IntPtr.Zero, ref api, ref err) == XSDK_COMPLETE ? (api, err) : (0, 0);
    }

    /// <summary>Mô tả lỗi gần nhất của máy: "API 0x..., error 0x...".</summary>
    public (int ApiCode, int ErrorCode, string Text) LastError(IntPtr hCamera)
    {
        if (hCamera == IntPtr.Zero) return (0, 0, "camera handle is closed");
        int api = 0, err = 0;
        if (_getErrorNumber(hCamera, ref api, ref err) != XSDK_COMPLETE) return (0, 0, "XSDK_GetErrorNumber failed");
        string text = $"API 0x{api:X}, error 0x{err:X}";
        if (err == XSDK_ERRCODE_RUNNING_OTHER_FUNCTION)
        {
            int detail = 0;
            if (_getErrorDetails(hCamera, ref detail) == XSDK_COMPLETE) text += $", detail 0x{detail:X}";
        }
        return (api, err, text);
    }

    private static int[]? CapList(CapListFn fn, IntPtr hCamera)
    {
        int count = 0;
        if (fn(hCamera, ref count, IntPtr.Zero) != XSDK_COMPLETE || count <= 0) return null;
        IntPtr buffer = Marshal.AllocCoTaskMem(sizeof(int) * count);
        try
        {
            int filled = count;
            if (fn(hCamera, ref filled, buffer) != XSDK_COMPLETE) return null;
            var values = new int[Math.Min(count, Math.Max(0, filled))];
            Marshal.Copy(buffer, values, 0, values.Length);
            return values;
        }
        finally
        {
            Marshal.FreeCoTaskMem(buffer);
        }
    }

    public void Dispose()
    {
        NativeLibrary.Free(_module);
    }
}
