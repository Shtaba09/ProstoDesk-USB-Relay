using System;

namespace ProstoDesk.UsbRelay.Common
{
    public static class UsbRelayPolicy
    {
        public static bool RequiresExplicitUserConsent(UsbDeviceInfo device)
        {
            if (device == null) throw new ArgumentNullException("device");
            return true;
        }

        public static bool LooksLikeSensitiveInputDevice(UsbDeviceInfo device)
        {
            if (device == null) throw new ArgumentNullException("device");
            var description = (device.Description ?? string.Empty).ToLowerInvariant();
            return description.Contains("keyboard") ||
                   description.Contains("mouse") ||
                   description.Contains("hid") ||
                   description.Contains("input device");
        }
    }
}
