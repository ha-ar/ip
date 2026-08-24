namespace ip.models
{
    public sealed class Notifications
    {
        private Notifications() { }

        public static bool
            failedToSetIpAddress,
            failedToSetIpAddressAgain,
            ipConflict,
            login,
            networkAdapter,
            networkCable,
            offline,
            otp,
            pingStatus,
            version
        ;

        public static int
            loadingSpinnerMachineType,
            loadingSpinnerNic,
            loadingSpinnerStatus;

        public static object _lock = new();
    }
}
