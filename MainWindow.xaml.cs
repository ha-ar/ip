#pragma warning disable CA1416 // Validate platform compatibility

using CsvHelper;
using ip.models;
using ip.xaml;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using Microsoft.Win32;
using SkiaSharp;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Management;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.NetworkInformation;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ip
{
    public partial class MainWindow : Window
    {
        #region WindowViewModel

        public class WindowViewModel : INotifyPropertyChanged
        {
            #region Instance fields

            private static MainWindow? window;

            #endregion

            #region Constructor

            public WindowViewModel(MainWindow window)
            {
                WindowViewModel.window = window;
            }

            #endregion

            #region PropertyChanged

            public event PropertyChangedEventHandler? PropertyChanged;

            protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

            private void Set<T>(ref T currentValue, T newValue, string propertyName)
            {
                if (currentValue?.Equals(newValue) ?? false) return;
                currentValue = newValue;
                OnPropertyChanged(propertyName);
            }

            #endregion

            #region ViewModel properties

            private Visibility _loggedInTabVisibility = Visibility.Hidden;
            public Visibility LoggedInTabVisibility
            {
                get => _loggedInTabVisibility;
                set => Set(ref _loggedInTabVisibility, value, nameof(LoggedInTabVisibility));
            }

            private MachineType _selectedMachineType;
            public MachineType SelectedMachineType
            {
                get => _selectedMachineType;
                set
                {
                    // Guard against a null incoming value (e.g. the DataGrid selection
                    // being cleared) - the previous code dereferenced value.System
                    // unconditionally and threw a NullReferenceException here.
                    if (!(_selectedMachineType?.System?.Equals(value?.System) ?? false))
                        _pingValues.Clear();
                    InitChart();
                    Set(ref _selectedMachineType, value, nameof(SelectedMachineType));
                }
            }

            private NIC _selectedNIC;
            public NIC SelectedNIC
            {
                get => _selectedNIC;
                set
                {
                    Set(ref _selectedNIC, value, nameof(SelectedNIC));
                }
            }

            private Visibility _ip_yes;
            public Visibility ip_yes
            {
                get => _ip_yes;
                set => Set(ref _ip_yes, value, nameof(ip_yes));
            }

            private Visibility _ip_warning;
            public Visibility ip_warning
            {
                get => _ip_warning;
                set => Set(ref _ip_warning, value, nameof(ip_warning));
            }

            private Visibility _ip_no;
            public Visibility ip_no
            {
                get => _ip_no;
                set => Set(ref _ip_no, value, nameof(ip_no));
            }

            private Visibility _speed_yes;
            public Visibility speed_yes
            {
                get => _speed_yes;
                set => Set(ref _speed_yes, value, nameof(speed_yes));
            }

            private Visibility _speed_warning;
            public Visibility speed_warning
            {
                get => _speed_warning;
                set => Set(ref _speed_warning, value, nameof(speed_warning));
            }

            private Visibility _jumbo_frame_yes;
            public Visibility jumbo_frame_yes
            {
                get => _jumbo_frame_yes;
                set => Set(ref _jumbo_frame_yes, value, nameof(jumbo_frame_yes));
            }

            private Visibility _jumbo_frame_maybe;
            public Visibility jumbo_frame_maybe
            {
                get => _jumbo_frame_maybe;
                set => Set(ref _jumbo_frame_maybe, value, nameof(jumbo_frame_maybe));
            }

            private Visibility _jumbo_frame_no;
            public Visibility jumbo_frame_no
            {
                get => _jumbo_frame_no;
                set => Set(ref _jumbo_frame_no, value, nameof(jumbo_frame_no));
            }

            #endregion

            #region Commands

            public RelayCommand SetToInterfaceCommand { get; } = new(item => {});

            #endregion

            #region Ping Chart

            private readonly ObservableCollection<double> _pingValues = [];
            public ISeries[] Series { get; set; }
            public Axis[] XAxes { get; set; }
            public Axis[] YAxes { get; set; }

            public void AddPing(double ping)
            {
                lock (_pingValues)
                {
                    _pingValues.Add(ping);
                    if (_pingValues.Count > 30)
                        _pingValues.RemoveAt(0);
                }
                InitChart();
            }

            private void InitChart()
            {
                // Main ping line - deep blue so it reads clearly against the
                // app's yellow accent (used for the warning threshold below)
                var pingSeries = new LineSeries<double>
                {
                    Values = _pingValues,
                    Stroke = new SolidColorPaint(new SKColor(0x0F, 0x6C, 0xBD)) { StrokeThickness = 2 },
                    Fill = new SolidColorPaint(new SKColor(0x0F, 0x6C, 0xBD, 30)),
                    GeometrySize = 0,
                    AnimationsSpeed = TimeSpan.Zero // disable animation
                };

                // Constant threshold lines
                var warningLine = new LineSeries<double>
                {
                    Values = [.. new double[30].Select(_ => 50)],
                    Stroke = new SolidColorPaint(new SKColor(0xE0, 0xA8, 0x00)) { StrokeThickness = 2 },
                    Fill = null,
                    GeometrySize = 0,
                    AnimationsSpeed = TimeSpan.Zero // disable animation
                };

                var criticalLine = new LineSeries<double>
                {
                    Values = [.. new double[30].Select(_ => 100)],
                    Stroke = new SolidColorPaint(new SKColor(0xC4, 0x2B, 0x1C)) { StrokeThickness = 2 },
                    Fill = null,
                    GeometrySize = 0,
                    AnimationsSpeed = TimeSpan.Zero // disable animation
                };

                Series = [pingSeries, warningLine, criticalLine];
                XAxes = [new Axis { Name = "Samples" }];
                YAxes = [new Axis { Name = "Ping (ms)" }];

                OnPropertyChanged(nameof(Series));
            }

            #endregion
        }

        #endregion

        #region Constructor

        public MainWindow()
        {
            InitializeComponent();

            Title = $"IP Setting for AM.CO.ZA Machines, Version {Version.shortVersion}";
            vm = new WindowViewModel(this);
            DataContext = vm;
            if (SkipLoginForTesting)
            {
                loggedIn = true;
                vm.LoggedInTabVisibility = Visibility.Visible;
            }
            Closed += (sender, e) => closed = true;

            // Windows 11 rounded window corners, and tinting the title bar to match the
            // app's current theme instead of leaving it the plain default OS color.
            // Both safely no-op on Windows 10 / older Windows 11 builds.
            SourceInitialized += (sender, e) =>
            {
                WindowBackdrop.Apply(this);
                WindowBackdrop.ApplyCaptionColor(this);
                // Live-follow the OS light/dark toggle while the app is running, instead of
                // only picking up the current theme at launch.
                WindowBackdrop.WatchForThemeChange(this, () =>
                {
                    App.ApplyTheme(App.IsSystemDarkTheme());
                    WindowBackdrop.ApplyCaptionColor(this);
                });
            };
        }

        #endregion

        #region Instance fields

        // A single shared HttpClient for the app's lifetime. The previous code created a new
        // HttpClient per request (including in a tight-ish version/countries/OTP/image-fetch
        // path) - a well-known .NET anti-pattern that can exhaust available sockets under load
        // because each HttpClient owns its own connection pool that isn't released promptly on
        // disposal (SocketException: "unable to connect" after enough churn).
        private static readonly HttpClient httpClient = new();

        #region Window state

        private bool closed;

        private readonly WindowViewModel? vm;

        #endregion

        #region DataGrid data

        private static IEnumerable<NIC>? nicData;

        private static IEnumerable<MachineType>? machineTypeData;

        #endregion

        #region UI state

        // TESTING ONLY - bypasses the WhatsApp OTP activation flow so the
        // Machine Type / Status tabs are reachable without a real phone
        // number. Set back to false before shipping.
        private const bool SkipLoginForTesting = true;

        private static bool loggedIn;

        #region DataGrids

        private NIC selectedNIC;

        private MachineType selectedMachineType;

        private bool populatingNICDataGrid;

        #endregion

        #region Set IP

        private bool failedToSetIPAddressUIUpdated;

        private bool pingStatusClosed;

        private string lastSetIpNicId; // the ID of the last NIC whose IP was attempted to be automatically set

        private long lastSetIpTime;

        private int setIpAttempts;

        #endregion

        #region Ping

        public long devicePingRoundtripTime;

        #endregion

        #endregion

        #endregion

        #region Properties

        private static long UnixTimestamp => (DateTime.UtcNow.Ticks - new DateTime(1970, 1, 1).Ticks) / 10_000_000;

        #endregion

        #region Methods

        #region Popups

        private static bool Confirm(string msg, string title)
        {
            return MessageBox.Show(msg, title, MessageBoxButton.OKCancel, MessageBoxImage.Warning) == MessageBoxResult.OK;
        }

        private static void Error(string msg, string title = "Error")
        {
            MessageBox.Show(msg, title, MessageBoxButton.OK, MessageBoxImage.Error);
        }

        private static void Message(string msg, string title = "Message")
        {
            MessageBox.Show(msg, title, MessageBoxButton.OK, MessageBoxImage.Information);
        }

        #endregion

        #region Login

        private async Task<bool> CheckLogin(bool withNotifications, bool onlyErrorNotifications)
        {
            if (SkipLoginForTesting)
            {
                loggedIn = true;
                (DataContext as WindowViewModel)?.LoggedInTabVisibility = Visibility.Visible;
                return true;
            }

            if (loggedIn)
            {
                if (withNotifications && !onlyErrorNotifications)
                    Message("Application activation complete.", "Login");
            }
            else
            {
                try
                {
                    var regKey = Registry.LocalMachine.OpenSubKey(@"Software\AM.CO.ZA\IP Setting for AM.CO.ZA Machines");
                    var phone = regKey?.GetValue("Phone")?.ToString();
                    var md5 = regKey?.GetValue("Login")?.ToString();
                    if (string.IsNullOrWhiteSpace(md5))
                    {
                        if (withNotifications)
                            Error("Application not activated.");
                        return false;
                    }
                    Increase(ref Notifications.loadingSpinnerNic);
                    var response = await httpClient.PostAsync("https://he3.co.za/support/ip/verify_login", new StringContent(JsonSerializer.Serialize(new { phone, md5 })));
                    Decrease(ref Notifications.loadingSpinnerNic);
                    if ((int)response.StatusCode >= 200 && (int)response.StatusCode <= 299)
                    {
                        loggedIn = true;
                        (DataContext as WindowViewModel)?.LoggedInTabVisibility = Visibility.Visible;
                        if (withNotifications && !onlyErrorNotifications)
                            Message("Application activation complete.", "Login");
                    }
                    else if (withNotifications)
                        Error("Application not activated.", "Login");
                }
                catch (Exception ex)
                {
                    if (withNotifications)
                        Error("Unable to check login, please try again later.");
                }
            }

            return loggedIn;
        }

        #region Login

        private async Task SendOTP()
        {
            using var regKey = Registry.LocalMachine.CreateSubKey(@"Software\AM.CO.ZA\IP Setting for AM.CO.ZA Machines");

            var phone = cbCountry1.SelectedValue + Regex.Replace(txtPhone1.Text, @"[0+\s]", "").Trim();
            if (!string.IsNullOrWhiteSpace(phone))
            {
                try
                {
                    regKey.SetValue("Phone", phone);
                    Increase(ref Notifications.loadingSpinnerNic);
                    var response = await httpClient.PostAsync("https://he3.co.za/whatsapp/send", new StringContent(JsonSerializer.Serialize(new { phone, template = "z_winapp_code", data = new { } })));
                    Decrease(ref Notifications.loadingSpinnerNic);
                    if (!((int)response.StatusCode >= 200 && (int)response.StatusCode <= 299))
                    {
                        Error("Unable to send Whatsapp message, please try again later.");
                        return;
                    }
                    cbCountry2.SelectedValue = cbCountry1.SelectedValue;
                    txtPhone2.Text = txtPhone1.Text.Trim();
                    Notifications.login = false;
                    Notifications.otp = true;
                    RefreshNotifications();
                    BtnResend.IsEnabled = false;
                    var sendTime = UnixTimestamp;
                    new Thread(() =>
                    {
                        while (sendTime > UnixTimestamp - 60)
                        {
                            Dispatcher.Invoke(() => BtnResend.Content = $"Resend ({sendTime - (UnixTimestamp - 60)})");
                            Thread.Sleep(1000);
                        }
                        Dispatcher.Invoke(() =>
                        {
                            BtnResend.Content = "Resend";
                            BtnResend.IsEnabled = true;
                        });
                    }).Start();
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(ex.Message);
                    Error("Unable to send Whatsapp message, please try again later.");
                    return;
                }
            }
        }

        private async Task ConfirmOTP()
        {
            using var regKey = Registry.LocalMachine.CreateSubKey(@"Software\AM.CO.ZA\IP Setting for AM.CO.ZA Machines");

            var phone = cbCountry1.SelectedValue + Regex.Replace(txtPhone1.Text, @"[0+\s]", "").Trim();
            var otp = txtOTP.Text;
            if (string.IsNullOrWhiteSpace(otp)) return;
            var hashBytes = MD5.HashData(Encoding.UTF8.GetBytes(otp));
            var sb = new StringBuilder();
            foreach (byte b in hashBytes)
                sb.Append(b.ToString("x2"));
            var md5 = sb.ToString();
            try
            {
                Increase(ref Notifications.loadingSpinnerNic);
                var response = await httpClient.PostAsync("https://he3.co.za/support/ip/verify_login", new StringContent(JsonSerializer.Serialize(new { phone, md5 })));
                Decrease(ref Notifications.loadingSpinnerNic);
                RefreshNotifications();
                if ((int)response.StatusCode >= 200 && (int)response.StatusCode <= 299)
                {
                    regKey.SetValue("Phone", phone);
                    regKey.SetValue("Login", md5);
                    loggedIn = true;
                    (DataContext as WindowViewModel)?.LoggedInTabVisibility = Visibility.Visible;
                    Message("Application activation complete.");
                    Notifications.otp = false;
                    RefreshNotifications();
                }
                else
                    Error("Invalid OTP, please try again later.");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
                Error("Unable to confirm OTP, please try again later.");
            }

        }

        #endregion

        #endregion

        #region UI state

        #region NIC

        private void RefreshUIForSelectedNIC(NIC currentSelectedNIC = null)
        {
            currentSelectedNIC = currentSelectedNIC ?? selectedNIC;
            Notifications.networkAdapter = !nicData.Any();
            Notifications.networkCable = !nicData.Any((dynamic item) => item.status == "Up");
            RefreshNotifications();
            NotificationSelectThenNext();
            vm?.SelectedNIC = selectedNIC = (NIC)nicDataGrid.SelectedItem;
            InitStatusImages();
            if (setIpAttempts > 0 && currentSelectedNIC?.id != selectedNIC?.id)
            {
                ClearStatus();
                tabMachineType.IsEnabled = false;
                tabStatus.IsEnabled = false;
            }
        }

        #endregion

        #region Status

        private void ClearStatus()
        {
            setIpAttempts = 0;
            lastSetIpNicId = null;
            selectedNIC?.selfPingFails = 0;
            failedToSetIPAddressUIUpdated = false;
            Status(false);
            NotificationSelectThenNext();
        }

        private bool IsIPConflict()
        {
            return selectedNIC != null && selectedMachineType != null && nicData.Any(nic => !nic.id.Equals(selectedNIC.id) && nic.ip.Equals(selectedMachineType.NicIP));
        }

        public void InitStatusImages()
        {
            if (selectedNIC == null || selectedMachineType == null)
            {
                vm.ip_yes = vm.ip_warning = vm.ip_no = vm.speed_yes = vm.speed_warning = vm.jumbo_frame_yes = vm.jumbo_frame_maybe = vm.jumbo_frame_no = Visibility.Collapsed;
                return;
            }

            // IP
            try
            {
                vm.ip_yes = !string.IsNullOrWhiteSpace(selectedMachineType.DeviceIP) && !string.IsNullOrWhiteSpace(selectedNIC.ip) && "255.255.255.0".Equals(selectedNIC.mask) && string.IsNullOrWhiteSpace(selectedNIC.gateway) && string.IsNullOrWhiteSpace(selectedNIC.dns1) && string.IsNullOrWhiteSpace(selectedNIC.dns2) && string.IsNullOrWhiteSpace(selectedNIC.dns2) && (selectedNIC.selfPingRoundtripTime <= 1) ? Visibility.Visible : Visibility.Collapsed;
            }
            catch
            {
                vm.ip_yes = Visibility.Collapsed;
            }
            try
            {
                vm.ip_warning = vm.ip_yes == Visibility.Collapsed && !string.IsNullOrWhiteSpace(selectedMachineType.DeviceIP) && !string.IsNullOrWhiteSpace(selectedNIC.ip) && (!"255.255.255.0".Equals(selectedNIC.mask) || !string.IsNullOrWhiteSpace(selectedNIC.gateway) || !string.IsNullOrWhiteSpace(selectedNIC.dns1) || !string.IsNullOrWhiteSpace(selectedNIC.dns2) || (selectedNIC.selfPingFails > 0)) ? Visibility.Visible : Visibility.Collapsed;
            }
            catch
            {
                vm.ip_warning = Visibility.Collapsed;
            }
            try
            {
                vm.ip_no = vm.ip_yes == Visibility.Collapsed && vm.ip_warning == Visibility.Collapsed && !string.IsNullOrWhiteSpace(selectedMachineType.DeviceIP) && !string.IsNullOrWhiteSpace(selectedNIC.ip) && (!"255.255.255.0".Equals(selectedNIC.mask) || !string.IsNullOrWhiteSpace(selectedNIC.gateway) || !string.IsNullOrWhiteSpace(selectedNIC.dns1) || !string.IsNullOrWhiteSpace(selectedNIC.dns2) || (selectedNIC.selfPingRoundtripTime > 1)) ? Visibility.Visible : Visibility.Collapsed;
            }
            catch
            {
                vm.ip_yes = Visibility.Collapsed;
                vm.ip_warning = Visibility.Collapsed;
                vm.ip_no = Visibility.Visible;
            }

            var speed = (string speed) =>
            {
                try
                {
                    var multiplier = Regex.Replace(speed?.ToUpper() ?? "", "[^A-Za-z]", "").FirstOrDefault();
                    return double.Parse(Regex.Replace(speed ?? "", "[^0-9.]", "")) * ('K' == multiplier ? 1000 : 'M' == multiplier ? 1_000_000 : 'G' == multiplier ? 1_000_000_000 : 'T' == multiplier ? 1_000_000_000_000 : 1);
                }
                catch
                {
                    return 0;
                }
            };

            // Network Speed
            if (!string.IsNullOrWhiteSpace(selectedMachineType.Speed))
            {
                var nicSpeed = speed(selectedNIC.speed ?? "");
                var machineTypeSpeed = speed(selectedMachineType.Speed);
                vm.speed_yes = nicSpeed >= machineTypeSpeed ? Visibility.Visible : Visibility.Collapsed;
                vm.speed_warning = nicSpeed < machineTypeSpeed ? Visibility.Visible : Visibility.Collapsed;
            }
            else
            {
                vm.speed_yes = vm.speed_warning = Visibility.Collapsed;
            }

            // Jumbo Frame
            // JumboFrame is a nullable CSV column (ip.models.MachineType.JumboFrame) - guard
            // every access with ?. so a blank cell doesn't throw a NullReferenceException here
            // (this block isn't wrapped in try/catch, so that used to crash the UI thread).
            var jumboRequired = !string.IsNullOrWhiteSpace(selectedMachineType.JumboFrame?.Replace("-", ""));
            vm.jumbo_frame_yes = !jumboRequired || (selectedMachineType.JumboFrame?.Equals(selectedNIC.jumboFrame) ?? false) || speed(selectedMachineType.JumboFrame) == speed(selectedNIC.jumboFrame) ? Visibility.Visible : Visibility.Collapsed;
            // "maybe": the NIC's jumbo frame setting couldn't be read at all, so we can't tell -
            // point the user at Device Manager to check/set it themselves.
            vm.jumbo_frame_maybe = vm.jumbo_frame_yes == Visibility.Collapsed && jumboRequired && string.IsNullOrWhiteSpace(selectedNIC.jumboFrame) ? Visibility.Visible : Visibility.Collapsed;
            // "no": the NIC's jumbo frame setting IS known and definitively doesn't match what's
            // required. Previously this had the exact same condition as "maybe" above, so the two
            // states always fired together and never distinguished "unknown" from "confirmed wrong".
            vm.jumbo_frame_no = vm.jumbo_frame_yes == Visibility.Collapsed && jumboRequired && !string.IsNullOrWhiteSpace(selectedNIC.jumboFrame) ? Visibility.Visible : Visibility.Collapsed;
        }

        private bool IsFailedToSetIPAddress()
        {
            return selectedNIC != null && selectedMachineType != null && selectedNIC.id.Equals(lastSetIpNicId) && (!selectedNIC.ip.Equals(selectedMachineType.NicIP) || !string.IsNullOrWhiteSpace(selectedNIC.gateway) || !string.IsNullOrWhiteSpace(selectedNIC.dns1) || !string.IsNullOrWhiteSpace(selectedNIC.dns2) || (selectedNIC.selfPingFails >= 5));
        }

        #endregion

        #region UI

        private void Refresh()
        {
            // Update NIC DataGrid
            PopulateNICData();

            if (IsFailedToSetIPAddress())
            {
                if (setIpAttempts == 1 && !failedToSetIPAddressUIUpdated)
                {
                    // Failed to set IP Address
                    failedToSetIPAddressUIUpdated = true;
                    Dispatcher.Invoke(() => NotificationFailedToSetIPAddress(true));
                }
                else if (setIpAttempts > 1 && lastSetIpTime < UnixTimestamp - 5)
                    // Failed to set IP Address, again
                    Dispatcher.Invoke(() =>
                    {
                        Decrease(ref Notifications.loadingSpinnerStatus);
                        NotificationFailedToSetIPAddressAgain();
                    });
            }
            else if (lastSetIpTime < UnixTimestamp - 5)
                Decrease(ref Notifications.loadingSpinnerStatus);

            // Update Ping Chart
            if (selectedNIC != null && selectedMachineType != null && !string.IsNullOrWhiteSpace(selectedMachineType?.DeviceIP?.Replace("-", "")))
            {
                long ping = 0;
                try
                {
                    var reply = new Ping().SendPingAsync(selectedMachineType.DeviceIP, 2000).Result;
                    devicePingRoundtripTime = reply.RoundtripTime;
                    if (reply.Status == IPStatus.Success)
                        ping = reply.RoundtripTime;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(ex.Message);
                }
                Dispatcher.Invoke(() => RefreshNotifications());
                vm?.AddPing(ping);
            }

            Dispatcher.Invoke(() =>
            {
                if (setIpAttempts > 0 && !pingStatusClosed)
                {
                    txtPingStatus.Text = $"{(string.IsNullOrWhiteSpace(selectedNIC?.ip) || !selectedMachineType.NicIP.Equals(selectedNIC?.ip) ? "" : $"Local PC IP address {selectedNIC.ip} set successfully.")}{(string.IsNullOrWhiteSpace(selectedMachineType.DeviceIP.Replace("-", "")) ? "" : $" Machine IP {selectedMachineType.DeviceIP}, PING {devicePingRoundtripTime}ms")}";
                    Notifications.pingStatus = true;
                }
            });

            // Self-ping test
            if (selectedNIC != null)
            {
                try
                {
                    var reply = new Ping().SendPingAsync(selectedNIC.ip, 2000).Result;
                    selectedNIC.selfPingRoundtripTime = reply.RoundtripTime;
                    if (reply.Status == IPStatus.Success)
                        selectedNIC.selfPingFails = 0;
                    else
                        ++selectedNIC.selfPingFails;
                }
                catch (Exception ex)
                {
                    ++selectedNIC.selfPingFails;
                    Console.Error.WriteLine(ex.Message);
                }
            }
        }

        #endregion

        #endregion

        #region NIC

        private static void Run(string cmd)
        {
            new Thread(() =>
            {
                // var directoryName = Regex.Replace(Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName), @"\\$", "").TrimEnd("\\").ToString();
                var directoryName = Path.GetTempPath().TrimEnd("\\").ToString();
                cmd += Environment.NewLine + $@"del {directoryName}\amcoza_ip_token";

                File.WriteAllText($@"{directoryName}\amcoza_ip_token", "");
                File.WriteAllText($@"{directoryName}\amcoza_ip_run.bat", cmd);
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = $@"cmd.exe",
                        // /c runs the batch and exits; the previous /k left cmd.exe sitting at an
                        // interactive prompt forever, leaking one orphaned process per IP-set attempt.
                        Arguments = $@"/c {directoryName}\amcoza_ip_run.bat",
                        UseShellExecute = false,
                        // Verb = "runas",
                        WorkingDirectory = directoryName,
                        ErrorDialog = true,
                        CreateNoWindow = true,
                        WindowStyle = ProcessWindowStyle.Minimized
                    }
                };
                process.Start();
                while (File.Exists($@"{directoryName}\amcoza_ip_token"))
                {
                    Thread.Sleep(1000);
                }
            }).Start();
        }

        private bool RunNetshCommand(bool dhcp, NIC nicItem, MachineType machineType)
        {
            if (nicItem == null || machineType == null)
                return false;

            var cmd =
                 dhcp
                 ?
                    $@"netsh interface ipv4 set address name=""{nicItem.name}"" source=dhcp" +
                    Environment.NewLine +
                    $@"netsh interface ipv4 set dnsservers name=""{nicItem.name}"" source=dhcp"
                 :
                    $@"netsh interface ipv4 set address name=""{nicItem.name}"" source=static addr={machineType.NicIP} mask=255.255.255.0" +
                    Environment.NewLine +
                    $@"netsh interface ip delete address name=""{nicItem.name}"" gateway=all" +
                    Environment.NewLine +
                    $@"netsh interface ip delete dns name=""{nicItem.name}"" all";
            ;

            lastSetIpTime = UnixTimestamp;
            Run(cmd);

            return true;
        }

        // NOTE: the previous implementation never actually matched on nicId - it returned the
        // "jumbo"-named property of the FIRST adapter subkey it found in registry enumeration
        // order, regardless of which NIC was being queried. That meant the Jumbo Frame Yes/Maybe/No
        // status shown to the user could reflect a completely different network adapter. Fixed by
        // matching each subkey's NetCfgInstanceId (the adapter GUID Windows stores there) against
        // the NIC's Id (NetworkInterface.Id uses the same GUID), and by actually using the
        // propertyName argument instead of a hardcoded "jumbo" literal.
        private string GetAdvancedNICProperty(string nicId, string propertyName)
        {
            using var adapters = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}");
            if (adapters != null)
                foreach (var adapter in adapters.GetSubKeyNames().Where(key => int.TryParse(key, out var result)))
                {
                    using var adapterAdvancedProperties = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Control\Class\{{4d36e972-e325-11ce-bfc1-08002be10318}}\{adapter}");
                    var instanceId = adapterAdvancedProperties?.GetValue("NetCfgInstanceId")?.ToString();
                    if (instanceId == null || !instanceId.Equals(nicId, StringComparison.OrdinalIgnoreCase))
                        continue;

                    foreach (var property in adapterAdvancedProperties.GetValueNames())
                        if (property.ToLower().Contains(propertyName.ToLower()))
                            return adapterAdvancedProperties.GetValue(property).ToString();
                    return null; // matched adapter, but it has no such advanced property
                }
            return null;
        }

        #endregion

        #region DataGrids populate

        [GeneratedRegex(@"^\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}$")]
        private static partial Regex IPRegex();

        public void PopulateNICData()
        {
            List<NIC> rows = [];
            try
            {
                // Get NIC info from WMI
                var searcher = new ManagementObjectSearcher("SELECT Name, NetEnabled, TimeOfLastReset FROM Win32_NetworkAdapter WHERE NetEnabled = TRUE");
                var wmiInfo = searcher.Get().Cast<ManagementObject>().ToList();

                foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces().OrderBy(nic => nic.Name))
                {
                    try
                    {
                        // Do not show adapters for localhost, Wi-Fi, VPN
                        if (new[] { NetworkInterfaceType.Loopback, NetworkInterfaceType.Ppp, NetworkInterfaceType.Loopback, NetworkInterfaceType.Wireless80211 }.Contains(nic.NetworkInterfaceType)
                            || (int)nic.NetworkInterfaceType == 53 // GenericTunnel
                            || new[] { "tun", "tap", "wireguard", "wg", "openvpn", "ovpn", "l2tp", "l2tp/ipsec", "ipsec", "ikev2", "ike", "pptp", "ppp", "pptp", "softether", "hamachi", "zerotier", "tailscale", "nordlynx", "nordvpn", "expressvpn", "vpngateway" }.Any(tunnel => $"{nic.Id} {nic.Name} {nic.Description}".ToLower().Contains(tunnel)))
                            continue;

                        var ipRegex = IPRegex();
                        var address = nic.GetIPProperties().UnicastAddresses.FirstOrDefault(addr => ipRegex.IsMatch(addr.Address.ToString() ?? ""));
                        var dhcp = nic.GetIPProperties().GetIPv4Properties().IsDhcpEnabled;

                        if (address != null)
                        {
                            static string speed(double speed) => (speed >= 1_000_000_000_000 ? (Math.Round(speed / 1_000_000_000_000, 2) + " Tbps") : (speed >= 1_000_000_000 ? (Math.Round(speed / 1_000_000_000, 2) + " Gbps") : (speed >= 1_000_000 ? (Math.Round(speed / 1_000_000, 2) + " Mbps") : (speed >= 1_000 ? (Math.Round(speed / 1_000, 2) + " Kbps") : (speed >= 1_000_000_000 ? (Math.Round(speed, 2) + " Bps") : "")))));

                            // UL/DL Rate
                            /*
                            string uploadSpeed = "", downloadSpeed = "";
                            var stats = nic.GetIPv4Statistics();
                            if (nicStatsData.TryGetValue(nic.Id, out object? value))
                            {
                                var nicData = (dynamic)value;
                                uploadSpeed = speed((stats.BytesSent - nicData.bytesSent) / (UnixTimestamp - nicData.time));
                                downloadSpeed = speed((stats.BytesReceived - nicData.bytesReceived) / (UnixTimestamp - nicData.time));
                            }
                            nicStatsData[nic.Id] = new { bytesSent = stats.BytesSent, bytesReceived = stats.BytesReceived, time = UnixTimestamp };
                            */

                            // Uptime
                            var resetTime = wmiInfo.FirstOrDefault(wmiNic => nic.Name.Equals(wmiNic["Name"]?.ToString()))?["TimeOfLastReset"]?.ToString();
                            string uptime = string.Empty;
                            if (!string.IsNullOrWhiteSpace(resetTime))
                            {
                                var timespan = DateTime.Now - ManagementDateTimeConverter.ToDateTime(resetTime);
                                uptime =
                                    (timespan.Days > 0 ? timespan.Days.ToString().PadLeft(2, '0') + ":" : "") +
                                    timespan.Hours.ToString().PadLeft(2, '0') + ":" +
                                    timespan.Minutes.ToString().PadLeft(2, '0') + ":" +
                                    timespan.Seconds.ToString().PadLeft(2, '0');
                            }

                            var operationalStatus = nic.OperationalStatus.ToString();
                            var ip = address.Address.ToString();
                            var gateway = nic.GetIPProperties().GatewayAddresses.FirstOrDefault(addr => ipRegex.IsMatch(addr.Address.ToString() ?? ""))?.Address.ToString();
                            var dns = nic.GetIPProperties().DnsAddresses.Where(address => ipRegex.IsMatch(address.ToString())).ToList();
                            var dns1 = dns.Count >= 1 ? dns[0]?.MapToIPv4().ToString() : null;
                            var dns2 = dns.Count >= 2 ? dns[1]?.MapToIPv4().ToString() : null;

                            var machineType = machineTypeData?.FirstOrDefault(machineType => machineType.NicIP.Equals(ip));
                            rows.Add(new NIC
                            {
                                id = nic.Id,
                                description = nic.Description,
                                name = nic.Name,

                                status = operationalStatus,
                                type = "Up".Equals(operationalStatus) ? (dhcp ? "DHCP" : "Static") : "Offline",

                                ip = ip,
                                gateway = gateway,
                                dns1 = dns1,
                                dns2 = dns2,

                                speed = speed(nic.Speed),
                                uptime = uptime,
                                jumboFrame = GetAdvancedNICProperty(nic.Id, "Jumbo"),

                                systemImage = machineType?.SystemImage,
                                systemImageVisibility = machineType == null ? Visibility.Collapsed : Visibility.Visible,
                                systemName = machineType?.System,
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine(ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
            }

            Dispatcher.Invoke(() =>
            {
                try
                {
                    var currentSelectedNIC = selectedNIC;
                    nicData = rows;
                    populatingNICDataGrid = true;
                    nicDataGrid.ItemsSource = rows;
                    nicDataGrid.SelectedIndex = rows.FindIndex(row => row.id.Equals(currentSelectedNIC?.id));
                    populatingNICDataGrid = false;
                    RefreshUIForSelectedNIC(currentSelectedNIC);
                    Decrease(ref Notifications.loadingSpinnerNic);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(ex.Message);
                }
            });
        }

        private async Task PopulateMachineTypeData()
        {
            Increase(ref Notifications.loadingSpinnerMachineType);
            try
            {
                // Was plain http:// - unencrypted, so the machine-type/device-IP mapping data (which
                // is later used to push network config) could be tampered with in transit. The image
                // fetch two lines below already correctly uses https on the same host.
                var data = await httpClient.GetStreamAsync("https://am.co.za/icon/ip/devices.csv");
                using var reader = new StreamReader(data);
                using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);
                var records = csv.GetRecords<MachineType>().ToList();
                foreach (var record in records.Where(record => !string.IsNullOrWhiteSpace(record.ID)))
                {
                    try
                    {
                        var imageBytes = await httpClient.GetByteArrayAsync($"https://am.co.za/icon/ip/{record.ID}.png");
                        using var stream = new MemoryStream(imageBytes);
                        var image = new BitmapImage();
                        image.BeginInit();
                        image.CacheOption = BitmapCacheOption.OnLoad;
                        image.StreamSource = stream;
                        image.EndInit();
                        image.Freeze();
                        record.SystemImage = image;
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine(ex.Message);
                    }
                }
                machineTypeData = records;
                machineTypeDataGrid.ItemsSource = records;
                Notifications.offline = false;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
                Notifications.offline = true;
            }
            Decrease(ref Notifications.loadingSpinnerMachineType);
        }

        #endregion

        #endregion

        #region Event handlers

        #region Window

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Continuous UI refresh
            // Refresh() only slept internally while actively pinging a selected device; with no
            // NIC/machine type selected yet (e.g. right after launch) this was an unthrottled busy
            // loop re-enumerating adapters and running WMI queries as fast as the CPU allowed. The
            // pacing now lives here unconditionally so every cycle - idle or active - is ~1s.
            new Thread(() =>
            {
                while (!closed)
                {
                    Refresh();
                    Thread.Sleep(1000);
                }
            }).Start();

            // Populate Machine Type DataGrid
            new Thread(() => Dispatcher.Invoke(async () => await PopulateMachineTypeData())).Start();

            // Version check
            new Thread(() =>
            {
                Dispatcher.Invoke(async () =>
                {
                    try
                    {
                        var latestVersion = await httpClient.GetStringAsync("https://he3.co.za/support/ip/version");
                        if (!string.IsNullOrWhiteSpace(latestVersion) && !Version.shortVersion.Equals(latestVersion))
                        {
                            NotificationVersion(latestVersion);
                        }
                    }
                    catch (Exception ex)
                    {
                        Notifications.offline = true;
                        RefreshNotifications();
                    }
                });
            }).Start();

            // Countries
            new Thread(() =>
            {
                Dispatcher.Invoke(async () =>
                {
                    Country[] countries;
                    try
                    {
                        countries = await httpClient.GetFromJsonAsync<Country[]>("https://he3.co.za/support/public/ip/countries");
                    }
                    catch
                    {
                        Notifications.offline = true;
                        return;
                    }
                    cbCountry1.ItemsSource = cbCountry2.ItemsSource = countries;
                    cbCountry1.SelectedIndex = cbCountry2.SelectedIndex = 0;
                    if (!await CheckLogin(false, false))
                    {
                        Notifications.login = true;
                        RefreshNotifications();
                    }
                });
            }).Start();
        }

        #endregion

        #region DataGrids

        private void NICDataGrid_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (!populatingNICDataGrid)
                RefreshUIForSelectedNIC();
        }

        private void MachineTypeDataGrid_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            vm?.SelectedMachineType = selectedMachineType = (MachineType)machineTypeDataGrid.SelectedItem;
            ClearStatus();
        }

        #endregion

        #region Notification buttons

        #region Network Interface

        private void BtnDownload_Click(object sender, RoutedEventArgs e)
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://www.SetIP.co.za") { UseShellExecute = true });
        }

        private void BtnNetworkAdapter_Click(object sender, RoutedEventArgs e)
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://buythis.co.za/ag-usb-net#ip") { UseShellExecute = true });
        }

        private void BtnNetworkCable_Click(object sender, RoutedEventArgs e)
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://buythis.co.za/ag-net-3#ip") { UseShellExecute = true });
        }

        private void BtnNext1_Click(object sender, RoutedEventArgs e)
        {
            if (selectedNIC != null)
            {
                tabStrip.SelectedIndex = 1;
                tabMachineType.IsEnabled = true;
                tabStatus.IsEnabled = false;
            }
        }

        private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        }

        #endregion

        #region Login

        private async void BtnConfirm_Click(object sender, RoutedEventArgs e)
        {
            await ConfirmOTP();
        }

        private async void BtnResend_Click(object sender, RoutedEventArgs e)
        {
            await SendOTP();
            var resendTime = UnixTimestamp;
            new Thread(() =>
            {
                Thread.Sleep(20000);
                if (!loggedIn)
                {
                    Message("Still haven't received the WhatsApp message?\n\nSend \"hi\" to +27 60 600 6000. If you get a reply, send \"resend\".");
                }
            }).Start();
        }

        private async void BtnSend_Click(object sender, RoutedEventArgs e)
        {
            await SendOTP();
        }

        private void ChangePhoneNumber(object sender, RoutedEventArgs e)
        {
            if (Confirm("Do you want to change your phone number?", "Change phone number"))
            {
                Notifications.otp = false;
                Notifications.login = true;
                RefreshNotifications();
            }
        }

        #endregion

        #region Machine Type

        private async void BtnNext2_Click(object sender, RoutedEventArgs e)
        {
            if (selectedMachineType != null)
            {
                tabStatus.IsEnabled = true;
                tabStrip.SelectedIndex = 2;
                if (await CheckLogin(true, true))
                {
                    if (IsIPConflict())
                        NotificationIPConflict();
                    else if (RunNetshCommand(false, selectedNIC, selectedMachineType))
                    {
                        setIpAttempts = 1;
                        lastSetIpNicId = selectedNIC.id;
                        failedToSetIPAddressUIUpdated = false;
                        Status(true);
                    }
                }
            }
        }

        private async void BtnTryAgain_Click(object sender, RoutedEventArgs e)
        {
            await PopulateMachineTypeData();
        }

        #endregion

        #region Status

        private void BtnClosePingStatus_Click(object sender, RoutedEventArgs e)
        {
            pingStatusClosed = true;
            Notifications.pingStatus = false;
            RefreshNotifications();
        }

        #region IP Conflict

        private async void BtnRelease_Click(object sender, RoutedEventArgs e)
        {
            if (Confirm("Press OK to release the IP Address from the conflicting network interface.", "Release"))
            {
                Increase(ref Notifications.loadingSpinnerStatus);
                Notifications.ipConflict = false;
                RefreshNotifications();
                if (selectedNIC != null && selectedMachineType != null)
                    foreach (var nic in nicData)
                        if (!nic.id.Equals(selectedNIC.id) && selectedMachineType.NicIP.Equals(nic.ip))
                            RunNetshCommand(true, nic, selectedMachineType);
                new Thread(() =>
                {
                    Thread.Sleep(3000);
                    Dispatcher.Invoke(() =>
                    {
                        Decrease(ref Notifications.loadingSpinnerStatus);
                        RefreshNotifications();
                        tabStrip.SelectedIndex = 0;
                        tabMachineType.IsEnabled = false;
                        tabStatus.IsEnabled = false;
                    });
                }).Start();
            }
        }

        #endregion

        #region Failed to set IP Address

        private async void BtnRetrySetIP_Click(object sender, RoutedEventArgs e)
        {
            if (selectedNIC != null && selectedMachineType != null)
            {
                Increase(ref Notifications.loadingSpinnerStatus);
                NotificationFailedToSetIPAddress(false);
                if (RunNetshCommand(false, selectedNIC, selectedMachineType))
                    ++setIpAttempts;
            }
        }

        private async void BtnDeleteAndReboot_Click(object sender, RoutedEventArgs e)
        {
            if (Confirm("Press OK to confirm to delete the network adapter and reboot.", "Delete and reboot"))
            {
                var searcher = new ManagementObjectSearcher($"SELECT * FROM Win32_NetworkAdapter WHERE NetConnectionID='{selectedNIC.name.Replace("'", "''")}'");
                foreach (var adapter in searcher.Get().Cast<ManagementObject>())
                    adapter.InvokeMethod("Delete", null);
                Process.Start("shutdown", "/r /t 5");
            }
        }

        #endregion

        #endregion

        #endregion

        #endregion

        #region Notifications

        private void Increase(ref int spinner)
        {
            lock (Notifications._lock)
                ++spinner;
            Dispatcher.Invoke(() => RefreshNotifications());
        }

        private void Decrease(ref int spinner)
        {
            lock (Notifications._lock)
                spinner = Math.Max(0, spinner - 1);
            Dispatcher.Invoke(() => RefreshNotifications());
        }

        private void RefreshNotifications()
        {
            var show = Visibility.Visible;
            var hide = Visibility.Collapsed;

            ntfFailedToSetIPAddress.Visibility =
            ntfFailedToSetIPAddressAgain.Visibility =
            ntfIpConflict.Visibility =
            ntfLoadingSpinnerMachineType.Visibility =
            ntfLoadingSpinnerNic.Visibility =
            ntfLoadingSpinnerStatus.Visibility =
            ntfLogin.Visibility =
            ntfNetworkAdapter.Visibility =
            ntfNetworkCable.Visibility =
            ntfOffline.Visibility =
            ntfOTP.Visibility =
            ntfPingStatus.Visibility =
            ntfSelectThenNext1.Visibility =
            ntfSelectThenNext2.Visibility =
            ntfVersion.Visibility =
            hide;

            // Network Interface
            if (Notifications.loadingSpinnerNic > 0)
                ntfLoadingSpinnerNic.Visibility = show;
            else if (Notifications.offline)
                ntfOffline.Visibility = show;
            else if (Notifications.version)
                ntfVersion.Visibility = show;
            else if (Notifications.login)
                ntfLogin.Visibility = show;
            else if (Notifications.otp)
                ntfOTP.Visibility = show;
            else if (Notifications.networkAdapter)
                ntfNetworkAdapter.Visibility = show;
            else if (Notifications.networkCable)
                ntfNetworkCable.Visibility = show;
            else
                ntfSelectThenNext1.Visibility = show;

            // Machine Type
            if (Notifications.loadingSpinnerMachineType > 0)
                ntfLoadingSpinnerMachineType.Visibility = show;
            else
                ntfSelectThenNext2.Visibility = show;

            // Status
            if (Notifications.loadingSpinnerStatus > 0)
                ntfLoadingSpinnerStatus.Visibility = show;
            else if (Notifications.ipConflict)
                ntfIpConflict.Visibility = show;
            else if (Notifications.failedToSetIpAddress)
                ntfFailedToSetIPAddress.Visibility = show;
            else if (Notifications.failedToSetIpAddressAgain)
                ntfFailedToSetIPAddressAgain.Visibility = show;
            else if (Notifications.pingStatus)
                ntfPingStatus.Visibility = show;
        }

        #region Network Interface

        private void NotificationSelectThenNext()
        {
            var show = nicData?.Any(static item => item.status == "Up") ?? false;
            var isValidSelection = selectedNIC?.status == "Up";
            // Previously hardcoded the light theme's literal colors here (#FFF3C4 / #8A6400),
            // bypassing the theme system entirely - this banner would've stayed pale yellow
            // with near-black text even in dark mode. Look the current theme's brushes up by
            // resource key instead, same as everything driven from XAML.
            ntfSelectThenNext1.Background = isValidSelection ? Brushes.Transparent : (Brush)TryFindResource("AccentBrushLight");
            ntfSelectThenNext1Text.Foreground = isValidSelection ? (Brush)TryFindResource("TextPrimaryBrush") : (Brush)TryFindResource("AccentBrushDarker");
            ntfSelectThenNext1Button.IsEnabled = isValidSelection;
        }

        private void NotificationVersion(string latestVersion)
        {
            ntfVersionText.Text = $"New version {latestVersion} is now available. Please download it from www.SetIP.co.za.";
            Notifications.version = true;
            RefreshNotifications();
        }

        #endregion

        #region Status

        private void Status(bool showSuccess)
        {
            pingStatusClosed = false;
            SuccessStatus.Visibility = showSuccess ? Visibility.Visible : Visibility.Collapsed;
            PingChart.Visibility = showSuccess ? Visibility.Visible : Visibility.Collapsed;
            ErrorIPConflict.Visibility = Visibility.Collapsed;
            ErrorFailedToSetIPAddress.Visibility = Visibility.Collapsed;
            Notifications.ipConflict = false;
            Notifications.failedToSetIpAddress = false;
            Notifications.failedToSetIpAddressAgain = false;
            Decrease(ref Notifications.loadingSpinnerStatus);
            Notifications.pingStatus = false;
            RefreshNotifications();
        }

        #region Failed to set IP Address

        private void NotificationFailedToSetIPAddress(bool show)
        {
            pingStatusClosed = false;
            ErrorIPConflict.Visibility = Visibility.Collapsed;
            ErrorFailedToSetIPAddress.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            SuccessStatus.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
            PingChart.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
            Notifications.failedToSetIpAddress = show;
            Notifications.pingStatus = false;
            RefreshNotifications();
        }

        private void NotificationFailedToSetIPAddressAgain()
        {
            ErrorIPConflict.Visibility = Visibility.Collapsed;
            ErrorFailedToSetIPAddress.Visibility = Visibility.Visible;
            SuccessStatus.Visibility = Visibility.Collapsed;
            PingChart.Visibility = Visibility.Collapsed;
            Notifications.failedToSetIpAddress = false;
            Notifications.failedToSetIpAddressAgain = true;
            RefreshNotifications();
        }

        #endregion

        #region IP Conflict

        private void NotificationIPConflict()
        {
            ErrorIPConflict.Visibility = Visibility.Visible;
            ErrorFailedToSetIPAddress.Visibility = Visibility.Collapsed;
            SuccessStatus.Visibility = Visibility.Collapsed;
            PingChart.Visibility = Visibility.Collapsed;
            Notifications.ipConflict = true;
            Notifications.failedToSetIpAddress = false;
            Notifications.failedToSetIpAddressAgain = false;
            RefreshNotifications();
        }

        #endregion

        #endregion

        #endregion
    }
}