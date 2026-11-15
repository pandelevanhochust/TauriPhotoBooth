using System;
using System.Runtime.InteropServices;

namespace PhotoBooth.Hardware.Camera.Canon;

/// <summary>
/// Khai báo P/Invoke tối thiểu cho Canon EDSDK 13.x (EDSDK.dll, 64-bit).
/// Từ EDSDK 13 các kích thước stream / file là 64-bit (EdsUInt64).
/// Mọi lời gọi phải chạy trên cùng một luồng STA (xem CanonEdsdkCamera).
/// </summary>
internal static class EdsdkNative
{
    private const string Dll = "EDSDK.dll";

    // --- Mã lỗi hay gặp ---
    public const uint EDS_ERR_OK = 0x00000000;
    public const uint EDS_ERR_DEVICE_BUSY = 0x00000081;
    public const uint EDS_ERR_OBJECT_NOTREADY = 0x0000A102;
    public const uint EDS_ERR_TAKE_PICTURE_AF_NG = 0x00008D01;
    public const uint EDS_ERR_TAKE_PICTURE_NO_CARD = 0x00008D06;
    public const uint EDS_ERR_TAKE_PICTURE_CARD_NG = 0x00008D07;
    public const uint EDS_ERR_SESSION_NOT_OPEN = 0x00002003;
    public const uint EDS_ERR_COMM_DISCONNECTED = 0x000000C1;

    // --- Property ID ---
    public const uint PropID_ProductName = 0x00000002;
    public const uint PropID_BatteryLevel = 0x00000008;
    public const uint PropID_SaveTo = 0x0000000B;
    public const uint PropID_Evf_OutputDevice = 0x00000500;
    public const uint PropID_Evf_Mode = 0x00000501;

    public const uint SaveTo_Host = 2;
    /// <summary>Lưu cả thẻ nhớ và máy tính: R50 đang có thẻ thì đặt "Host" vẫn không gửi ảnh về.</summary>
    public const uint SaveTo_Both = 3;
    public const uint EvfOutputDevice_TFT = 1;
    public const uint EvfOutputDevice_PC = 2;

    // --- Lệnh máy ảnh ---
    public const uint CameraCommand_TakePicture = 0x00000000;
    public const uint CameraCommand_ExtendShutDownTimer = 0x00000001;
    public const uint CameraCommand_PressShutterButton = 0x00000004;

    public const int ShutterButton_OFF = 0x00000000;
    public const int ShutterButton_Halfway = 0x00000001;
    public const int ShutterButton_Completely_NonAF = 0x00010003;

    // --- Sự kiện ---
    public const uint ObjectEvent_All = 0x00000200;
    public const uint ObjectEvent_DirItemCreated = 0x00000204;
    public const uint ObjectEvent_DirItemRequestTransfer = 0x00000208;

    public const uint StateEvent_All = 0x00000300;
    public const uint StateEvent_Shutdown = 0x00000301;
    public const uint StateEvent_WillSoonShutDown = 0x00000303;
    public const uint StateEvent_CaptureError = 0x00000305;
    public const uint StateEvent_InternalError = 0x00000306;

    // --- Stream ---
    public const uint FileCreateDisposition_CreateAlways = 1;
    public const uint Access_ReadWrite = 2;

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate uint EdsObjectEventHandler(uint inEvent, IntPtr inRef, IntPtr inContext);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate uint EdsStateEventHandler(uint inEvent, uint inParameter, IntPtr inContext);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct EdsDeviceInfo
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szPortName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szDeviceDescription;

