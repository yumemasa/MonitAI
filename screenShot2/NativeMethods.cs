using System;
using System.Runtime.InteropServices;

namespace screenShot2
{
    internal static class NativeMethods
    {
        // 画面ロック
        [DllImport("user32.dll")]
        public static extern bool LockWorkStation();

        // マウス操作
        [DllImport("user32.dll")]
        public static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        public static extern bool SetCursorPos(int X, int Y);

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X; public int Y; }

        // グレースケール (Magnification API)
        [DllImport("Magnification.dll", SetLastError = true)]
        public static extern bool MagInitialize();

        [DllImport("Magnification.dll", SetLastError = true)]
        public static extern bool MagUninitialize();

        [DllImport("Magnification.dll", SetLastError = true)]
        public static extern bool MagSetFullscreenColorEffect(ref MAGCOLOREFFECT pEffect);

        public struct MAGCOLOREFFECT
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 25)]
            public float[] transform;
        }

        // キーボードフック
        public const int WH_KEYBOARD_LL = 13;
        public const int WM_KEYDOWN = 0x0100;

        public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll")]
        public static extern IntPtr GetModuleHandle(string lpModuleName);
        
        // DPI設定
        [DllImport("user32.dll")]
        public static extern bool SetProcessDPIAware();
    }
}