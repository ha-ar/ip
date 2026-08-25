using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace ip.xaml
{
    /// <summary>
    /// Applies the Windows 11 rounded window corners to a WPF window. Only
    /// touches the native window frame via DWM - never disables the normal
    /// title bar / min-max-close buttons or resize behavior. On Windows 10,
    /// or where the DWM API is unavailable (VMs, remote sessions), this call
    /// safely no-ops and the window keeps the flat, square-cornered frame.
    ///
    /// Deliberately does not request a Mica/Acrylic system backdrop: DWM can
    /// report success for that without actually compositing the blur (common
    /// in VMs and over Remote Desktop), which leaves the window painted flat
    /// black instead of the intended glass effect. The flat background from
    /// App.xaml is used instead, which always renders correctly.
    /// </summary>
    public static class WindowBackdrop
    {
        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMWA_CAPTION_COLOR = 35;
        private const int DWMWA_TEXT_COLOR = 36;
        private const int WM_SETTINGCHANGE = 0x001A;

        private enum DWM_WINDOW_CORNER_PREFERENCE
        {
            DWMWCP_DEFAULT = 0,
            DWMWCP_DONOTROUND = 1,
            DWMWCP_ROUND = 2,
            DWMWCP_ROUNDSMALL = 3
        }

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

        /// <summary>
        /// Call from the window's SourceInitialized event (after the native
        /// handle exists, before the window is shown) so the rounded corners
        /// are in place for the very first frame.
        /// </summary>
        public static void Apply(Window window)
        {
            try
            {
                var hwnd = new WindowInteropHelper(window).Handle;
                if (hwnd == IntPtr.Zero)
                    return;

                int corner = (int)DWM_WINDOW_CORNER_PREFERENCE.DWMWCP_ROUND;
                DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));
            }
            catch
            {
                // Older Windows / API unavailable - keep the normal square-cornered frame.
            }
        }

        /// <summary>
        /// Tints the native title bar to match the app's current theme
        /// (WindowBackgroundBrush / TextPrimaryBrush) instead of leaving it
        /// the plain default OS color, so the title bar reads as part of the
        /// same "glass" surface as the window content below it. Reads colors
        /// from Application resources at call time rather than hardcoding
        /// light/dark values here, so it always matches whatever App.xaml.cs
        /// currently has merged in. Requires Windows 11 build 22000+; safely
        /// no-ops (title bar keeps the default OS color) on anything older.
        /// </summary>
        public static void ApplyCaptionColor(Window window)
        {
            try
            {
                var hwnd = new WindowInteropHelper(window).Handle;
                if (hwnd == IntPtr.Zero)
                    return;

                if (Application.Current?.TryFindResource("WindowBackgroundBrush") is SolidColorBrush bg)
                {
                    int captionColor = ToColorRef(bg.Color);
                    DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref captionColor, sizeof(int));
                }

                if (Application.Current?.TryFindResource("TextPrimaryBrush") is SolidColorBrush fg)
                {
                    int textColor = ToColorRef(fg.Color);
                    DwmSetWindowAttribute(hwnd, DWMWA_TEXT_COLOR, ref textColor, sizeof(int));
                }
            }
            catch
            {
                // Windows 10, or a pre-22000 Windows 11 build - title bar keeps the default OS color.
            }
        }

        // Win32 COLORREF is 0x00BBGGRR - the reverse byte order from the ARGB
        // WPF Color we're reading it from.
        private static int ToColorRef(Color c) => c.R | (c.G << 8) | (c.B << 16);

        /// <summary>
        /// Subscribes to WM_SETTINGCHANGE and invokes <paramref name="onThemeChange"/>
        /// whenever Windows broadcasts that the system light/dark app theme
        /// changed (identified by the lParam string "ImmersiveColorSet") -
        /// this is how the app repaints live if the user flips their Windows
        /// theme while it's running, without needing a restart. Safe no-op if
        /// the window's native handle/HwndSource isn't available yet.
        /// </summary>
        public static void WatchForThemeChange(Window window, Action onThemeChange)
        {
            try
            {
                var hwnd = new WindowInteropHelper(window).Handle;
                if (hwnd == IntPtr.Zero)
                    return;

                var source = HwndSource.FromHwnd(hwnd);
                if (source == null)
                    return;

                IntPtr Hook(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
                {
                    if (msg == WM_SETTINGCHANGE && lParam != IntPtr.Zero &&
                        Marshal.PtrToStringUni(lParam) == "ImmersiveColorSet")
                    {
                        onThemeChange();
                    }
                    return IntPtr.Zero;
                }

                source.AddHook(Hook);
                window.Closed += (_, _) => source.RemoveHook(Hook);
            }
            catch
            {
                // No native handle / HwndSource available - the app just won't live-follow
                // an OS theme change; it still picks up the right theme on next launch.
            }
        }
    }
}
