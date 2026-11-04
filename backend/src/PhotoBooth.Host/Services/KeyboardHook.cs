using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PhotoBooth.Host.Services;

public class KeyboardHook : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int VK_TAB = 0x09;
    private const int VK_ESCAPE = 0x1B;
    private const int VK_LWIN = 0x5B;
    private const int VK_RWIN = 0x5C;
    private const int VK_F4 = 0x73;

    // Prevent GC from collecting delegate
    private static LowLevelKeyboardProc? _proc;
    private IntPtr _hookId = IntPtr.Zero;
    private readonly bool _allowEscapeToExit;

    public event Action? EscapePressed;

    public KeyboardHook(bool allowEscapeToExit = true)
    {
        _allowEscapeToExit = allowEscapeToExit;
        _proc = HookCallback;
        try
        {
            _hookId = SetHook(_proc);
        }
        catch (Exception ex)
        {
            App.Log($"[WARN] SetHook failed: {ex.Message}");
        }
    }

    private IntPtr SetHook(LowLevelKeyboardProc proc)
    {
        using var curProcess = Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule;
        IntPtr hMod = GetModuleHandle(curModule?.ModuleName);
        if (hMod == IntPtr.Zero)
        {
            hMod = GetModuleHandle(null);
        }
        return SetWindowsHookEx(WH_KEYBOARD_LL, proc, hMod, 0);
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (nCode >= 0 && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
            {
                int vkCode = Marshal.ReadInt32(lParam);

                if (vkCode == VK_ESCAPE)
                {
                    EscapePressed?.Invoke();
                    if (!_allowEscapeToExit) return (IntPtr)1;
                }

                if (vkCode == VK_LWIN || vkCode == VK_RWIN)
                {
                    return (IntPtr)1;
                }

                bool isAlt = (GetAsyncKeyState(0x12) & 0x8000) != 0;
                if (isAlt && (vkCode == VK_TAB || vkCode == VK_F4))
                {
                    return (IntPtr)1;
                }
            }
        }
        catch
        {
            // Never let hook callback throw into Win32 subsystem
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);
}
