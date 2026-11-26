namespace ProstoDesk.UsbRelay.Common;

public static class UsbRelayPolicy
{
    public static bool RequiresExplicitUserConsent(UsbDeviceInfo device) => true;

    public static bool LooksLikeSensitiveInputDevice(UsbDeviceInfo device)
    {
        var d = device.Description.ToLowerInvariant();
        return d.Contains("keyboard") || d.Contains("mouse") || d.Contains("hid") || d.Contains("input device");
    }
}
