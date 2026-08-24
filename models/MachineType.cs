using CsvHelper.Configuration.Attributes;
using System.Windows.Media.Imaging;

namespace ip.models
{
    public class MachineType
    {
        [Ignore]
        public BitmapImage? SystemImage { get; set; }
        public string? ID { get; set; }
        public string? System { get; set; }
        [Name("NIC IP")]
        public string? NicIP { get; set; }
        [Name("Jumbo Frame")]
        public string? JumboFrame { get; set; }
        public string? Speed { get; set; }
        [Name("Device IP")]
        public string? DeviceIP { get; set; }
    }

}
