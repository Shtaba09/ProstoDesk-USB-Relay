# ProstoDesk USB Relay

Experimental USB device redirection for ProstoDesk.

Goal: a USB device physically connected to the remote ProstoDesk client should appear on the controller PC as a normal local USB device. The first target is printer/MFP service software that needs vendor-specific USB commands, not ordinary remote printing.

## Current MVP

The repository contains a buildable transport/orchestration prototype:

- **Host** reads device state from `usbipd-win` (`usbipd state`), binds/unbinds a selected device and provides a LAB-only raw USB/IP bridge.
- **Controller** exposes a temporary loopback USB/IP endpoint, forwards it to the Host bridge, invokes `usbip-win2`, parses the assigned virtual USB port and detaches that exact port on exit.
- **Common** contains duplex stream forwarding and conservative device-policy helpers.

Production ProstoDesk will replace the LAB TCP hop with an authenticated/encrypted ProstoDesk session channel. No TCP/3240 exposure is required in the final design.

## Intended UX

1. Operator opens an active ProstoDesk session and presses **USB**.
2. Client USB devices are listed with name, VID:PID, state and a Connect button.
3. Operator requests a device.
4. Remote user receives an explicit consent dialog unless a trusted policy explicitly allows unattended USB redirection.
5. After consent, ProstoDesk binds the device and opens a dedicated USB relay channel.
6. On the operator PC, `usbip-win2` creates the virtual USB device and Windows loads the normal vendor driver.
7. Session end, denial, disconnect or explicit **Disconnect** tears the mapping down and releases the device.

## Build

Requires .NET SDK 10:

```powershell
dotnet build ProstoDesk.UsbRelay.slnx -c Release
```

No NuGet dependencies are used by the MVP.

## LAB test (LAN only)

> **Warning:** `lab-bridge --allow-lan` is intentionally not authenticated or encrypted. Use it only on a trusted isolated LAN. Never expose it to the Internet.

On the remote/client PC (Administrator):

```powershell
ProstoDesk.UsbRelay.Host.exe devices
ProstoDesk.UsbRelay.Host.exe bind 3-2
ProstoDesk.UsbRelay.Host.exe lab-bridge --listen 0.0.0.0:3241 --allow-lan
```

On the operator PC with usbip-win2 installed:

```powershell
ProstoDesk.UsbRelay.Controller.exe lab-mount 3-2 --remote 192.168.1.50:3241
```

Press `Ctrl+C` to detach the virtual device.

## Safety defaults

- HID/input devices are treated as sensitive and must never be silently redirected by default.
- Production channel must be session-bound and authenticated.
- Production Host should run privileged operations in a small service, not in the UI process.
- Device ownership is released on session termination and after transport failure.

## License

ProstoDesk USB Relay is licensed under GNU GPL v3. Third-party components retain their own licenses; see [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
