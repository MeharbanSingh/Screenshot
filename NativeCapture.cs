using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;

namespace Screenshot
{
    public static class NativeCapture
    {
        [DllImport("gdi32.dll")]
        private static extern bool BitBlt(IntPtr hdcDest, int xDest, int yDest,
            int w, int h, IntPtr hdcSrc, int xSrc, int ySrc, int rop);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int w, int h);

        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hObj);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObj);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr hdc);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDesktopWindow();

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindowDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        private const int SRCCOPY = 0x00CC0020;

        public static BitmapSource CaptureScreenRect(Rect rect)
        {
            var desktopWnd = GetDesktopWindow();
            var desktopDc = GetWindowDC(desktopWnd);
            var memDc = CreateCompatibleDC(desktopDc);

            int width = (int)Math.Round(rect.Width);
            int height = (int)Math.Round(rect.Height);

            var bmp = CreateCompatibleBitmap(desktopDc, width, height);
            var old = SelectObject(memDc, bmp);

            BitBlt(memDc, 0, 0, width, height, desktopDc, (int)Math.Round(rect.Left), (int)Math.Round(rect.Top), SRCCOPY);

            var bitmapSource = CreateBitmapSourceFromHBitmap(bmp);

            // Cleanup
            SelectObject(memDc, old);
            DeleteObject(bmp);
            DeleteDC(memDc);
            ReleaseDC(desktopWnd, desktopDc);

            return bitmapSource;
        }

        private static BitmapSource CreateBitmapSourceFromHBitmap(IntPtr hBitmap)
        {
            var bitmap = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                hBitmap,
                IntPtr.Zero,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());

            bitmap.Freeze();
            return bitmap;
        }
    }
}