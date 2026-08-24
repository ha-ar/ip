using System.Windows;
using System.Windows.Media.Imaging;

namespace ip.models
{
    public class NIC
    {
        #region Adapter Type

        public string description { get; set; }

        public string id { get; set; }

        public string name { get; set; }

        public string speed { get; set; }

        #endregion

        #region IP Settings

        public string dns1 { get; set; }

        public string dns2 { get; set; }

        public string gateway { get; set; }

        public string ip { get; set; }

        public string jumboFrame { get; set; }

        public string mask { get; set; }

        #endregion

        #region Status

        public string status { get; set; } // Up, Down

        public string type { get; set; } // Static, DHCP, Offline

        public string uptime { get; set; }

        #endregion

        #region Machine Type

        public BitmapImage systemImage { get; set; } // Machine Type image, if nic.ip == machineType.NicIP

        public Visibility systemImageVisibility { get; set; } // visible, if nic.ip == machineType.NicIP

        public string systemName { get; set; } // Machine Type name (hover tooltip), if nic.ip == machineType.NicIP

        #endregion

        #region Ping

        public int selfPingFails;

        public long selfPingRoundtripTime;

        #endregion
    }
}
