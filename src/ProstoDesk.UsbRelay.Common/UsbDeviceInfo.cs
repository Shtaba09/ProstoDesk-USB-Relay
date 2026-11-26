using System.Text.RegularExpressions;

namespace ProstoDesk.UsbRelay.Common;

public sealed record UsbDeviceInfo(
    string? BusId,
    string Description,
    string InstanceId,
    bool IsForced,
    Guid? PersistedGuid,
    string? StubInstanceId,
    string? ClientIpAddress,
    string? VendorId,
    string? ProductId)
{
    public bool IsConnected => !string.IsNullOrWhiteSpace(BusId);
    public bool IsBound => PersistedGuid is not null;
    public bool IsAttached => !string.IsNullOrWhiteSpace(ClientIpAddress);

    public static (string? Vid, string? Pid) ParseVidPid(string instanceId)
    {
        var m = Regex.Match(instanceId ?? string.Empty,
            @"VID_([0-9A-F]{4}).*PID_([0-9A-F]{4})",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return m.Success
            ? (m.Groups[1].Value.ToUpperInvariant(), m.Groups[2].Value.ToUpperInvariant())
            : (null, null);
    }
}
