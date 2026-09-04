using System.Windows.Media;

namespace ip.models
{
    public class PingRecordItem
    {
        public long PingMs { get; set; }
        public string LatencyText { get; set; } = "";
        public string Timestamp { get; set; } = "";
        public Brush StatusBrush { get; set; } = Brushes.Transparent;
        public Brush StatusBgBrush { get; set; } = Brushes.Transparent;
        public string TierName { get; set; } = "";
        public bool IsTimeout { get; set; }
        public string TooltipText { get; set; } = "";
    }
}
