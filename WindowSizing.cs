using Microsoft.UI.Xaml;
using System;
using System.Runtime.InteropServices;

namespace Flex
{
    class WindowSizing
    {
        (int Min, int Max) _widthBounds;
        (int Width, int Height) _ratio;
        private readonly ProcHook _hook;

        public WindowSizing(
            Window window,
            (int Min, int Max) widthBounds,
            (int Width, int Height) ratio
        )
        {
            _widthBounds = widthBounds;
            _ratio = ratio;
            _hook = new ProcHook(window, WndProc);
        }

        IntPtr? WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg != WM_SIZING)
                return null;

            var rect = Marshal.PtrToStructure<WindowRect>(lParam);

            if (wParam.ToInt64() == WMSZ_LEFT || wParam.ToInt64() == WMSZ_RIGHT)
                rect.AdjustHeight(_ratio);
            else
                rect.AdjustWidth(_ratio);

            if (rect.width < _widthBounds.Min)
            {
                rect.width = _widthBounds.Min;
                rect.AdjustHeight(_ratio);
            }
            else if (rect.width > _widthBounds.Max)
            {
                rect.width = _widthBounds.Max;
                rect.AdjustHeight(_ratio);
            }

            Marshal.StructureToPtr(rect, lParam, false);
            return new IntPtr(1);
        }

        const uint WM_SIZING = 0x0214;
        const uint WMSZ_LEFT = 1;
        const uint WMSZ_RIGHT = 2;

            #region Rect

        [StructLayout(LayoutKind.Sequential)]
        struct WindowRect
        {
            public int left;
            public int top;
            public int right;
            public int bottom;

            public int width
            {
                get => right - left;
                set => right = left + value;
            }

            public int height
            {
                get => bottom - top;
                set => bottom = top + value;
            }

            public void AdjustHeight((int Width, int Height) ratio) =>
                height = width * ratio.Height / ratio.Width;

            public void AdjustWidth((int Width, int Height) ratio) =>
                width = height * ratio.Width / ratio.Height;
        }

        #endregion

        #region ProcHook

        sealed class ProcHook
        {
            private readonly IntPtr _prevProc;
            private readonly WNDPROC _wndProc;
            private readonly Func<IntPtr, int, IntPtr, IntPtr, IntPtr?> _callback;

            public ProcHook(Window window, Func<IntPtr, int, IntPtr, IntPtr, IntPtr?> callback)
            {
                ArgumentNullException.ThrowIfNull(window);
                ArgumentNullException.ThrowIfNull(callback);

                _wndProc = WndProc;
                var handle = WinRT.Interop.WindowNative.GetWindowHandle(window);
                _callback = callback;

                const int GWLP_WNDPROC = -4;
                _prevProc = GetWindowLong(handle, GWLP_WNDPROC);
                SetWindowLong(
                    handle,
                    GWLP_WNDPROC,
                    Marshal.GetFunctionPointerForDelegate(_wndProc)
                );
            }

            private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam) =>
                _callback(hwnd, msg, wParam, lParam)
                ?? CallWindowProc(_prevProc, hwnd, msg, wParam, lParam);

            private delegate IntPtr WNDPROC(IntPtr handle, int msg, IntPtr wParam, IntPtr lParam);

            private static IntPtr GetWindowLong(IntPtr handle, int index) =>
                IntPtr.Size == 8
                    ? GetWindowLongPtrW(handle, index)
                    : (IntPtr)GetWindowLongW(handle, index);

            private static IntPtr SetWindowLong(IntPtr handle, int index, IntPtr newLong) =>
                IntPtr.Size == 8
                    ? SetWindowLongPtrW(handle, index, newLong)
                    : (IntPtr)SetWindowLongW(handle, index, newLong.ToInt32());

            [DllImport("user32")]
            static extern IntPtr CallWindowProc(
                IntPtr prevWndProc,
                IntPtr handle,
                int msg,
                IntPtr wParam,
                IntPtr lParam
            );

            [DllImport("user32")]
            static extern IntPtr GetWindowLongPtrW(IntPtr hWnd, int nIndex);

            [DllImport("user32")]
            static extern int GetWindowLongW(IntPtr hWnd, int nIndex);

            [DllImport("user32")]
            static extern int SetWindowLongW(IntPtr hWnd, int nIndexn, int dwNewLong);

            [DllImport("user32")]
            static extern IntPtr SetWindowLongPtrW(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
        }

        #endregion
    }
}