        public uint deviceSubType;
        public uint reserved;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct EdsDirectoryItemInfo
    {
        public ulong size;
        public int isFolder;
        public uint groupID;
        public uint option;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szFileName;

        public uint format;
        public uint dateTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct EdsCapacity
    {
        public int NumberOfFreeClusters;
        public int BytesPerSector;
        public int Reset;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct EdsVolumeInfo
    {
        /// <summary>0 = khe trống, 1 = CF, 2 = SD...</summary>
        public uint StorageType;
        public uint Access;
        public ulong MaxCapacity;
        public ulong FreeSpaceInBytes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szVolumeLabel;
    }

    [DllImport(Dll)] public static extern uint EdsGetVolumeInfo(IntPtr inVolumeRef, out EdsVolumeInfo outVolumeInfo);
    [DllImport(Dll)] public static extern uint EdsInitializeSDK();
    [DllImport(Dll)] public static extern uint EdsTerminateSDK();
    [DllImport(Dll)] public static extern uint EdsRelease(IntPtr inRef);

    [DllImport(Dll)] public static extern uint EdsGetCameraList(out IntPtr outCameraListRef);
    [DllImport(Dll)] public static extern uint EdsGetChildCount(IntPtr inRef, out int outCount);
    [DllImport(Dll)] public static extern uint EdsGetChildAtIndex(IntPtr inRef, int inIndex, out IntPtr outRef);
    [DllImport(Dll)] public static extern uint EdsGetDeviceInfo(IntPtr inCameraRef, out EdsDeviceInfo outDeviceInfo);

    [DllImport(Dll)] public static extern uint EdsOpenSession(IntPtr inCameraRef);
    [DllImport(Dll)] public static extern uint EdsCloseSession(IntPtr inCameraRef);

    [DllImport(Dll)] public static extern uint EdsGetPropertySize(IntPtr inRef, uint inPropertyID, int inParam, out uint outDataType, out int outSize);
    [DllImport(Dll)] public static extern uint EdsGetPropertyData(IntPtr inRef, uint inPropertyID, int inParam, int inPropertySize, out uint outPropertyData);
    [DllImport(Dll)] public static extern uint EdsGetPropertyData(IntPtr inRef, uint inPropertyID, int inParam, int inPropertySize, IntPtr outPropertyData);
    [DllImport(Dll)] public static extern uint EdsSetPropertyData(IntPtr inRef, uint inPropertyID, int inParam, int inPropertySize, ref uint inPropertyData);

    [DllImport(Dll)] public static extern uint EdsSendCommand(IntPtr inCameraRef, uint inCommand, int inParam);
    [DllImport(Dll)] public static extern uint EdsSetCapacity(IntPtr inCameraRef, EdsCapacity inCapacity);

    [DllImport(Dll)] public static extern uint EdsSetObjectEventHandler(IntPtr inCameraRef, uint inEvent, EdsObjectEventHandler? inObjectEventHandler, IntPtr inContext);
    [DllImport(Dll)] public static extern uint EdsSetCameraStateEventHandler(IntPtr inCameraRef, uint inEvent, EdsStateEventHandler? inStateEventHandler, IntPtr inContext);
    [DllImport(Dll)] public static extern uint EdsGetEvent();

    [DllImport(Dll)] public static extern uint EdsGetDirectoryItemInfo(IntPtr inDirItemRef, out EdsDirectoryItemInfo outDirItemInfo);
    [DllImport(Dll)] public static extern uint EdsDownload(IntPtr inDirItemRef, ulong inReadSize, IntPtr outStream);
    [DllImport(Dll)] public static extern uint EdsDownloadCancel(IntPtr inDirItemRef);
    [DllImport(Dll)] public static extern uint EdsDownloadComplete(IntPtr inDirItemRef);

    [DllImport(Dll, CharSet = CharSet.Unicode)]
    public static extern uint EdsCreateFileStreamEx(string inFileName, uint inCreateDisposition, uint inDesiredAccess, out IntPtr outStream);

    [DllImport(Dll)] public static extern uint EdsCreateMemoryStream(ulong inBufferSize, out IntPtr outStream);
    [DllImport(Dll)] public static extern uint EdsGetPointer(IntPtr inStreamRef, out IntPtr outPointer);
    [DllImport(Dll)] public static extern uint EdsGetLength(IntPtr inStreamRef, out ulong outLength);

    [DllImport(Dll)] public static extern uint EdsCreateEvfImageRef(IntPtr inStreamRef, out IntPtr outEvfImageRef);
    [DllImport(Dll)] public static extern uint EdsDownloadEvfImage(IntPtr inCameraRef, IntPtr inEvfImageRef);

    // --- Bơm message Win32: EDSDK trên Windows gửi sự kiện qua hàng đợi message của luồng gọi SDK ---
    [StructLayout(LayoutKind.Sequential)]
    public struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int ptX;
        public int ptY;
    }

    public const uint PM_REMOVE = 0x0001;

    [DllImport("user32.dll")] public static extern bool PeekMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);
    [DllImport("user32.dll")] public static extern bool TranslateMessage(ref MSG lpMsg);
    [DllImport("user32.dll")] public static extern IntPtr DispatchMessage(ref MSG lpMsg);

    public static string Hex(uint err) => $"0x{err:X8}";
}
