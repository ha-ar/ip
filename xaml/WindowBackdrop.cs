using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

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
    }
}
