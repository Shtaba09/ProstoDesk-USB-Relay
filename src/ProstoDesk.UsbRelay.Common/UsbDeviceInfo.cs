using System;
using System.Text.RegularExpressions;

namespace ProstoDesk.UsbRelay.Common
{
    public sealed class UsbDeviceInfo
    {
        public UsbDeviceInfo(
            string busId,
            string description,
            string instanceId,
            bool isForced,
            Guid? persistedGuid,
            string stubInstanceId,
            string clientIpAddress,
            string vendorId,
            string productId)
        {
            BusId = busId;
            Description = description ?? string.Empty;
            InstanceId = instanceId ?? string.Empty;
            IsForced = isForced;
            PersistedGuid = persistedGuid;
            StubInstanceId = stubInstanceId;
            ClientIpAddress = clientIpAddress;
            VendorId = vendorId;
            ProductId = productId;
        }

        public string BusId { get; private set; }
        public string Description { get; private set; }
        public string InstanceId { get; private set; }
        public bool IsForced { get; private set; }
        public Guid? PersistedGuid { get; private set; }
        public string StubInstanceId { get; private set; }
        public string ClientIpAddress { get; private set; }
        public string VendorId { get; private set; }
        public string ProductId { get; private set; }

        public bool IsConnected { get { return !string.IsNullOrWhiteSpace(BusId); } }
        public bool IsBound { get { return PersistedGuid.HasValue; } }
        public bool IsAttached { get { return !string.IsNullOrWhiteSpace(ClientIpAddress); } }

        public static void ParseVidPid(string instanceId, out string vid, out string pid)
        {
            var match = Regex.Match(
                instanceId ?? string.Empty,
                @"VID_([0-9A-F]{4}).*PID_([0-9A-F]{4})",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            if (match.Success)
            {
                vid = match.Groups[1].Value.ToUpperInvariant();
                pid = match.Groups[2].Value.ToUpperInvariant();
                return;
            }

            vid = null;
            pid = null;
        }
    }
}
