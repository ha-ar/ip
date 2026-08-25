using System.Configuration;
using System.Data;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using Microsoft.Win32;

namespace ip
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        // StartupUri was removed from App.xaml so the correct theme dictionary
        // can be merged in BEFORE MainWindow is constructed - if we let WPF's
        // StartupUri auto-create the window, every DynamicResource lookup on
        // the very first frame would have already resolved against whatever
        // was merged at XAML-parse time (always Theme.Light.xaml).
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            ApplyTheme(IsSystemDarkTheme());

            var mainWindow = new MainWindow();
            MainWindow = mainWindow;
            mainWindow.Show();
        }

        /// <summary>
        /// Reads the "apps use dark theme" preference Windows stores for the
        /// signed-in user. Defaults to light (false) if the key is missing or
        /// unreadable - e.g. Windows versions predating the light/dark toggle.
        /// </summary>
        public static bool IsSystemDarkTheme()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                // AppsUseLightTheme: 1 (or missing) = light, 0 = dark.
                var value = key?.GetValue("AppsUseLightTheme");
                return value is int intValue && intValue == 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Swaps the merged theme dictionary at runtime. Every color in the
        /// app is consumed via DynamicResource specifically so this takes
        /// effect immediately, with no window re-creation needed. Called once
        /// at startup and again by MainWindow whenever Windows broadcasts a
        /// theme change (see WindowBackdrop.WatchForThemeChange).
        /// </summary>
        public static void ApplyTheme(bool dark)
        {
            var uri = new Uri(dark ? "Theme.Dark.xaml" : "Theme.Light.xaml", UriKind.Relative);
            Current.Resources.MergedDictionaries[0] = new ResourceDictionary { Source = uri };
        }
    }

    public class EmptyStringToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string text = value as string;
            return string.IsNullOrEmpty(text) ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
